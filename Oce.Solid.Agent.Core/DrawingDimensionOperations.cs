using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// QUOTE SU UNA VISTA DI TAVOLA, CREATE — non importate (voce K10).
    ///
    /// ── PERCHE' ESISTE, E PERCHE' NON SI CHIAMA «IMPORTA» ───────────────
    ///
    /// `model_dimensions` **importa** le quote che il modello ha gia', con
    /// `InsertModelAnnotations*`. Misurato il 18/9/2026: porta **zero**, su un
    /// pezzo con sette quote tutte marcate per il disegno, su **otto** modelli
    /// di tavola diversi (sette di serie piu' quello dell'officina), con cinque
    /// strade di import e il controllo positivo verde nello stesso giro
    /// (`InsertNote` sulla stessa tavola funziona). Quella strada e' chiusa.
    ///
    /// Ma il fine non e' «le quote del modello in tavola»: e' **una tavola
    /// quotata**. *Un nome che descrive il MEZZO invece del fine restringe le
    /// soluzioni prima che qualcuno le cerchi* — e per due giorni il nome
    /// dell'altra operazione ha deciso la strada al posto mio.
    ///
    /// ── LA MISURA CHE HA APERTO QUESTA STRADA ───────────────────────────
    ///
    ///     spigoli visibili nella vista: 7
    ///       spigolo 0..3: AddDimension -> un oggetto,  quote 0 -> 4
    ///
    ///     COSA MISURANO (pezzo 120 x 80 x 10, smusso 5):
    ///       RD1  120 mm     RD2  120 mm
    ///       RD3    5 mm     RD4    5 mm
    ///
    /// Quattro su quattro, **fuori processo**, e con i valori giusti. Sono
    /// quote di riferimento (`RD`), che e' il tipo giusto per una tavola.
    ///
    /// **E il valore si rilegge, non si presume.** Il primo giro della sonda
    /// rispondeva `?` al valore, e con quel `?` la strada sarebbe sembrata
    /// aperta lo stesso: *quattro quote comparse non sono quattro quote
    /// giuste*, e una quota che misura un'altra cosa e' peggio di una quota che
    /// manca — sulla carta si legge uguale.
    ///
    /// ── LE DUE TRAPPOLE, TUTTE E DUE GIA' PAGATE DA QUESTO PROGETTO ─────
    ///
    /// 1. `swInputDimValOnCreate` **era acceso** su questa installazione.
    ///    Lasciato cosi', `AddDimension` apre la finestra «Modifica» e la
    ///    chiamata COM **non torna piu'**: il ponte si appende, e il client
    ///    dietro con lui. Si spegne prima e si **rimette com'era in un
    ///    `finally`** — una preferenza lasciata spenta cambia SOLIDWORKS per
    ///    chi lo usa dopo, e nessuno collegherebbe la cosa a questa operazione.
    /// 2. Le coordinate di `AddDimension` sono del **FOGLIO**, non del modello.
    ///    Sbagliarle mette la quota fuori dal foglio **senza dare errore**.
    ///    Qui si prendono da `IView.GetOutline()`, che e' l'ingombro della
    ///    vista sul foglio, e si scalano in metri.
    ///
    /// ── GLI SPIGOLI SI CHIEDONO ALLA VISTA ──────────────────────────────
    ///
    ///     Object IView.GetVisibleEntities2(Component2 comp, Int32 entityType)
    ///
    /// con `swViewEntityType_e.swViewEntityType_Edge`. Non si indovinano
    /// coordinate su cui cliccare: si prende l'elenco che la vista stessa
    /// espone e si seleziona con `IEntity.Select4`.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingDimension(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: le quote stanno su una " +
                    "vista di disegno. Crea prima la tavola con «create_drawing».");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "La tavola non ha viste: le quote non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «standard_views» o «model_view».");

            string wanted = Str(a, "view", null);
            string viewReason;
            IView view = ResolveView(drawing, names, wanted, out viewReason);
            if (viewReason != null) return Failed(viewReason);
            if (view == null)
                return Failed("Non riesco a prendere la vista su cui quotare.");

            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }

            // ── GLI SPIGOLI CHE LA VISTA MOSTRA ─────────────────────────
            object[] edges = null;
            try
            {
                edges = view.GetVisibleEntities2(
                    null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            }
            catch (Exception ex)
            {
                return Failed("Non riesco a chiedere gli spigoli alla vista: " + ex.Message);
            }

            // ZERO-E-ZERO: l'eccezione ha gia' il suo rifiuto qui sopra; null e'
            // una vista senza spigoli visibili, e lo zero e' vero.
            int available = edges == null ? 0 : edges.Length;
            if (available == 0)
                return Failed(
                    "La vista «" + viewName + "» non mostra nessuno spigolo quotabile. " +
                    "Su una vista vuota, o su una vista in cui il modello non e' " +
                    "risolto, non c'e' niente da quotare — e mettere zero quote " +
                    "rispondendo «fatto» sarebbe peggio.");

            // QUALI SPIGOLI. Senza dirlo si quotano i primi, e quanti si dice.
            var chosen = new List<int>();
            var requestedList = StringList(a, "edges");
            if (requestedList.Count > 0)
            {
                foreach (var o in requestedList)
                {
                    int i;
                    if (!int.TryParse((o ?? "").Trim(), out i))
                        return Failed("«edges» vuole numeri, e «" + o + "» non lo e'.");
                    if (i < 0 || i >= available)
                        return Failed(
                            "Lo spigolo " + i + " non c'e': la vista «" + viewName +
                            "» ne mostra " + available + " (da 0 a " + (available - 1) + ").");
                    if (!chosen.Contains(i)) chosen.Add(i);
                }
            }
            else
            {
                int howMany = (int)Num(a, "count", 4);
                if (howMany <= 0)
                    return Failed("«count» dev'essere almeno 1: zero quote non sono una quota.");
                for (int i = 0; i < Math.Min(howMany, available); i++) chosen.Add(i);
            }

            // ── DOVE METTERLE, in coordinate FOGLIO ─────────────────────
            //
            // IL CONTORNO SI LEGGE UNA VOLTA SOLA, prima di piazzare qualunque
            // quota. Se `GetOutline()` crescesse insieme alle annotazioni
            // attaccate alla vista, rileggerlo a ogni giro farebbe scappare le
            // quote sempre piu' lontano. Non e' verificato che cresca: per
            // questo la risposta riporta il contorno di prima E quello di
            // dopo, cosi' chi legge puo' vederlo invece di fidarsi.
            double[] contour = null;
            try { contour = view.GetOutline() as double[]; } catch { }
            if (contour == null || contour.Length < 4)
                return Failed(
                    "Non riesco a leggere l'ingombro della vista sul foglio, e senza " +
                    "quello le quote finirebbero in un punto qualunque — fuori dal " +
                    "foglio, se va male, e senza nessun errore.");

            double dimensionOffset = Num(a, "offset_mm", 12) / 1000.0;
            double planStep = Num(a, "pitch_mm", 10) / 1000.0;

            // ── COME STA OGNI SPIGOLO SUL FOGLIO ────────────────────────
            //
            // Prima si guarda, poi si decide dove va la quota. La stesura
            // precedente saltava questo passo e metteva TUTTE le quote alla
            // stessa X — il centro della vista — scendendo di un passo per
            // ognuna: per uno spigolo verticale SOLIDWORKS costruisce allora
            // una quota la cui linea di misura passa per quella X, cioe' **in
            // mezzo al disegno**. E' il difetto che si vedeva a occhio.
            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null)
                return Failed(
                    "Non riesco a leggere come la vista porta il modello sul foglio: senza, le " +
                    "quote finirebbero nel punto sbagliato, e senza nessun errore.");
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;

            // TUTTI GLI SPIGOLI DELLA VISTA, UNA VOLTA SOLA (voce GT11). I fori e
            // gli smussi si cercano su tutti, non sui primi «count»: una
            // piastra con quattro fori non smette di averli perche' si sono
            // chiesti quattro spigoli.
            var all = new List<ProjectedEdge>();
            var circles = new List<Circle>();
            int holesInTable = 0;   // ZERO-E-ZERO: nessuna tabella fori nella vista, finche' non si guarda
            for (int i = 0; i < available; i++)
            {
                var c = CircleOnSheet(edges[i] as IEdge, i, mu, tr);
                if (c != null) { circles.Add(c); continue; }
                var s = ReadEdge(view, edges[i], i, mu, tr);
                if (s != null) all.Add(s);
            }

            // LE QUOTE CHE LA TAVOLA HA GIA', IN TUTTE LE VISTE (voce GT11). «Ciascun
            // elemento dell'oggetto deve essere quotato non piu' di una volta»
            // vale per la TAVOLA, non per la vista: il 25/9/2026 la tavola della
            // piastra portava il 10 nella vista frontale e in quella laterale, e
            // il 120 nella frontale e in quella dall'alto.
            // EVERY SHEET since Z-C21.2 (finding N43), only the views of this model
            // and configuration, and without the dimensions hidden by hand (C8-2):
            // what was read is in the answer, see KeysAlreadyDimensioned.
            var already = KeysAlreadyDimensioned(drawing, view, out var existingRead);

            var failed = new List<string>();
            var externalDiameters = new List<Dictionary<string, object>>();
            var undimensionedBorders = new List<string>();
            var cutting = new List<string>();
            RoundOutcome round = null;
            var measuredValues = new List<ProjectedEdge>();
            var duplicates = new List<string>();
            var elsewhere = new List<string>();
            var onChamfer = new List<string>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in chosen)
            {
                if (circles.Exists(c => c.Index == i)) continue;   // i fori hanno la loro strada, qui sotto
                var s = all.Find(t => t.Index == i);
                if (s == null) { failed.Add(i + " (non riesco a misurarlo sul foglio)"); continue; }

                // LO SMUSSO NON SI QUOTA COI SUOI SPIGOLI. La prima tavola
                // guardata aveva lo smusso quotato 7,071 in obliquo (l'ipotenusa)
                // e la catena 5 + 75 accanto all'80: si scrive 5 x 45°, con la
                // quota da smusso, e le «gambe» — i tratti accorciati dallo
                // smusso — le dicono la quota da smusso e l'ingombro insieme.
                if (s.ChamferFeature != null)
                {
                    onChamfer.Add(i + " (spigolo dello smusso «" + s.ChamferFeature + "»: va con la quota da smusso)");
                    continue;
                }
                if (IsChamferLeg(s, all))
                {
                    onChamfer.Add(i + " (accorciato dallo smusso: lo dicono la quota da smusso e l'ingombro)");
                    continue;
                }

                // UNA QUOTA PER ELEMENTO, E LA CHIAVE NON E' IL VALORE.
                //
                // «Ciascun elemento dell'oggetto deve essere quotato non piu'
                // di una volta» (UNI ISO 129-1, nella scheda Sammarone per
                // Zanichelli): un rettangolo vuole DUE quote, non quattro, e
                // 120/10/120/10 e' quotatura ripetuta — la fonte la
                // didascalizza «quindi errata».
                //
                // Ma deduplicare sul VALORE sarebbe sbagliato: due gradini
                // diversi da 10 mm sono due elementi distinti e vanno quotati
                // tutti e due. La chiave e' geometrica — come sta lo spigolo e
                // DOVE sta la sua proiezione — cosi' i due lati opposti di un
                // rettangolo si riconoscono come la stessa misura, e due
                // gradini uguali in posti diversi no.
                if (seen.ContainsKey(s.Key))
                {
                    duplicates.Add(i + " (stessa misura dello spigolo " + seen[s.Key] + ")");
                    continue;
                }
                if (s.InModel != null && already.Contains(s.InModel))
                {
                    elsewhere.Add(i + " (" + Math.Round(s.Length * 1000.0 / scale, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                " mm: la stessa estensione e' gia' quotata in un'altra vista)");
                    continue;
                }
                seen[s.Key] = i;
                if (s.InModel != null) already.Add(s.InModel);
                measuredValues.Add(s);
            }

            // DALLE PICCOLE ALLE GRANDI, allontanandosi dal pezzo: «le linee di
            // misura andranno disegnate in ordine progressivo, dalle minori
            // alle maggiori, allontanandosi dal contorno delle figure».
            measuredValues.Sort((x, y) => x.Length.CompareTo(y.Length));

            int before = CountDimensions(drawing);
            if (before < 0)
                return Failed(
                    "Non riesco a contare le quote della tavola prima di cominciare: senza " +
                    "quel numero non saprei dirti quali ho messo io. Non ho toccato niente.");

            // ── LA TRAPPOLA DEL MODALE, spenta e rimessa ────────────────
            bool askedForTheValue = true;
            bool off = false;
            var born = new List<Dictionary<string, object>>();
            var holes = new List<Dictionary<string, object>>();
            var chamfers = new List<Dictionary<string, object>>();
            try
            {
                try
                {
                    askedForTheValue = _sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    _sw.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    off = true;
                }
                catch { /* se non si puo' spegnere lo dira' il primo che si appende */ }

                // UN CONTATORE PER FASCIA, non uno solo per tutte. La stesura
                // precedente ne aveva uno globale, e cosi' la seconda quota
                // scendeva di un passo anche se andava a destra.
                int below = 0, sideways = 0, onLeft = 0;

                // ── I FORI, PRIMA DI TUTTO (voce GT11) ──────────────────
                //
                // La prima tavola guardata non quotava i fori: ne' il diametro
                // ne' la posizione, cioe' proprio cio' che in officina serve di
                // piu'. Il diametro con il RICHIAMO DEL FORO (`AddHoleCallout2`,
                // «Ø8 PASSANTE»), one per diameter AND depth (DimensionTheHoles), col numero dei fori uguali
                // davanti («4× »: il prefisso si aggiunge a quello che c'e', che
                // e' il simbolo del diametro — sostituirlo lo cancellava,
                // misurato). La posizione con quote dai due spigoli di
                // riferimento, il sinistro e quello in basso: in parallelo,
                // quindi senza catene e senza quote sovrabbondanti. Misurato il
                // 25/9/2026: cerchio + spigolo con `AddHorizontalDimension2`
                // misura dal CENTRO (15 sulla piastra), con `AddDimension` non
                // nasce niente.
                var sheetDimensions = SheetDimensions(drawing);
                // UNA TABELLA FORI NELLA VISTA QUOTA GIA' I FORI (voce GT5, 26/9/2026).
                // La prima tavola con la tabella, GUARDATA, aveva i fori due volte:
                // posizioni e misure nella tabella, e di nuovo richiami e quote di
                // posizione sulla vista. «Ciascun elemento si quota una volta sola»:
                // se la tabella c'e', i fori sono suoi.
                holesInTable = HolesInHoleTable(drawing);
                var trueHoles = circles.FindAll(c => c.Type == "foro");
                if (trueHoles.Count > 0 && holesInTable <= 0)
                    DimensionTheHoles(model, drawing, trueHoles, all, already, contour, dimensionOffset, planStep, scale,
                               ref below, ref sideways, ref onLeft, sheetDimensions, born, holes, elsewhere, failed);
                DimensionTheContours(model, drawing, circles.FindAll(c => c.Type == "contorno"), already, contour, dimensionOffset, planStep,
                               born, externalDiameters, undimensionedBorders, elsewhere, failed);
                foreach (var c in circles)
                    if (c.Type == "di_taglio") cutting.Add("Ø" + c.DiameterMm.ToString("0.##", Ci));
                // IL TONDO VISTO DI FIANCO (voce GT14): cerchi di taglio coassiali
                // vogliono dire un pezzo tornito nella vista longitudinale —
                // lunghezze fra le facce e diametri dalle generatrici.
                round = DimensionTheRoundFromTheSide(model, drawing, view, mu, tr, circles.FindAll(c => c.Type == "di_taglio"),
                                             already, contour, dimensionOffset, planStep, ref below, ref sideways, born, elsewhere, failed);

                foreach (var s in measuredValues)
                {
                    int dimensionsBefore = CountDimensions(drawing);
                    try
                    {
                        model.ClearSelection2(true);
                        var ent = edges[s.Index] as IEntity;
                        if (ent == null) { failed.Add(s.Index + " (non e' un'entita')"); continue; }
                        if (!ent.Select4(false, null)) { failed.Add(s.Index + " (non si seleziona)"); continue; }

                        // ── DOVE VA QUESTA QUOTA ────────────────────────
                        //
                        // «Devono essere parallele alla dimensione a cui si
                        // riferiscono» e «devono, per quanto possibile, essere
                        // disposte all'esterno delle figure» (scheda
                        // Zanichelli, DISPOSIZIONE DELLE LINEE DI MISURA).
                        // Quindi: orizzontali SOTTO, verticali DI LATO.
                        double x, y;
                        if (s.Facing == "horizontal")
                        {
                            x = s.Half;
                            y = contour[1] - dimensionOffset - below * planStep;
                            below++;
                        }
                        else if (s.Facing == "vertical")
                        {
                            x = contour[2] + dimensionOffset + sideways * planStep;
                            y = s.Half;
                            sideways++;
                            // DENTRO LA CORNICE: una vista sul bordo destro del
                            // foglio manda la fascia fuori dal disegno. Allora a
                            // sinistra — stessa regola dei fori.
                            if (sheetDimensions != null && x > sheetDimensions[0] - 0.015)
                            {
                                sideways--;
                                x = contour[0] - dimensionOffset - onLeft * planStep;
                                onLeft++;
                            }
                        }
                        else
                        {
                            // OBLIQUO: non c'e' una fascia giusta, e metterlo
                            // con le orizzontali lo farebbe sembrare tale. Va
                            // a sinistra, e la risposta lo dice.
                            x = contour[0] - dimensionOffset - onLeft * planStep;
                            y = (contour[1] + contour[3]) / 2;
                            onLeft++;
                        }

                        var nature = model.Extension.AddDimension(x, y, 0, 0)
                                     as IDisplayDimension;
                        try { model.EditRebuild3(); } catch { }

                        if (NotGrown(drawing, dimensionsBefore, nature))
                        {
                            // NON SI GUARDA SE `AddDimension` HA RISPOSTA: si
                            // guarda se la quota c'e'. E' la regola di casa —
                            // il valore che torna da chi ha fatto la cosa non
                            // e' la misura della cosa.
                            failed.Add(s.Index + " (chiamata andata, quota non nata)");
                            continue;
                        }

                        var described = DescribeDimension(nature, s.Index);
                        described["orientation"] = s.Facing;
                        described["position_mm"] = WhereItEnded(nature);
                        born.Add(described);
                    }
                    catch (Exception ex)
                    {
                        var m = ex.Message ?? "";
                        failed.Add(s.Index + " (" + (m.Length > 90 ? m.Substring(0, 90) + "..." : m) + ")");
                    }
                }

                // ── GLI SMUSSI, CON LA QUOTA DA SMUSSO (voce GT11) ─────
                DimensionTheChamfers(model, drawing, edges, all, already, contour, dimensionOffset, born, chamfers, elsewhere, failed);
            }
            finally
            {
                if (off)
                    try
                    {
                        _sw.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue);
                    }
                    catch { }
                try { model.ClearSelection2(true); } catch { }
            }

            int after = CountDimensions(drawing);
            if (after < 0)
                return Failed(
                    "Ho messo le quote nella vista «" + viewName + "», ma poi non riesco a " +
                    "ricontarle: non so dirti quante ne sono nate. Guarda la tavola prima di " +
                    "andare avanti.");
            int put = after - before;

            if (put <= 0)
            {
                // NIENTE DA QUOTARE NON E' LA STESSA COSA DI «NON SONO RIUSCITO».
                // Se tutto cio' che la vista mostra e' gia' quotato altrove, o e'
                // un doppione, o sta nello smusso, si dice cosi' — e resta un
                // rifiuto, perche' zero quote non sono una risposta «fatto».
                if (failed.Count == 0 && cutting.Count > 0 && measuredValues.Count == 0 && round == null &&
                    duplicates.Count + elsewhere.Count + onChamfer.Count == 0 && holesInTable <= 0)
                    return Failed(
                        "Nella vista «" + viewName + "» gli unici spigoli sono cerchi visti di taglio (" +
                        string.Join(", ", cutting) + ") e non stanno su uno stesso asse: non e' un pezzo tondo " +
                        "visto di fianco che io sappia quotare. Il diametro di un cerchio lo quoto dalla vista in " +
                        "cui e' un cerchio (di taglio, AddDiameterDimension2 dice 60 per un Ø30).");
                if (failed.Count == 0 && ((duplicates.Count + elsewhere.Count + onChamfer.Count) > 0 || holesInTable > 0))
                    return Failed(
                        "Nella vista «" + viewName + "» non c'e' niente da quotare che non lo sia " +
                        "gia': " + duplicates.Count + " doppioni nella vista, " + elsewhere.Count +
                        " gia' quotati in un'altra vista, " + onChamfer.Count + " dello smusso" +
                        (holesInTable > 0 ? ", " + holesInTable + " fori nella tabella fori" : "") + ". " +
                        "Ciascun elemento si quota una volta sola in tutta la tavola (UNI ISO 129-1).");
                return Failed(
                    "Nessuna quota e' nata (erano " + before + ", sono " + after + "). " +
                    "La vista «" + viewName + "» mostra " + available + " spigoli e ne " +
                    "ho provati " + chosen.Count +
                    (failed.Count == 0 ? "" : ": " + string.Join(", ", failed)) + ". " +
                    "Se la finestra «Modifica» e' rimasta accesa, la chiamata si appende " +
                    "invece di fallire: qui non e' successo, quindi e' altro.");
            }

            double[] afterContour = ContourOf(view);

            // ── I TESTI CHE SI ACCAVALLANO (voce GT12, punto 3) ─────────────
            // Il rettangolo vero di ogni testo, letto da IDisplayData
            // (OperazioniTestiQuote): testo su testo in tutta la tavola, testo
            // su una linea del pezzo in questa vista.
            var dimensionsTexts = DimensionTexts(drawing);
            var overlapping = TextsThatOverlap(drawing, view, dimensionsTexts);

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["edges_available"] = available,
                ["edges_tried"] = chosen.Count,
                ["dimensions_before"] = before,
                ["dimensions_after"] = after,
                ["dimensions_placed"] = put,
                // OGNI QUOTA COL SUO VALORE RILETTO, in millimetri. E' il pezzo
                // che distingue questa operazione da una che disegna numeri.
                ["dimensions"] = born.ToArray(),
                // E LE STESSE MISURE IN FILA, SENZA L'ORDINE.
                //
                // `dimensions[0].mm` costringe chi prova a sapere **in che ordine**
                // la vista elenca i suoi spigoli — un ordine che nessuno
                // dichiara, e che cambia col pezzo. Il 18/9/2026 un caso di
                // banco e' stato scritto su `quote[0]` e `quote[2]` prendendo i
                // numeri da una sonda fatta su un ALTRO pezzo: sette spigoli
                // invece di quattro, e l'indice non voleva piu' dire niente.
                //
                // Qui ci sono i millimetri e basta: si chiede «c'e' il 120?»
                // invece di «il terzo e' 5?», e la domanda resta vera anche se
                // SOLIDWORKS cambia l'ordine.
                // *Un'attesa che dipende da un ordine non dichiarato misura
                // l'ordine, non la cosa.*
                ["dimensions_mm"] = MillimetersInARow(born),
                ["edges_not_dimensioned"] = failed.ToArray(),

                // ── FORI, SMUSSI, E CIO' CHE ERA GIA' QUOTATO (voce GT11) ──
                //
                // Nati il 25/9/2026 da una tavola GUARDATA: i fori non erano
                // quotati, lo smusso era un 7,071 obliquo, il 10 e il 120
                // comparivano in due viste. Le attese del banco erano tutte
                // verdi, perche' chiedevano i millimetri delle quote messe —
                // non se la tavola basta a fare il pezzo.
                ["holes"] = holes.ToArray(),
                ["holes_in_hole_table"] = holesInTable > 0 ? (object)holesInTable : null,
                // I CERCHI CHE NON SONO FORI (26/9/2026): il contorno di un pezzo
                // tondo ha una quota di diametro, non un richiamo; un cerchio visto
                // di taglio non si quota da qui.
                ["outer_diameters"] = externalDiameters.ToArray(),
                ["borders_not_dimensioned"] = undimensionedBorders.ToArray(),
                ["cut_circles"] = cutting.ToArray(),
                // IL TONDO VISTO DI FIANCO (voce GT14): lunghezze in parallelo dalla
                // prima faccia, diametri dalle generatrici; e i diametri che la
                // vista non espone, detti.
                ["round_axis"] = round?.Axis,
                ["round_lengths_mm"] = round == null ? null : round.Lengths.ToArray(),
                ["round_diameters_mm"] = round == null ? null : round.Diameters.ToArray(),
                ["round_diameters_not_reached"] = round == null ? null : round.NotReached.ToArray(),
                ["round_notes"] = round == null ? null : round.Note.ToArray(),
                ["round_generatrices_without_face"] = round == null ? null : (object)round.FacelessGeneratrices,
                ["chamfers"] = chamfers.ToArray(),
                ["already_dimensioned_elsewhere"] = elsewhere.ToArray(),
                // WHAT «ALREADY DIMENSIONED» WAS READ FROM (Z-C21.2, findings N43 and
                // C8-2). «every sheet» when IDrawingDoc.GetViews handed over the views
                // of all the sheets, «current sheet only» when it did not and the walk
                // fell back to the sheet in front; «interrupted» when the walk broke
                // off, and then the two counts are null (not known), not zero. The
                // counts are what was left out on purpose: dimensions hidden by hand
                // (not on the paper), and views of another model or configuration
                // (their model coordinates are not this part's).
                ["already_dimensioned_read_from"] =
                    (existingRead.EverySheet ? "every sheet" : "current sheet only") +
                    (existingRead.Complete ? "" : ", interrupted: some views were not read"),
                ["hidden_dimensions_not_counted"] = existingRead.Complete ? (object)existingRead.HiddenByHand : null,
                ["views_of_other_models_not_counted"] = existingRead.Complete ? (object)existingRead.ViewsOfAnotherModel : null,
                ["chamfer_edges"] = onChamfer.ToArray(),

                // ── I DOPPIONI SI DICONO, NON SI NASCONDONO ─────────────
                //
                // Chiedere quattro spigoli di un rettangolo e vedersene
                // quotare due e' sorprendente finche' non si sa perche'. La
                // ragione e' una regola di disegno, non un difetto: «ciascun
                // elemento dell'oggetto deve essere quotato non piu' di una
                // volta» (UNI ISO 129-1, scheda Sammarone/Zanichelli), e
                // 120/10/120/10 e' quotatura ripetuta — «quindi errata».
                ["edges_discarded_duplicates"] = duplicates.ToArray(),

                // IL CONTORNO PRIMA E DOPO. Non e' accertato che `GetOutline()`
                // cresca con le annotazioni attaccate alla vista: se i due
                // numeri differiscono, cresce, e chi mettera' altre quote dopo
                // di noi deve saperlo invece di scoprirlo.
                ["contour_view_mm"] = InMillimeters(contour),
                ["contour_view_after_mm"] = InMillimeters(afterContour),
                // IL CONFRONTO LO FA IL CODICE, non chi scrive l'attesa. Un
                // caso di banco puo' pretendere un valore, non paragonare due
                // campi fra loro: mettere qui il paragone gia' fatto e' l'unico
                // modo perche' diventi una riga verificabile.
                ["contour_grown"] = GrownContour(contour, afterContour),
                // A WALK THAT BROKE OFF IS «NOT KNOWN» (Z-C21.8, finding N47):
                // DimensionTexts gives null then, and the three fields are null,
                // not the count of the texts read before the break.
                ["texts_read"] = dimensionsTexts == null ? null : (object)dimensionsTexts.Count,
                ["overlapping_texts"] = overlapping == null ? null : overlapping.ToArray(),
                // Nessun testo letto su una tavola con quote e' «non lo so», non
                // «nessuno si accavalla» (revisione del 26/9/2026).
                ["how_many_texts_overlap"] = dimensionsTexts == null || overlapping == null ||
                                                  (dimensionsTexts.Count == 0 && after > 0)
                    ? null : (object)overlapping.Count,
                ["offset_used_mm"] = Math.Round(dimensionOffset * 1000, 2),
                ["pitch_used_mm"] = Math.Round(planStep * 1000, 2),

                ["where_placed"] =
                    "FUORI dalla sagoma della vista, in coordinate foglio: le " +
                    "orizzontali sotto, le verticali di lato, le oblique a sinistra, " +
                    "in ordine crescente allontanandosi dal pezzo. Viene da due " +
                    "criteri di norma — la linea di misura e' parallela alla " +
                    "dimensione a cui si riferisce, e sta per quanto possibile " +
                    "all'esterno delle figure (UNI ISO 129-1, nella scheda " +
                    "Sammarone/Zanichelli) — piu' la distanza: oltre 10 mm dal " +
                    "contorno e passo costante fra 7 e 12 su A3/A4. Dentro la cornice " +
                    "del foglio: se da un lato non c'e' posto, dall'altro. I fori col " +
                    "richiamo e la posizione dai bordi di riferimento (sinistro e in " +
                    "basso: lo zero e' l'angolo in basso a sinistra, lo stesso della " +
                    "tabella fori), attaccata al foro piu' vicino al lato della quota; gli " +
                    "smussi con la quota da smusso, fuori dal pezzo dalla parte dello smusso.",

                ["what_it_does_not_check"] =
                    "QUALI quote servono, e se bastano. La norma distingue quote " +
                    "funzionali, non funzionali e ausiliarie, e una quota funzionale " +
                    "non si deve dedurre da altre: quello lo sa il progettista. So " +
                    "dire quante ne ho messe, non se il pezzo e' definito.\n" +
                    "Il TESTO delle quote lo guardo (dal 26/9/2026): il suo rettangolo " +
                    "vero, letto da SOLIDWORKS, contro gli altri testi della tavola e le " +
                    "linee del pezzo in questa vista — «overlapping_texts». Non " +
                    "guardo le linee di misura e di richiamo, ne' il cartiglio. " +
                    "E non guardo: se una quota di posizione dei fori andava presa da un " +
                    "altro riferimento funzionale (prendo i bordi); quale sistema " +
                    "adottare fra in serie, in parallelo e a quote sovrapposte; se una " +
                    "quota andava fra parentesi come ausiliaria. Tolleranze, rugosita' " +
                    "e riferimenti geometrici non li metto.\n" +
                    "Quotare bene resta un mestiere: queste regole tolgono i doppioni " +
                    "e portano le quote fuori dal pezzo, non fanno una tavola d'autore.",
            };

            _log("[agente] " + put + " quote sulla vista «" + viewName + "»: " +
                 ValuesInLine(born));
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// Uno spigolo, misurato SUL FOGLIO invece che nel modello.
        ///
        /// Serve a sapere due cose prima di quotare: da che parte sta — e
        /// quindi dove va la sua quota — e dove sta, per riconoscere che due
        /// spigoli diversi misurano lo stesso elemento.
        /// </summary>
        private sealed class ProjectedEdge
        {
            public int Index;
            /// <summary>«horizontal», «vertical» oppure «obliquo».</summary>
            public string Facing;
            /// <summary>Il centro della proiezione, in coordinate foglio.</summary>
            public double Half;
            public double Length;
            /// <summary>
            /// Verso piu' intervallo proiettato, arrotondato al centesimo di
            /// millimetro. Due spigoli con la stessa chiave misurano lo stesso
            /// elemento: i lati opposti di un rettangolo. Due gradini uguali
            /// in posti diversi hanno chiavi diverse, e si quotano tutti e due.
            /// </summary>
            public string Key;
            /// <summary>Gli estremi sul foglio e nel modello, in metri.</summary>
            public double[] FromSheet, ToSheet, FromModel, ToModel;
            /// <summary>Asse e intervallo nel MODELLO: la chiave fra viste. Null se obliquo.</summary>
            public string InModel;
            /// <summary>La lavorazione di smusso a cui appartiene, o null.</summary>
            public string ChamferFeature;
            /// <summary>L'entita' come la espone la vista, per selezionarla.</summary>
            public object Entity;
        }

        /// <summary>
        /// Da spigolo del modello a spigolo sul foglio. `null` se non si puo'
        /// misurare, che non e' un errore: si dice e si va avanti.
        ///
        /// ── PERCHE' SERVE UNA TRASFORMAZIONE ────────────────────────────
        ///
        /// `IEdge.GetCurveParams3()` da' gli estremi in coordinate del
        /// MODELLO. La vista li mostra ruotati e in scala, e una quota si
        /// mette in coordinate FOGLIO: senza passare per
        /// `IView.ModelToViewTransform` si classificherebbe come orizzontale
        /// uno spigolo che sul foglio e' verticale, e la quota finirebbe dalla
        /// parte sbagliata.
        ///
        /// PER LA SOLA DIREZIONE BASTA LA PARTE ROTATORIA: una traslazione non
        /// cambia il verso di un vettore. Per il PUNTO invece servirebbe anche
        /// la traslazione, e non e' accertato che quella matrice porti la
        /// posizione della vista sul foglio — per questo le coordinate
        /// assolute si prendono da `GetOutline()`, che e' gia' in coordinate
        /// foglio, e da qui esce solo il centro RELATIVO lungo l'asse di
        /// misura, riportato dentro l'ingombro.
        /// </summary>
        private static ProjectedEdge ReadEdge(IView view, object entity, int index, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var edge = entity as IEdge;
                if (edge == null) return null;
                var p = ApiHelpers.EdgeParameters(edge);
                if (p == null) return null;
                var da = p.StartPoint as double[];
                var a = p.EndPoint as double[];
                if (da == null || a == null || da.Length < 3 || a.Length < 3) return null;

                // SUL FOGLIO CON LA TRASFORMAZIONE INTERA, e la stesura di prima
                // sbagliava due volte (misurato il 25/9/2026, quando la tavola e'
                // stata GUARDATA e si sono visti i numeri sopra le linee):
                //
                //   1. usava la matrice TRASPOSTA: per una vista ruotata la Y
                //      usciva specchiata attorno al centro — nella vista
                //      dall'alto la striscia dello smusso cadeva a y 70,7
                //      invece di 145,7, e il «5» si scriveva in fondo alla
                //      vista, con una linea di misura lunga quanto il pezzo;
                //   2. sommava `IView.Position` al posto della traslazione della
                //      matrice. Sono uguali solo se il pezzo e' centrato
                //      sull'origine: per una piastra alta 10 estrusa da 0, la
                //      vista frontale la metteva fra 229,5 e 239,5 mentre sta
                //      fra 224,5 e 234,5 (il contorno della vista e' simmetrico
                //      di 5,94 mm). Il testo dei «10» verticali finiva 5 mm piu'
                //      su, SOPRA la linea di richiamo.
                //
                // Le orizzontali si salvavano perche' la piastra era centrata in
                // X. `MultiplyTransform` fa il conto come lo fa SOLIDWORKS, e il
                // punto esce gia' in coordinate foglio.
                double[] fromV = OnSheet(da, mu, tr), aV = OnSheet(a, mu, tr);
                if (fromV == null || aV == null) return null;
                double dx = aV[0] - fromV[0], dy = aV[1] - fromV[1];
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) return null;

                // LA SOGLIA E' UNA SCELTA DI CODICE, NON UNA PRESCRIZIONE.
                // Le fonti parlano di parallelismo come criterio geometrico
                // esatto; un numero ci vuole lo stesso, e si dichiara. Un
                // millesimo sulla componente normalizzata: sotto, lo spigolo
                // e' parallelo a un asse entro un decimo di grado.
                const double THRESHOLD = 1e-3;
                var s = new ProjectedEdge { Index = index, Length = len };
                double a0, a1;
                if (Math.Abs(dy) / len < THRESHOLD)
                {
                    s.Facing = "horizontal";
                    a0 = Math.Min(fromV[0], aV[0]); a1 = Math.Max(fromV[0], aV[0]);
                }
                else if (Math.Abs(dx) / len < THRESHOLD)
                {
                    s.Facing = "vertical";
                    a0 = Math.Min(fromV[1], aV[1]); a1 = Math.Max(fromV[1], aV[1]);
                }
                else
                {
                    s.Facing = "obliquo";
                    a0 = Math.Min(fromV[0], aV[0]); a1 = Math.Max(fromV[0], aV[0]);
                }
                s.Half = (a0 + a1) / 2;
                s.Key = s.Facing + "|" + Math.Round(a0 * 100000) + "|" + Math.Round(a1 * 100000);
                s.FromSheet = fromV;
                s.ToSheet = aV;
                s.FromModel = da;
                s.ToModel = a;
                s.InModel = KeyInModel(da, a);
                s.ChamferFeature = TouchesAChamfer(edge);
                s.Entity = entity;
                return s;
            }
            catch { return null; }
        }

        /// <summary>Un cerchio intero nella vista: un foro, il contorno di un pezzo tondo, o un cerchio visto di taglio.</summary>
        private sealed class Circle
        {
            public int Index;
            public IEdge Edge;
            public double[] SheetCenter;   // metri
            public double DiameterMm;
            public string Type;             // "foro", "contorno", "di_taglio"
            public bool? ThroughAll;          // dalla geometria; null se non si capisce
            public double DepthMm = double.NaN;
            public double[] ModelCenter;  // metri, nel modello
            public double[] ModelAxis;    // versore, nel modello
        }

        private static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Il cerchio sul foglio, se lo spigolo e' un cerchio intero; null altrimenti.</summary>
        private static Circle CircleOnSheet(IEdge e, int index, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var p = ApiHelpers.EdgeParameters(e, out ICurve curva);
                if (curva == null || !curva.IsCircle()) return null;
                var da = p?.StartPoint as double[];
                var a = p?.EndPoint as double[];
                // UN CERCHIO INTERO ha inizio e fine nello stesso punto: un arco
                // (un raccordo, una asola) non e' un foro da richiamare.
                if (da == null || a == null ||
                    Math.Abs(da[0] - a[0]) + Math.Abs(da[1] - a[1]) + Math.Abs(da[2] - a[2]) > 1e-8) return null;
                var cp = curva.CircleParams as double[];
                if (cp == null || cp.Length < 7) return null;
                var center = OnSheet(new[] { cp[0], cp[1], cp[2] }, mu, tr);
                if (center == null) return null;
                // CHE CERCHIO E' (26/9/2026, una boccola tornita): il contorno Ø30
                // di un pezzo tondo prendeva il richiamo «Ø30 ↧30» come fosse un
                // foro, e i due cerchi della boccola visti di taglio finivano fra
                // i «fori il cui richiamo non e' nato». Un foro guarda chi legge
                // (l'asse, portato sul foglio, e' perpendicolare al foglio) ED e'
                // un anello interno di una faccia piana — il criterio delle croci
                // di centro (EUnForoCheSiVede).
                string typ;
                var axis = ((IMathVector)((IMathVector)mu.CreateVector(new[] { cp[3], cp[4], cp[5] })).MultiplyTransform(tr)).ArrayData as double[];
                // ZERO-E-ZERO: un asse che non si porta sul foglio vale lunghezza zero
                // apposta — il cerchio finisce fra quelli di taglio, cioe' NON quotato
                // e detto nella risposta, invece di prendere un richiamo di foro.
                double l = axis == null ? 0 : Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
                if (l < 1e-12 || Math.Abs(axis[2]) / l < 0.99) typ = "di_taglio";
                // UN ANELLO INTERNO NON BASTA (revisione avversariale del 27/9/2026): lo
                // e' anche il piede di una borchia, e un cerchio di tangenza Ø24 al
                // piede di una borchia Ø20 raccordata diventava «un foro» con le sue
                // quote di posizione. Una borchia ha una quota di diametro.
                else typ = IsAVisibleHole(e, curva, mu, tr) && !IsTheFootOfAStud(e) ? "foro" : "contorno";
                var c = new Circle { Index = index, Edge = e, SheetCenter = center, DiameterMm = Math.Round(cp[6] * 2000.0, 3), Type = typ,
                                      ModelCenter = new[] { cp[0], cp[1], cp[2] }, ModelAxis = new[] { cp[3], cp[4], cp[5] } };
                if (typ == "foro")
                {
                    double prof;
                    c.ThroughAll = ThroughAllFromGeometry(e, out prof);
                    c.DepthMm = prof;
                }
                return c;
            }
            catch { return null; }
        }

        /// <summary>
        /// Lo spigolo tocca uno spigolo di smusso con un estremo (nel modello)?
        /// Allora e' un tratto ACCORCIATO dallo smusso — il 75 accanto all'80,
        /// il 5 accanto al 10 — e la sua misura la dicono la quota da smusso e
        /// l'ingombro insieme: quotarlo e' una quota in piu'.
        /// </summary>
        private static bool IsChamferLeg(ProjectedEdge s, List<ProjectedEdge> all)
        {
            foreach (var t in all)
            {
                if (t.ChamferFeature == null || ReferenceEquals(t, s)) continue;
                foreach (var p in new[] { s.FromModel, s.ToModel })
                    foreach (var q in new[] { t.FromModel, t.ToModel })
                        if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) return true;
            }
            return false;
        }

        /// <summary>
        /// Cosa e' GIA' quotato nella tavola, in tutte le viste: le estensioni
        /// del modello (asse e intervallo) delle quote su uno spigolo solo, i
        /// fori per diametro, gli smussi per lavorazione. Si legge dagli
        /// spigoli a cui le quote sono attaccate, non dai numeri: due 10
        /// diversi sono due elementi.
        ///
        /// THE WHOLE DRAWING, AS THE CATALOGUE PROMISES (Z-C21.2, finding N43).
        /// Until library 0.140.0 the walk started at IDrawingDoc.GetFirstView,
        /// which is «the first drawing view on the current sheet»
        /// (IDrawingDoc~GetFirstView.html): a second sheet with a view of the
        /// same part got the 120 and the 10 again. Now the views of every sheet
        /// (ApiHelpers.ViewsOfEverySheet, IDrawingDoc.GetViews); when GetViews
        /// does not hand them over, the current sheet as before, and
        /// <paramref name="reading"/> says which.
        ///
        /// ONLY THE VIEWS OF THIS MODEL. The keys are model coordinates, and on
        /// another sheet (one part per sheet is the common layout) a different
        /// plate 120 long from the origin has the same key as this one: its 120
        /// would silently take this part's 120 away. A view whose model and
        /// configuration differ from those of <paramref name="view"/> is left
        /// out; one that does not say counts, as every view of the current
        /// sheet did (ApiHelpers.MayShowTheSameModel).
        ///
        /// NOT THE DIMENSIONS HIDDEN BY HAND (Z-C21.2, finding C8-2).
        /// IView~GetFirstDisplayDimension5.html hands over hidden and suppressed
        /// dimensions too, and a dimension hidden by hand made its edge «already
        /// dimensioned in this drawing» while the paper did not show it. It is
        /// skipped by ApiHelpers.IsHiddenByHand; a hidden layer and a suppressed
        /// owner feature are not seen by IAnnotation.Visible (its Remarks) and
        /// still count.
        /// </summary>
        private static HashSet<string> KeysAlreadyDimensioned(IDrawingDoc drawing, IView view, out ExistingDimensionsReading reading)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);
            reading = new ExistingDimensionsReading();
            // Le generatrici dicono la loro faccia solo a tavola ridisegnata (82zp).
            try { (drawing as IModelDoc2)?.GraphicsRedraw2(); } catch { }
            try
            {
                List<IView> views = null;
                try { views = ApiHelpers.ViewsOfEverySheet(drawing); } catch { }
                reading.EverySheet = views != null;
                if (views == null)
                {
                    var currentSheet = new List<IView>();
                    var w = (drawing.GetFirstView() as IView)?.GetNextView() as IView;   // [0] is the sheet
                    while (w != null) { currentSheet.Add(w); w = w.GetNextView() as IView; }
                    views = currentSheet;
                }
                string thisModel = null;
                try { thisModel = ApiHelpers.ReferencedModelKey(view); } catch { }
                foreach (var v in views)
                {
                    string itsModel = null;
                    try { itsModel = ApiHelpers.ReferencedModelKey(v); } catch { }
                    if (!ApiHelpers.MayShowTheSameModel(thisModel, itsModel)) { reading.ViewsOfAnotherModel++; continue; }
                    for (var d = v.GetFirstDisplayDimension5(); d != null; d = d.GetNext5())
                    {
                        // Not on the paper, not «already dimensioned» (C8-2). A state
                        // that cannot be read counts as shown, as before.
                        bool hidden = false;
                        try { hidden = ApiHelpers.IsHiddenByHand(d); } catch { }
                        if (hidden) { reading.HiddenByHand++; continue; }
                        try
                        {
                            var ann = d.GetAnnotation() as IAnnotation;
                            var ents = ann?.GetAttachedEntities3() as object[];
                            var straight = new List<IEdge>();
                            // DUE CERCHI COASSIALI SONO UNA LUNGHEZZA DEL TONDO (voce GT14),
                            // non due diametri gia' quotati; UNA GENERATRICE porta il
                            // diametro della sua faccia.
                            var attachedCircles = new List<double[]>();
                            bool onGeneratrix = false, withStraightLine = false;
                            string generatrixKey = null;
                            if (ents != null)
                                foreach (var o in ents)
                                {
                                    var there = o as IEdge;
                                    var cc = there == null ? null : there.GetCurve() as ICurve;
                                    if (cc != null && cc.IsCircle()) attachedCircles.Add(cc.CircleParams as double[]);
                                    if (cc != null && cc.IsLine()) withStraightLine = true;
                                    var gen = o as ISilhouetteEdge;
                                    if (gen != null)
                                    {
                                        onGeneratrix = true;
                                        IFace2 fg = null;
                                        try { fg = gen.GetFace() as IFace2; } catch { }
                                        var kg = CylinderKey(fg);
                                        if (kg != null) generatrixKey = kg;
                                    }
                                }
                            // UNA QUOTA SU GENERATRICI (revisione avversariale del 27/9/2026):
                            // col cilindro, la SUA chiave; SENZA faccia — sulla boccola GetFace
                            // resta null (82zp) — il diametro dal valore, se la quota porta il Ø:
                            // lo stesso criterio con cui la strada «generatrici senza faccia»
                            // l'ha tenuta. Prima non la si vedeva affatto, e la chiamata dopo
                            // rimetteva lo stesso diametro.
                            if (onGeneratrix)
                            {
                                if (generatrixKey != null)
                                {
                                    outside.Add(generatrixKey);
                                    outside.Add("foro|" + generatrixKey.Split('|')[1]);
                                }
                                else
                                {
                                    string pre = ""; double mm = double.NaN;
                                    try { pre = d.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
                                    try { mm = ApiHelpers.ReadDimensionInConfiguration(d.GetDimension2(0) as IDimension, "") * 1000.0; } catch { }
                                    if (pre.Contains("<MOD-DIAM>") && !double.IsNaN(mm))
                                    {
                                        string dd = Math.Round(mm, 2).ToString("0.00", Ci);
                                        outside.Add("foro|" + dd);
                                        outside.Add("cil?|" + dd);   // un Ø30, ma non si sa di quale cilindro
                                    }
                                }
                            }
                            bool roundLength = attachedCircles.Count == 2 && Coaxial(attachedCircles[0], attachedCircles[1]);
                            if (roundLength)
                                outside.Add(RoundKey(new[] { attachedCircles[0][0], attachedCircles[0][1], attachedCircles[0][2] },
                                                      new[] { attachedCircles[1][0], attachedCircles[1][1], attachedCircles[1][2] }));
                            if (ents != null && !roundLength)
                                foreach (var o in ents)
                                {
                                    var e = o as IEdge;
                                    if (e == null) continue;
                                    var c = e.GetCurve() as ICurve;
                                    if (c != null && c.IsCircle())
                                    {
                                        // UN DIAMETRO, NON UNA POSIZIONE: la quota di posizione di un
                                        // foro e' attaccata al cerchio E a uno spigolo, e non quota il
                                        // foro (il richiamo nasce prima e porta la sua chiave).
                                        if (withStraightLine) continue;
                                        var cp = c.CircleParams as double[];
                                        if (cp == null || cp.Length < 7) continue;
                                        string dd = Math.Round(cp[6] * 2000.0, 2).ToString("0.00", Ci);
                                        outside.Add("foro|" + dd);
                                        // E IL SUO CILINDRO, se si trova: un gradino di un tondo e un
                                        // foro dello stesso Ø sono due elementi (revisione del 27/9).
                                        outside.Add(CylinderCircleKey(e) ?? "cil?|" + dd);
                                        continue;
                                    }
                                    var sm = TouchesAChamfer(e);
                                    if (sm != null) outside.Add("smusso|" + sm);
                                    if (c != null && c.IsLine()) straight.Add(e);
                                }
                            // L'ESTENSIONE DI UNO SPIGOLO E' QUOTATA solo da una quota attaccata
                            // a LUI SOLO (revisione avversariale del 27/9/2026): la quota di
                            // posizione di un foro prende il bordo della piastra come
                            // riferimento, e segnarne l'estensione faceva scartare l'altezza
                            // vera nella vista dopo («gia' quotata in un'altra vista»).
                            if (straight.Count == 1 && ents.Length == 1 && attachedCircles.Count == 0 && !onGeneratrix)
                            {
                                var p = ApiHelpers.EdgeParameters(straight[0]);
                                var k = KeyInModel(p.StartPoint as double[], p.EndPoint as double[]);
                                if (k != null) outside.Add(k);
                            }
                        }
                        catch { }
                    }
                }
                reading.Complete = true;
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// What the reading of the existing dimensions covered (Z-C21.2, findings
        /// N43 and C8-2), so that the answer says it instead of implying «the
        /// whole drawing».
        /// </summary>
        private sealed class ExistingDimensionsReading
        {
            /// <summary>
            /// False when the walk broke off (a view or GetNext5 threw): the keys
            /// found so far are used, and the counts below are not a number to
            /// report — zero would be a claim.
            /// </summary>
            public bool Complete;
            /// <summary>True: the views of every sheet (IDrawingDoc.GetViews). False: the current sheet only.</summary>
            public bool EverySheet;
            /// <summary>Display dimensions left out because their annotation is hidden by hand.</summary>
            public int HiddenByHand;
            /// <summary>Views left out because they show another model or configuration.</summary>
            public int ViewsOfAnotherModel;
        }

        /// <summary>
        /// Il cerchio e' il piede di una sporgenza, non la bocca di un foro: l'altra
        /// faccia che il cerchio borda (il cilindro della borchia, il raccordo al
        /// piede) sale dalla parte di FUORI della faccia piana, mentre la parete di
        /// un foro (o il suo smusso) scende dentro il materiale. Si misura sui
        /// cerchi e sugli estremi degli altri spigoli di quella faccia, contro la
        /// normale della faccia piana. False se non si capisce: resta foro, come prima.
        /// </summary>
        private static bool IsTheFootOfAStud(IEdge e)
        {
            try
            {
                var cb = (e.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 3) return false;
                IFace2 planar = null, other = null;
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var f = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var su = f?.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (su.IsPlane() && planar == null) planar = f; else other = f;
                }
                if (planar == null || other == null) return false;
                var n = ApiHelpers.NormalIfPlanar(planar);
                if (n == null || n.Length < 3) return false;
                double farther = 0;
                foreach (var o in (other.GetEdges() as object[]) ?? new object[0])
                {
                    var ed = o as IEdge;
                    var cv = ed?.GetCurve() as ICurve;
                    if (cv == null) continue;
                    var points = new List<double[]>();
                    if (cv.IsCircle()) { var cc = cv.CircleParams as double[]; if (cc != null) points.Add(cc); }
                    else
                    {
                        var pr = ApiHelpers.EdgeParameters(ed);
                        if (pr?.StartPoint is double[] a) points.Add(a);
                        if (pr?.EndPoint is double[] b) points.Add(b);
                    }
                    foreach (var p in points)
                    {
                        double h = (p[0] - cb[0]) * n[0] + (p[1] - cb[1]) * n[1] + (p[2] - cb[2]) * n[2];
                        if (Math.Abs(h) > Math.Abs(farther)) farther = h;
                    }
                }
                return farther > 1e-7;
            }
            catch { return false; }
        }

        /// <summary>
        /// Passante o cieco, DALLA GEOMETRIA: il cilindro del foro e il suo altro
        /// cerchio. Se l'altro cerchio e' un anello interno di una faccia piana il
        /// foro esce di li': passante. Se e' l'anello esterno di una faccia piana
        /// (il fondo) o tocca un cono (la punta), e' cieco, e la profondita' e' la
        /// distanza fra i due cerchi lungo l'asse — quella che il richiamo scrive
        /// dopo ↧, punta esclusa. null se non si capisce (il cilindro finisce su
        /// un altro cilindro, per esempio).
        /// </summary>
        private static bool? ThroughAllFromGeometry(IEdge border, out double depthMm)
        {
            depthMm = double.NaN;
            try
            {
                var cb = (border.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 7) return null;
                foreach (var oc in (border.GetCoEdges() as object[]) ?? new object[0])
                {
                    var cil = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var sc = cil?.GetSurface() as ISurface;
                    if (sc == null || !sc.IsCylinder()) continue;
                    foreach (var o in (cil.GetEdges() as object[]) ?? new object[0])
                    {
                        var other = o as IEdge;
                        var ca = other?.GetCurve() as ICurve;
                        if (ca == null || !ca.IsCircle()) continue;
                        var cp = ca.CircleParams as double[];
                        if (cp == null || cp.Length < 7) continue;
                        double axisLength = Math.Abs((cp[0] - cb[0]) * cb[3] + (cp[1] - cb[1]) * cb[4] + (cp[2] - cb[2]) * cb[5]);
                        if (axisLength < 1e-7) continue;   // e' il cerchio da cui si parte
                        foreach (var oc2 in (other.GetCoEdges() as object[]) ?? new object[0])
                        {
                            var an = (oc2 as ICoEdge)?.GetLoop() as ILoop2;
                            var s = (an?.GetFace() as IFace2)?.GetSurface() as ISurface;
                            if (s == null || s.IsCylinder()) continue;   // il cilindro del foro
                            if (s.IsCone()) { depthMm = Math.Round(axisLength * 1000, 3); return false; }
                            if (s.IsPlane())
                            {
                                if (!an.IsOuter()) return true;
                                depthMm = Math.Round(axisLength * 1000, 3);
                                return false;
                            }
                        }
                        return null;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// IL CONTORNO DI UN PEZZO TONDO visto lungo l'asse: una quota di
        /// diametro, non un richiamo di foro. Riletta: deve valere il diametro del
        /// cerchio — sullo stesso cerchio visto di taglio `AddDiameterDimension2`
        /// dice 60 per un Ø30 (misurato il 26/9/2026), e un numero sbagliato e
        /// plausibile non resta in tavola.
        /// </summary>
        private void DimensionTheContours(
            IModelDoc2 model, IDrawingDoc drawing, List<Circle> contours, HashSet<string> already,
            double[] contour, double dimensionOffset, double planStep, List<Dictionary<string, object>> born,
            List<Dictionary<string, object>> diameters, List<string> borders, List<string> elsewhere, List<string> failed)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            // UN PASSO PIU' IN ALTO PER OGNI DIAMETRO: i contorni di un tondo sono
            // concentrici, e alla stessa altezza i testi nascevano uno sull'altro
            // (l'albero a gradini visto di testa, 27/9/2026: quattro testi accavallati).
            int n = 0;
            foreach (var c in contours)
            {
                string dm = c.DiameterMm.ToString("0.##", Ci);
                string key = "foro|" + Math.Round(c.DiameterMm, 2).ToString("0.00", Ci);
                if (!done.Add(key)) continue;
                // IL SUO CILINDRO, se si trova (revisione del 27/9/2026): il contorno
                // di un tondo e' gia' quotato se lo e' QUEL cilindro, non un Ø uguale
                // qualunque — un foro trasversale Ø30 richiamato in un'altra vista non
                // quota il gradino Ø30.
                string its = CylinderCircleKey(c.Edge);
                // UN CERCHIO SENZA IL SUO CILINDRO non e' un diametro esterno: e' il
                // bordo di uno smusso (cono-piano, il Ø28 di un Ø30 smussato visto di
                // testa) o la linea di tangenza di un raccordo (il Ø24 al piede di una
                // borchia Ø20 raccordata). Quotarlo e' una quota che non si fabbrica.
                if (its == null)
                {
                    bool onChamfer = false;
                    foreach (var oc in (c.Edge.GetCoEdges() as object[]) ?? new object[0])
                    {
                        var su = (((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2)?.GetSurface() as ISurface;
                        if (su != null && (su.IsCone() || su.IsTorus())) onChamfer = true;
                    }
                    if (onChamfer)
                    {
                        borders.Add("Ø" + dm + " (bordo di uno smusso o di un raccordo: va con la sua quota, non con un diametro)");
                        continue;
                    }
                }
                bool dimensioned = its != null
                    ? already.Contains(its) || already.Contains("cil?|" + key.Substring(5))
                    : already.Contains(key);
                if (dimensioned) { elsewhere.Add("diametro esterno Ø" + dm + " (gia' quotato in questa tavola)"); continue; }
                int dimensionsBefore = CountDimensions(drawing);
                IDisplayDimension q = null;
                try
                {
                    model.ClearSelection2(true);
                    if ((c.Edge as IEntity).Select4(false, null))
                        q = model.AddDiameterDimension2(c.SheetCenter[0] + 0.015 + n * 0.012, contour[3] + dimensionOffset * 0.8 + n * planStep, 0) as IDisplayDimension;
                }
                catch { }
                model.ClearSelection2(true);
                if (NotGrown(drawing, dimensionsBefore, q))
                {
                    failed.Add("diametro esterno Ø" + dm + " (la quota non e' nata)");
                    continue;
                }
                // BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80): the count went up and
                // q is null. The dimension is on the sheet, so it goes on as the
                // others with its value unknown, not refused as a wrong value it
                // cannot be shown to be (see RecordBornWithoutObject).
                var described = DescribeDimension(q, c.Index);
                object read = described.ContainsKey("mm") ? described["mm"] : null;
                if (q != null && (!(read is double) || Math.Abs((double)read - c.DiameterMm) > 0.01))
                {
                    bool removed = false;
                    try
                    {
                        var an = q.GetAnnotation() as IAnnotation;
                        if (an != null && an.Select3(false, null))
                            removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        model.ClearSelection2(true);
                    }
                    catch { }
                    failed.Add("diametro esterno Ø" + dm + " (SOLIDWORKS ha scritto " + (read ?? "?") +
                                (removed ? ": l'ho tolta)" : ": toglila)"));
                    continue;
                }
                described["orientation"] = "outer diameter";
                described["position_mm"] = WhereItEnded(q);
                if (q != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    "external diameter Ø" + dm + " (the dimension was born but SOLIDWORKS did not hand it back: " +
                    "its value was not read back, check it on the sheet)");
                diameters.Add(new Dictionary<string, object> { ["diameter_mm"] = Math.Round(c.DiameterMm, 2), ["diameter_reread_mm"] = read });
                already.Add(key);
                if (its != null) already.Add(its);
                n++;
            }
        }

        /// <summary>
        /// THE HOLES: one callout per diameter AND depth (through, blind with its
        /// depth, or unknown), with the count of equal holes in front, and the
        /// position dimensions from the two reference edges, in parallel
        /// (finding N81: the catalogue said one per diameter).
        /// </summary>
        private void DimensionTheHoles(
            IModelDoc2 model, IDrawingDoc drawing, List<Circle> circles, List<ProjectedEdge> all,
            HashSet<string> already, double[] contour, double dimensionOffset, double planStep, double scale,
            ref int below, ref int sideways, ref int onLeft, double[] sheet,
            List<Dictionary<string, object>> born, List<Dictionary<string, object>> holes,
            List<string> elsewhere, List<string> failed)
        {
            // UN RICHIAMO PER DIAMETRO *E PROFONDITA'*: «4× Ø8 ↧5» su quattro Ø8
            // di cui due passanti manderebbe l'officina a forarli tutti ciechi.
            // Il secondo termine viene dalla geometria: -1 passante, -2 non si
            // capisce, altrimenti la profondita'.
            var groups = new SortedDictionary<Tuple<double, double>, List<Circle>>();
            foreach (var c in circles)
            {
                var k = Tuple.Create(Math.Round(c.DiameterMm, 2),
                    c.ThroughAll == true ? -1.0 : c.ThroughAll == false && !double.IsNaN(c.DepthMm) ? Math.Round(c.DepthMm, 2) : -2.0);
                if (!groups.ContainsKey(k)) groups[k] = new List<Circle>();
                groups[k].Add(c);
            }

            var toPosition = new List<Circle>();
            // GIA' QUOTATO VUOL DIRE PRIMA DI QUESTA CHIAMATA (revisione avversariale
            // del 27/9/2026). I gruppi sono per diametro E profondita', e con la
            // chiave del solo diametro aggiunta dentro questo ciclo il secondo gruppo
            // — due Ø8 ciechi accanto a due Ø8 passanti — finiva «gia' quotato»:
            // senza richiamo, senza profondita', senza posizione. In officina si
            // foravano tutti e quattro passanti, cioe' proprio il caso che il
            // raggruppamento doveva evitare.
            var alreadyBefore = new HashSet<string>(already, StringComparer.Ordinal);
            foreach (var g in groups)
            {
                string key = "foro|" + g.Key.Item1.ToString("0.00", Ci);
                // E PER CILINDRO, quando si trova: un gradino di un tondo dello
                // stesso Ø, quotato in un'altra vista, non e' questo foro.
                var itsItems = new List<string>();
                bool allFound = true;
                foreach (var c in g.Value)
                {
                    string k = CylinderCircleKey(c.Edge);
                    if (k == null) allFound = false; else itsItems.Add(k);
                }
                bool dimensioned = alreadyBefore.Contains("cil?|" + key.Substring(5));
                if (allFound) { foreach (var k in itsItems) if (alreadyBefore.Contains(k)) dimensioned = true; }
                else if (alreadyBefore.Contains(key)) dimensioned = true;
                if (dimensioned)
                {
                    elsewhere.Add("fori Ø" + g.Key.Item1.ToString("0.##", Ci) + " (gia' quotati in questa tavola)");
                    continue;
                }
                // Il richiamo va sul foro in alto a sinistra: e' il primo che si
                // legge, e la linea di richiamo esce verso l'alto, fuori dal pezzo.
                Circle head = g.Value[0];
                foreach (var c in g.Value)
                    if (c.SheetCenter[1] > head.SheetCenter[1] + 1e-6 ||
                        (Math.Abs(c.SheetCenter[1] - head.SheetCenter[1]) < 1e-6 && c.SheetCenter[0] < head.SheetCenter[0]))
                        head = c;

                int dimensionsBefore = CountDimensions(drawing);
                IDisplayDimension callout = null;
                // THE DIALOG OF THE GUIDE DOES NOT OPEN HERE (Z-C21.9, finding L15-2,
                // NOT REAL). IDrawingDoc~AddHoleCallout2.html, Remarks: «When you call
                // this method, the user must click OK in the dialog that shows the
                // system-generated hole callout». The call runs inside the try of
                // DrawingDimension that switched swInputDimValOnCreate off, and it has
                // come back in every bench case with holes run so far: the four
                // drawing cases that dimension holes (two equal holes, one through
                // and one blind; the blind hole that states its depth; the turned
                // bush; the plate with holes and chamfer), all green on library
                // 0.144.0. A modal window would have hung the COM call instead of
                // failing it.
                try
                {
                    model.ClearSelection2(true);
                    if ((head.Edge as IEntity).Select4(false, null))
                        callout = drawing.AddHoleCallout2(
                            head.SheetCenter[0] - 0.015, contour[3] + dimensionOffset * 0.8, 0) as IDisplayDimension;
                }
                catch { }
                string come = "richiamo del foro";
                // THE COUNT DECIDES, NOT THE OBJECT (Z-C21.8, finding N80). Until
                // library 0.144.0 a null object sent the hole to the fallback even
                // when the count of dimensions had gone up: the callout was on the
                // sheet, and the fallback put a diameter dimension on the same hole.
                bool calloutNotBorn = NotGrown(drawing, dimensionsBefore, callout);
                bool fellBack = false;
                if (calloutNotBorn)
                {
                    // Senza richiamo (un foro che non viene da un taglio
                    // riconoscibile), il diametro semplice.
                    fellBack = true;
                    callout = null;   // an object from a call that made nothing is not the dimension
                    try
                    {
                        model.ClearSelection2(true);
                        if ((head.Edge as IEntity).Select4(false, null))
                            callout = model.AddDiameterDimension2(
                                head.SheetCenter[0] - 0.015, contour[3] + dimensionOffset * 0.8, 0) as IDisplayDimension;
                        come = "quota di diametro";
                    }
                    catch { }
                    calloutNotBorn = NotGrown(drawing, dimensionsBefore, callout);
                }
                if (calloutNotBorn)
                {
                    failed.Add("fori Ø" + g.Key.Item1.ToString("0.##", Ci) + " (il richiamo non e' nato)");
                    continue;
                }
                // BORN WITHOUT ITS OBJECT (N80): with richiamo null from here on, the
                // writes and reads below fail inside their own try and leave their
                // values unknown (the «N×» is then reported as not stuck, the depth
                // of a blind hole as missing), the positions are still placed as
                // dimensions of their own, and the row says why its values are null.
                string countPrefix = g.Value.Count > 1 ? g.Value.Count.ToString(Ci) + "×" : null;
                if (countPrefix != null)
                {
                    // «4× » DAVANTI a cio' che c'e': il prefisso di un richiamo e'
                    // gia' il simbolo del diametro (<MOD-DIAM>), e sostituirlo lo
                    // cancellava — misurato, usciva «4× 8».
                    try
                    {
                        string prefix = callout.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                        callout.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix,
                                         countPrefix + " " + prefix);
                    }
                    catch { }
                }
                // THE COUNT IS READ BACK, NOT ASSUMED (Z-C21.2, finding N48).
                // IDisplayDimension~SetText.html returns nothing, and its Remarks
                // end with «NOTE: This method does not support hole callouts.» —
                // which is what AddHoleCallout2 hands back here. GetText carries
                // the same note, yet the guide's own example «Get Whether Display
                // Dimension is a Hole Callout» (VBA) reads the prefix and the suffix
                // of a hole callout with it, and the suffix below is read that way
                // since 26/9/2026. So the prefix is read again after writing it:
                // «how_many» stays the number of holes in the group (the geometry),
                // «prefix_read» is what the sheet says, and a count that did not
                // stick is a failure. Until library 0.140.0 the answer said
                // quanti = 4 whatever the callout carried, and the shop would have
                // read one Ø8.
                string prefixRead = null;
                try { prefixRead = callout.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix); } catch { }
                if (countPrefix != null &&
                    (prefixRead == null || !prefixRead.TrimStart().StartsWith(countPrefix, StringComparison.Ordinal)))
                    failed.Add("holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the «" + countPrefix + "» in front did not stick: " +
                                (prefixRead == null ? "the prefix could not be read back" : "the prefix reads «" + prefixRead + "»") +
                                ", so the sheet does not say how many holes there are; add the count by hand)");
                // IL RIPIEGO NON PORTA LA PROFONDITA' (revisione avversariale del
                // 26/9/2026): se il richiamo non nasce, la quota di diametro dice
                // «Ø8» e basta, e un cieco in officina si fora passante. La
                // profondita' allora la scrive il ponte, dalla geometria, nel
                // suffisso — lo stesso «<HOLE-DEPTH>» che usa il richiamo.
                string depthFrom = come == "richiamo del foro" ? "richiamo" : null;
                if (come == "quota di diametro" && head.ThroughAll == false && !double.IsNaN(head.DepthMm))
                {
                    try
                    {
                        callout.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix,
                                         " <HOLE-DEPTH>" + head.DepthMm.ToString("0.##", Ci));
                        depthFrom = "geometria";
                    }
                    catch { }
                }
                var described = DescribeDimension(callout, head.Index);
                described["orientation"] = come;
                described["position_mm"] = WhereItEnded(callout);
                if (callout != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    "holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the " + (fellBack ? "diameter dimension" : "hole callout") +
                    " was born but SOLIDWORKS did not hand it back: nothing could be written on it or read from it, " +
                    "check it on the sheet)");
                // LA PROFONDITA' DEL FORO CIECO (voce GT12, punto 1): il richiamo
                // di SOLIDWORKS la mette da se' nel suffisso — misurato il 26/9/2026,
                // «<MOD-DIAM>8» + «<HOLE-DEPTH>5» per un Ø8 profondo 5, «PASSANTE»
                // per il passante. Si rilegge e si dice: un foro cieco senza
                // profondita' in tavola si fora passante.
                string suffix = "";
                try { suffix = callout.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? ""; } catch { }
                object depth = null;
                var mp = Regex.Match(suffix, @"<HOLE-DEPTH>\s*([0-9]+(?:[.,][0-9]+)?)");
                double pv;
                if (mp.Success && double.TryParse(mp.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Ci, out pv))
                    depth = pv;
                bool throughAll = suffix.IndexOf("PASSANTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                suffix.IndexOf("THRU", StringComparison.OrdinalIgnoreCase) >= 0;
                if (depth == null) depthFrom = null;
                if (head.ThroughAll == false && depth == null)
                    // Un cieco, per la geometria, che esce dalla tavola senza
                    // profondita': non e' «fatto», e lo si dice.
                    failed.Add("fori Ø" + g.Key.Item1.ToString("0.##", Ci) + " (cieco" +
                                (double.IsNaN(head.DepthMm) ? "" : ", profondo " + head.DepthMm.ToString("0.##", Ci)) +
                                ", ma la quota non porta la profondita': in officina si forerebbe passante)");
                holes.Add(new Dictionary<string, object>
                {
                    ["diameter_mm"] = g.Key.Item1,
                    ["how_many"] = g.Value.Count,
                    // The prefix as read back after writing «N×» (N48): the count on
                    // the sheet, next to the count of the geometry above.
                    ["prefix_read"] = prefixRead,
                    ["how"] = come,
                    ["diameter_reread_mm"] = described.ContainsKey("mm") ? described["mm"] : null,
                    // Null, not false, for a callout born without its object (N80):
                    // its suffix was never read.
                    ["through_all"] = callout == null ? null : come == "richiamo del foro" ? (object)throughAll : head.ThroughAll,
                    ["depth_mm"] = depth,
                    ["depth_from"] = depthFrom,
                    ["callout_suffix"] = callout == null ? null : suffix,
                    // LA STESSA COSA LETTA DALLA GEOMETRIA, sempre: il cilindro del
                    // foro e il suo altro cerchio. Accanto a quella del richiamo, per
                    // chi vuole confrontarle.
                    ["through_all_from_geometry"] = head.ThroughAll,
                    ["depth_from_geometry_mm"] = double.IsNaN(head.DepthMm) ? null : (object)head.DepthMm,
                });
                already.Add(key);
                foreach (var k in itsItems) already.Add(k);
                toPosition.AddRange(g.Value);
            }
            if (toPosition.Count == 0) return;

            // ── LE POSIZIONI, DAI DUE SPIGOLI DI RIFERIMENTO ─────────────
            //
            // Quello piu' a sinistra fra i verticali e quello piu' in basso fra
            // gli orizzontali (a parita', il piu' lungo): sono i bordi del pezzo
            // in questa vista. Uno smusso non fa da riferimento.
            ProjectedEdge sx = null, down = null;
            foreach (var s in all)
            {
                if (s.ChamferFeature != null) continue;
                if (s.Facing == "vertical")
                {
                    double x = s.FromSheet[0];
                    if (sx == null || x < sx.FromSheet[0] - 1e-6 || (Math.Abs(x - sx.FromSheet[0]) < 1e-6 && s.Length > sx.Length)) sx = s;
                }
                else if (s.Facing == "horizontal")
                {
                    double y = s.FromSheet[1];
                    if (down == null || y < down.FromSheet[1] - 1e-6 || (Math.Abs(y - down.FromSheet[1]) < 1e-6 && s.Length > down.Length)) down = s;
                }
            }

            // Una colonna per ogni X distinta dei centri, una riga per ogni Y:
            // quattro fori in rettangolo sono due colonne e due righe, quattro
            // quote. In ordine di distanza, le piu' vicine al pezzo per prime.
            //
            // LA QUOTA SI ATTACCA AL FORO PIU' VICINO AL SUO LATO (guardato il
            // 25/9/2026): la prima stesura prendeva un foro qualunque della
            // riga, e la linea di richiamo del 15 partiva dal foro in basso a
            // DESTRA e attraversava tutta la piastra fino alla quota a sinistra.
            if (sx != null)
            {
                var columns = new SortedDictionary<double, Circle>();
                foreach (var c in toPosition)
                {
                    double d = Math.Round((c.SheetCenter[0] - sx.FromSheet[0]) * 1000.0, 2);
                    if (d <= 0.01) continue;
                    Circle its;
                    // le orizzontali stanno SOTTO: il foro piu' in basso della colonna
                    if (!columns.TryGetValue(d, out its) || c.SheetCenter[1] < its.SheetCenter[1]) columns[d] = c;
                }
                foreach (var col in columns)
                    HolePosition(model, drawing, col.Value, sx, true, col.Key / scale,
                                     (col.Value.SheetCenter[0] + sx.FromSheet[0]) / 2,
                                     contour[1] - dimensionOffset - below++ * planStep, born, failed);
            }
            if (down != null)
            {
                // DA CHE PARTE: dove c'e' posto DENTRO la cornice. La prima
                // stesura le metteva sempre a sinistra, e con la vista vicina al
                // bordo del foglio il 65 e' uscito dalla cornice.
                var lines = new SortedDictionary<double, List<Circle>>();
                foreach (var c in toPosition)
                {
                    double d = Math.Round((c.SheetCenter[1] - down.FromSheet[1]) * 1000.0, 2);
                    if (d <= 0.01) continue;
                    if (!lines.ContainsKey(d)) lines[d] = new List<Circle>();
                    lines[d].Add(c);
                }
                double margin = 0.015;
                double lastRight = contour[2] + dimensionOffset + (sideways + lines.Count - 1) * planStep;
                double lastLeft = contour[0] - dimensionOffset - (onLeft + lines.Count - 1) * planStep;
                bool onRight = sheet == null || sheet.Length < 2 || sheet[0] <= 0
                    ? false
                    : lastRight <= sheet[0] - margin || lastLeft < margin;
                foreach (var line in lines)
                {
                    // il foro della riga piu' vicino al lato della quota
                    Circle chosen = line.Value[0];
                    foreach (var c in line.Value)
                        if (onRight ? c.SheetCenter[0] > chosen.SheetCenter[0] : c.SheetCenter[0] < chosen.SheetCenter[0]) chosen = c;
                    double x = onRight
                        ? contour[2] + dimensionOffset + sideways++ * planStep
                        : contour[0] - dimensionOffset - onLeft++ * planStep;
                    HolePosition(model, drawing, chosen, down, false, line.Key / scale,
                                     x, (chosen.SheetCenter[1] + down.FromSheet[1]) / 2, born, failed);
                }
            }
        }

        /// <summary>
        /// Larghezza e altezza del foglio corrente, in metri, o null. Servono a
        /// non mettere una quota fuori dalla cornice.
        /// </summary>
        private static double[] SheetDimensions(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetCurrentSheet() as ISheet;
                var p = sheet?.GetProperties2() as double[];
                if (p != null && p.Length >= 7 && p[5] > 0 && p[6] > 0) return new[] { p[5], p[6] };
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Una quota di posizione fra il CENTRO di un foro e uno spigolo di
        /// riferimento, orizzontale o verticale. Si rilegge: deve valere la
        /// distanza vista sul foglio divisa per la scala — se misurasse al bordo
        /// del foro invece che al centro, sarebbe un raggio di troppo.
        /// </summary>
        private void HolePosition(
            IModelDoc2 model, IDrawingDoc drawing, Circle hole, ProjectedEdge reference, bool horizontal,
            double expectedMm, double x, double y,
            List<Dictionary<string, object>> born, List<string> failed)
        {
            int dimensionsBefore = CountDimensions(drawing);
            IDisplayDimension dimension = null;
            try
            {
                model.ClearSelection2(true);
                if ((hole.Edge as IEntity).Select4(false, null) && reference != null)
                {
                    // Lo spigolo di riferimento si prende dalla vista, come gli altri.
                    var ent = reference.Entity as IEntity;
                    if (ent != null && ent.Select4(true, null))
                        dimension = (horizontal
                            ? model.AddHorizontalDimension2(x, y, 0)
                            : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
                }
            }
            catch { }
            if (NotGrown(drawing, dimensionsBefore, dimension))
            {
                failed.Add("posizione del foro a " + expectedMm.ToString("0.##", Ci) + " mm (la quota non e' nata)");
                return;
            }
            // A quota null here was BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80):
            // described with its value unknown, see RecordBornWithoutObject.
            var described = DescribeDimension(dimension, hole.Index);
            described["orientation"] = horizontal ? "posizione orizzontale del foro" : "posizione verticale del foro";
            described["position_mm"] = WhereItEnded(dimension);
            object mm;
            if (described.TryGetValue("mm", out mm) && mm != null &&
                Math.Abs(Convert.ToDouble(mm, Ci) - expectedMm) > 0.05)
                described["warning"] = "vale " + Convert.ToDouble(mm, Ci).ToString("0.##", Ci) + " invece di " +
                                          expectedMm.ToString("0.##", Ci) + ": forse misura al bordo del foro e non al centro";
            if (dimension != null) born.Add(described);
            else RecordBornWithoutObject(born, described, failed,
                "hole position at " + expectedMm.ToString("0.##", Ci) + " mm (the dimension was born but SOLIDWORKS " +
                "did not hand it back: its value was not read back, check it on the sheet)");
        }

        /// <summary>
        /// GLI SMUSSI: uno per lavorazione, con la quota da smusso
        /// (`IDrawingDoc.AddChamferDim`, «5 x 45°»), dove lo smusso si vede di
        /// profilo — uno spigolo di smusso obliquo sulla vista. Si seleziona lo
        /// spigolo dello smusso e una delle sue «gambe», come a mano.
        /// </summary>
        private void DimensionTheChamfers(
            IModelDoc2 model, IDrawingDoc drawing, object[] edges, List<ProjectedEdge> all,
            HashSet<string> already, double[] contour, double dimensionOffset,
            List<Dictionary<string, object>> born, List<Dictionary<string, object>> chamfers,
            List<string> elsewhere, List<string> failed)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in all)
            {
                if (s.ChamferFeature == null || s.Facing != "obliquo" || done.Contains(s.ChamferFeature)) continue;
                done.Add(s.ChamferFeature);
                string key = "smusso|" + s.ChamferFeature;
                if (already.Contains(key))
                {
                    elsewhere.Add("smusso «" + s.ChamferFeature + "» (gia' quotato in questa tavola)");
                    continue;
                }
                ProjectedEdge leg = null;
                foreach (var t in all)
                {
                    if (t.ChamferFeature != null || t.Facing == "obliquo") continue;
                    foreach (var p in new[] { t.FromModel, t.ToModel })
                        foreach (var q in new[] { s.FromModel, s.ToModel })
                            if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) leg = t;
                    if (leg != null) break;
                }
                if (leg == null)
                {
                    failed.Add("smusso «" + s.ChamferFeature + "» (nessuno spigolo a cui appoggiare la quota da smusso)");
                    continue;
                }
                // Il testo FUORI dal pezzo, dalla parte dello smusso: dal centro
                // della vista verso lo smusso, oltre il contorno.
                double mx = (s.FromSheet[0] + s.ToSheet[0]) / 2, my = (s.FromSheet[1] + s.ToSheet[1]) / 2;
                double cx = (contour[0] + contour[2]) / 2, cy = (contour[1] + contour[3]) / 2;
                double vx = mx - cx, vy = my - cy, l = Math.Sqrt(vx * vx + vy * vy);
                if (l < 1e-9) { vx = -1; vy = 1; l = Math.Sqrt(2); }
                double x = mx + vx / l * (dimensionOffset + 0.006), y = my + vy / l * (dimensionOffset + 0.006);

                int dimensionsBefore = CountDimensions(drawing);
                IDisplayDimension dimension = null;
                try
                {
                    model.ClearSelection2(true);
                    if ((edges[s.Index] as IEntity).Select4(false, null) &&
                        (edges[leg.Index] as IEntity).Select4(true, null))
                        dimension = drawing.AddChamferDim(x, y, 0) as IDisplayDimension;
                }
                catch { }
                if (NotGrown(drawing, dimensionsBefore, dimension))
                {
                    failed.Add("smusso «" + s.ChamferFeature + "» (la quota da smusso non e' nata)");
                    continue;
                }
                // A quota null here was BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80).
                // AddChamferDim.html, Return Value: «Pointer to the newly created
                // chamfer dimension»; when it is null and the count went up, the
                // dimension is there and its two values cannot be read: the reads
                // below fail inside their try, and angle, side and width stay null.
                var described = DescribeDimension(dimension, s.Index);
                described["orientation"] = "chamfer dimension";
                described["position_mm"] = WhereItEnded(dimension);

                // LA QUOTA DA SMUSSO E' FATTA DI DUE QUOTE, e la prima e'
                // L'ANGOLO. Misurato il 25/9/2026: GetDimension2(0) = 0,785 rad
                // (45°), GetDimension2(1) = 7,071 mm, la larghezza della faccia
                // di smusso — e il numero sul foglio e' «5 x 45°». Il lato che si
                // legge e' quindi larghezza x cos(angolo). Per indice si
                // riportava 785,398 «mm», cioe' l'angolo in radianti per mille:
                // si distinguono per TIPO (`swDimensionParamType_e`: lineare 0,
                // angolare 1), non per posizione.
                double? angle = null, width = null;
                for (int k = 0; k < 2; k++)
                {
                    try
                    {
                        var dk = dimension.GetDimension2(k) as IDimension;
                        if (dk == null) continue;
                        int typ = dk.GetType();
                        if (typ == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular) angle = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                        else if (typ == (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear) width = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                    }
                    catch { }
                }
                object side = null;
                if (angle.HasValue && width.HasValue)
                    side = Math.Round(width.Value * Math.Cos(angle.Value) * 1000.0, 3);
                described["mm"] = side;
                var line = new Dictionary<string, object>
                {
                    ["feature"] = s.ChamferFeature,
                    ["leg_mm"] = side,
                    ["angle_deg"] = angle.HasValue ? (object)Math.Round(angle.Value * 180.0 / Math.PI, 2) : null,
                    ["face_width_mm"] = width.HasValue ? (object)Math.Round(width.Value * 1000.0, 3) : null,
                };
                chamfers.Add(line);
                if (dimension != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    "chamfer «" + s.ChamferFeature + "» (the chamfer dimension was born but SOLIDWORKS did not hand it " +
                    "back: its values were not read back, check it on the sheet)");
                already.Add(key);
            }
        }

        /// <summary>Un punto del modello sul foglio (metri), o null.</summary>
        private static double[] OnSheet(double[] p, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var q = ((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr) as IMathPoint;
                var outside = q?.ArrayData as double[];
                return outside != null && outside.Length >= 2 ? outside : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// L'ELEMENTO NEL MODELLO: l'asse lungo cui corre lo spigolo e il suo
        /// intervallo, al centesimo di millimetro. Due spigoli con la stessa
        /// chiave misurano la stessa estensione del pezzo, anche se stanno in
        /// due viste diverse — ed e' cosi' che si riconosce la quotatura
        /// ripetuta FRA viste: il 10 della vista frontale e il 10 di quella
        /// laterale sono lo stesso spessore. Null per uno spigolo obliquo.
        /// </summary>
        private static string KeyInModel(double[] da, double[] a)
        {
            double dx = a[0] - da[0], dy = a[1] - da[1], dz = a[2] - da[2];
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l < 1e-9) return null;
            int axis = Math.Abs(dx) / l > 0.999 ? 0 : Math.Abs(dy) / l > 0.999 ? 1 : Math.Abs(dz) / l > 0.999 ? 2 : -1;
            if (axis < 0) return null;
            double v0 = Math.Min(da[axis], a[axis]) * 1000.0, v1 = Math.Max(da[axis], a[axis]) * 1000.0;
            return "XYZ"[axis] + "|" + Math.Round(v0, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                   "|" + Math.Round(v1, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Il nome della lavorazione di smusso a cui lo spigolo appartiene
        /// (una delle sue due facce viene da uno smusso), o null.
        /// </summary>
        private static string TouchesAChamfer(IEdge edge)
        {
            try
            {
                var faces = edge.GetTwoAdjacentFaces2() as object[];
                if (faces == null) return null;
                foreach (var o in faces)
                {
                    var f = (o as IFace2)?.GetFeature() as IFeature;
                    if (f != null && f.GetTypeName2() == "Chamfer") return f.Name;
                }
            }
            catch { }
            return null;
        }

        /// <summary>L'ingombro della vista adesso, o `null` se non si legge.</summary>
        private static double[] ContourOf(IView view)
        {
            try { return view.GetOutline() as double[]; }
            catch { return null; }
        }

        /// <summary>
        /// L'ingombro della vista e' cambiato dopo aver messo le quote?
        ///
        /// «sconosciuto» quando una delle due letture non c'e': non «no». Un
        /// confronto che non si e' potuto fare non e' un confronto andato bene.
        /// </summary>
        private static string GrownContour(double[] before, double[] after)
        {
            if (before == null || after == null || before.Length < 4 || after.Length < 4)
                return "sconosciuto";
            for (int i = 0; i < 4; i++)
                if (Math.Abs(before[i] - after[i]) > 1e-9) return "si";
            return "no";
        }

        /// <summary>Da metri di foglio a millimetri, per la risposta.</summary>
        private static object InMillimeters(double[] v)
        {
            if (v == null) return null;
            var outside = new object[v.Length];
            for (int i = 0; i < v.Length; i++) outside[i] = Math.Round(v[i] * 1000, 2);
            return outside;
        }

        /// <summary>Dove la quota e' finita davvero, in millimetri di foglio.</summary>
        private static object WhereItEnded(IDisplayDimension d)
        {
            // SI RILEGGE, non si riporta il punto che si e' chiesto: il valore
            // che torna da chi ha fatto la cosa non e' la misura della cosa, e
            // qui serve proprio a poter asserire che la quota e' FUORI dalla
            // sagoma della vista.
            try
            {
                var ann = d?.GetAnnotation() as IAnnotation;
                var q = ann?.GetPosition() as double[];
                if (q != null && q.Length >= 2)
                    return new object[] { Math.Round(q[0] * 1000, 2), Math.Round(q[1] * 1000, 2) };
            }
            catch { }
            return null;
        }

        /// <summary>
        /// A dimension BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80): the count
        /// of dimensions went up and the call returned null. <see cref="NotGrown"/>
        /// is asked first, and only a dimension that did not grow is «not born»;
        /// one that grew goes on as the others, described through
        /// <see cref="DescribeDimension"/> with null (as the edge dimensions of
        /// DrawingDimension always were), and here it gets its row and a line of
        /// «edges_not_dimensioned» saying that its values were not read back.
        /// Until library 0.144.0 the other call sites asked «null or not grown»
        /// and took such a dimension for missing: the hole callout then fell
        /// back to a diameter dimension on the same hole, two annotations for
        /// one hole, and the others reported as not born a dimension that was on
        /// the sheet. The lists come in as IList because the obsolete-call
        /// contract cannot read a generic parameter type, and an Add on a
        /// receiver it cannot type is counted as undecided.
        /// </summary>
        private static void RecordBornWithoutObject(System.Collections.IList born, Dictionary<string, object> row,
                                                    System.Collections.IList failed, string failure)
        {
            born.Add(row);
            failed.Add(failure);
        }

        /// <summary>
        /// Nome e VALORE di una quota appena nata, il valore riletto da
        /// SOLIDWORKS e non quello che credevo di aver chiesto.
        ///
        /// `GetSystemValue2("")` torna **metri**, sempre, qualunque siano le
        /// unita' del documento: e' l'unica lettura che non dipende da come e'
        /// impostata la tavola. `IDimension.Value` invece segue le unita' del
        /// documento, quindi si riporta accanto ma non si usa per confrontare.
        /// </summary>
        private static Dictionary<string, object> DescribeDimension(IDisplayDimension d, int edge)
        {
            var outside = new Dictionary<string, object> { ["edge"] = edge };
            if (d == null)
            {
                // La quota c'e' — il conteggio e' salito — ma l'oggetto non e'
                // tornato. Si dice, invece di far sparire la riga: un elenco
                // piu' corto del conteggio farebbe cercare un difetto che non
                // c'e'. Every call that places a dimension can end here (N80), not
                // only AddDimension, so the text names none.
                outside["name"] = "(born, but the call that placed it did not hand the object back)";
                outside["mm"] = null;   // not read, not zero
                return outside;
            }
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                if (dim != null)
                {
                    try { outside["name"] = dim.FullName; } catch { }
                    try { outside["mm"] = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000, 3); } catch { }
                }
            }
            catch { }
            if (!outside.ContainsKey("mm"))
                outside["mm"] = null;   // «non l'ho potuto leggere», non «zero»
            return outside;
        }

        /// <summary>
        /// I millimetri delle quote nate, in fila e senza l'indice dello
        /// spigolo. Le quote che non si e' potuto rileggere **non entrano**:
        /// un `null` in mezzo a dei numeri si confronterebbe come stringa
        /// vuota e passerebbe qualunque «non contiene».
        /// </summary>
        private static object[] MillimetersInARow(List<Dictionary<string, object>> quote)
        {
            var outside = new List<object>();
            foreach (var q in quote)
            {
                object v;
                if (q.TryGetValue("mm", out v) && v != null) outside.Add(v);
            }
            return outside.ToArray();
        }

        private static string ValuesInLine(List<Dictionary<string, object>> quote)
        {
            var parts = new List<string>();
            foreach (var q in quote)
            {
                object v;
                parts.Add(q.TryGetValue("mm", out v) && v != null ? v + " mm" : "?");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// La vista chiesta come la scrive lo stato («V2») o per nome, o la prima
        /// vera se non si dice. Null col motivo quando la vista non c'e'; null
        /// senza motivo quando c'e' ma non si lascia prendere.
        ///
        /// «V2», COME LO SCRIVE LO STATO DELLA TAVOLA (25/9/2026). I NOMI delle
        /// viste dipendono dalla lingua di SOLIDWORKS — «Vista del disegno2»
        /// qui, «Drawing View2» in inglese — e un piano che li copia vale su
        /// una sola installazione. L'identificativo dello stato no: V1 e' la
        /// prima vista del foglio, nello stesso ordine in cui lo stato le
        /// elenca. Un nome vero vince sempre: se una vista si chiama «V2», e'
        /// quella. Estratto da `drawing_dimension` il 26/9/2026: lo usa anche
        /// `hole_table`, e due copie divergono.
        /// </summary>
        private static IView ResolveView(IDrawingDoc drawing, List<string> names, string wanted, out string reason)
        {
            reason = null;
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
            {
                var uid = System.Text.RegularExpressions.Regex.Match(wanted.Trim(), @"^[Vv](\d+)$");
                if (uid.Success)
                {
                    int k = int.Parse(uid.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (k < 1 || k > names.Count)
                    {
                        reason = "Non c'e' la vista «" + wanted + "»: la tavola ne ha " + names.Count +
                                 " (da V1 a V" + names.Count + ").";
                        return null;
                    }
                    wanted = names[k - 1];
                }
            }
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
            {
                reason = "Non c'e' nessuna vista che si chiami «" + wanted + "». Ci sono: " +
                         string.Join(", ", names) + ".";
                return null;
            }
            return ViewByName(drawing, wanted);
        }

        /// <summary>La vista chiesta per nome, o la prima vera se non si dice.</summary>
        private static IView ViewByName(IDrawingDoc drawing, string wanted)
        {
            try
            {
                // La prima «view» e' il FOGLIO: le viste vere cominciano dopo.
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                if (string.IsNullOrWhiteSpace(wanted)) return v;
                while (v != null)
                {
                    string its = "";
                    try { its = v.GetName2() ?? ""; } catch { }
                    if (SameName(its, wanted)) return v;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }
    }
}
