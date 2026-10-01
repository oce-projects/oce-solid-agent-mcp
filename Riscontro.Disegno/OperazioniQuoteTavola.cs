using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
    public partial class OperazioniSw
    {
        private Esito QuotaInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: le quote stanno su una " +
                    "vista di disegno. Crea prima la tavola con «create_drawing».");

            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito(
                    "La tavola non ha viste: le quote non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «standard_views» o «model_view».");

            string voluta = Str(a, "view", null);
            string motivoVista;
            IView vista = RisolviVista(disegno, nomi, voluta, out motivoVista);
            if (motivoVista != null) return Fallito(motivoVista);
            if (vista == null)
                return Fallito("Non riesco a prendere la vista su cui quotare.");

            string nomeVista = "";
            try { nomeVista = vista.GetName2() ?? ""; } catch { }

            // ── GLI SPIGOLI CHE LA VISTA MOSTRA ─────────────────────────
            object[] spigoli = null;
            try
            {
                spigoli = vista.GetVisibleEntities2(
                    null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            }
            catch (Exception ex)
            {
                return Fallito("Non riesco a chiedere gli spigoli alla vista: " + ex.Message);
            }

            // ZERO-E-ZERO: l'eccezione ha gia' il suo rifiuto qui sopra; null e'
            // una vista senza spigoli visibili, e lo zero e' vero.
            int disponibili = spigoli == null ? 0 : spigoli.Length;
            if (disponibili == 0)
                return Fallito(
                    "La vista «" + nomeVista + "» non mostra nessuno spigolo quotabile. " +
                    "Su una vista vuota, o su una vista in cui il modello non e' " +
                    "risolto, non c'e' niente da quotare — e mettere zero quote " +
                    "rispondendo «fatto» sarebbe peggio.");

            // QUALI SPIGOLI. Senza dirlo si quotano i primi, e quanti si dice.
            var scelti = new List<int>();
            var elencoChiesto = Lista(a, "edges");
            if (elencoChiesto.Count > 0)
            {
                foreach (var o in elencoChiesto)
                {
                    int i;
                    if (!int.TryParse((o ?? "").Trim(), out i))
                        return Fallito("«edges» vuole numeri, e «" + o + "» non lo e'.");
                    if (i < 0 || i >= disponibili)
                        return Fallito(
                            "Lo spigolo " + i + " non c'e': la vista «" + nomeVista +
                            "» ne mostra " + disponibili + " (da 0 a " + (disponibili - 1) + ").");
                    if (!scelti.Contains(i)) scelti.Add(i);
                }
            }
            else
            {
                int quante = (int)Num(a, "count", 4);
                if (quante <= 0)
                    return Fallito("«count» dev'essere almeno 1: zero quote non sono una quota.");
                for (int i = 0; i < Math.Min(quante, disponibili); i++) scelti.Add(i);
            }

            // ── DOVE METTERLE, in coordinate FOGLIO ─────────────────────
            //
            // IL CONTORNO SI LEGGE UNA VOLTA SOLA, prima di piazzare qualunque
            // quota. Se `GetOutline()` crescesse insieme alle annotazioni
            // attaccate alla vista, rileggerlo a ogni giro farebbe scappare le
            // quote sempre piu' lontano. Non e' verificato che cresca: per
            // questo la risposta riporta il contorno di prima E quello di
            // dopo, cosi' chi legge puo' vederlo invece di fidarsi.
            double[] contorno = null;
            try { contorno = vista.GetOutline() as double[]; } catch { }
            if (contorno == null || contorno.Length < 4)
                return Fallito(
                    "Non riesco a leggere l'ingombro della vista sul foglio, e senza " +
                    "quello le quote finirebbero in un punto qualunque — fuori dal " +
                    "foglio, se va male, e senza nessun errore.");

            double distacco = Num(a, "offset_mm", 12) / 1000.0;
            double passo = Num(a, "pitch_mm", 10) / 1000.0;

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
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = vista.ModelToViewTransform; } catch { }
            if (mu == null || tr == null)
                return Fallito(
                    "Non riesco a leggere come la vista porta il modello sul foglio: senza, le " +
                    "quote finirebbero nel punto sbagliato, e senza nessun errore.");
            double scala = 1;
            try { scala = vista.ScaleDecimal; } catch { }
            if (scala <= 0) scala = 1;

            // TUTTI GLI SPIGOLI DELLA VISTA, UNA VOLTA SOLA (voce GT11). I fori e
            // gli smussi si cercano su tutti, non sui primi «count»: una
            // piastra con quattro fori non smette di averli perche' si sono
            // chiesti quattro spigoli.
            var tutti = new List<Spigolo>();
            var cerchi = new List<Cerchio>();
            int foriNellaTabella = 0;   // ZERO-E-ZERO: nessuna tabella fori nella vista, finche' non si guarda
            for (int i = 0; i < disponibili; i++)
            {
                var c = CerchioSulFoglio(spigoli[i] as IEdge, i, mu, tr);
                if (c != null) { cerchi.Add(c); continue; }
                var s = LeggiSpigolo(vista, spigoli[i], i, mu, tr);
                if (s != null) tutti.Add(s);
            }

            // LE QUOTE CHE LA TAVOLA HA GIA', IN TUTTE LE VISTE (voce GT11). «Ciascun
            // elemento dell'oggetto deve essere quotato non piu' di una volta»
            // vale per la TAVOLA, non per la vista: il 25/9/2026 la tavola della
            // piastra portava il 10 nella vista frontale e in quella laterale, e
            // il 120 nella frontale e in quella dall'alto.
            // EVERY SHEET since Z-C21.2 (finding N43), only the views of this model
            // and configuration, and without the dimensions hidden by hand (C8-2):
            // what was read is in the answer, see ChiaviGiaQuotate.
            var gia = ChiaviGiaQuotate(disegno, vista, out var existingRead);

            var falliti = new List<string>();
            var diametriEsterni = new List<Dictionary<string, object>>();
            var bordiNonQuotati = new List<string>();
            var diTaglio = new List<string>();
            EsitoTondo tondo = null;
            var misurati = new List<Spigolo>();
            var doppioni = new List<string>();
            var altrove = new List<string>();
            var diSmusso = new List<string>();
            var visti = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in scelti)
            {
                if (cerchi.Exists(c => c.Indice == i)) continue;   // i fori hanno la loro strada, qui sotto
                var s = tutti.Find(t => t.Indice == i);
                if (s == null) { falliti.Add(i + " (non riesco a misurarlo sul foglio)"); continue; }

                // LO SMUSSO NON SI QUOTA COI SUOI SPIGOLI. La prima tavola
                // guardata aveva lo smusso quotato 7,071 in obliquo (l'ipotenusa)
                // e la catena 5 + 75 accanto all'80: si scrive 5 x 45°, con la
                // quota da smusso, e le «gambe» — i tratti accorciati dallo
                // smusso — le dicono la quota da smusso e l'ingombro insieme.
                if (s.DiSmusso != null)
                {
                    diSmusso.Add(i + " (spigolo dello smusso «" + s.DiSmusso + "»: va con la quota da smusso)");
                    continue;
                }
                if (EGambaDiSmusso(s, tutti))
                {
                    diSmusso.Add(i + " (accorciato dallo smusso: lo dicono la quota da smusso e l'ingombro)");
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
                if (visti.ContainsKey(s.Chiave))
                {
                    doppioni.Add(i + " (stessa misura dello spigolo " + visti[s.Chiave] + ")");
                    continue;
                }
                if (s.NelModello != null && gia.Contains(s.NelModello))
                {
                    altrove.Add(i + " (" + Math.Round(s.Lunghezza * 1000.0 / scala, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                " mm: la stessa estensione e' gia' quotata in un'altra vista)");
                    continue;
                }
                visti[s.Chiave] = i;
                if (s.NelModello != null) gia.Add(s.NelModello);
                misurati.Add(s);
            }

            // DALLE PICCOLE ALLE GRANDI, allontanandosi dal pezzo: «le linee di
            // misura andranno disegnate in ordine progressivo, dalle minori
            // alle maggiori, allontanandosi dal contorno delle figure».
            misurati.Sort((x, y) => x.Lunghezza.CompareTo(y.Lunghezza));

            int prima = ContaQuote(disegno);
            if (prima < 0)
                return Fallito(
                    "Non riesco a contare le quote della tavola prima di cominciare: senza " +
                    "quel numero non saprei dirti quali ho messo io. Non ho toccato niente.");

            // ── LA TRAPPOLA DEL MODALE, spenta e rimessa ────────────────
            bool chiedevaIlValore = true;
            bool spenta = false;
            var nate = new List<Dictionary<string, object>>();
            var fori = new List<Dictionary<string, object>>();
            var smussi = new List<Dictionary<string, object>>();
            try
            {
                try
                {
                    chiedevaIlValore = _sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    _sw.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    spenta = true;
                }
                catch { /* se non si puo' spegnere lo dira' il primo che si appende */ }

                // UN CONTATORE PER FASCIA, non uno solo per tutte. La stesura
                // precedente ne aveva uno globale, e cosi' la seconda quota
                // scendeva di un passo anche se andava a destra.
                int sotto = 0, diLato = 0, aSinistra = 0;

                // ── I FORI, PRIMA DI TUTTO (voce GT11) ──────────────────
                //
                // La prima tavola guardata non quotava i fori: ne' il diametro
                // ne' la posizione, cioe' proprio cio' che in officina serve di
                // piu'. Il diametro con il RICHIAMO DEL FORO (`AddHoleCallout2`,
                // «Ø8 PASSANTE»), one per diameter AND depth (QuotaIFori), col numero dei fori uguali
                // davanti («4× »: il prefisso si aggiunge a quello che c'e', che
                // e' il simbolo del diametro — sostituirlo lo cancellava,
                // misurato). La posizione con quote dai due spigoli di
                // riferimento, il sinistro e quello in basso: in parallelo,
                // quindi senza catene e senza quote sovrabbondanti. Misurato il
                // 25/9/2026: cerchio + spigolo con `AddHorizontalDimension2`
                // misura dal CENTRO (15 sulla piastra), con `AddDimension` non
                // nasce niente.
                var dimensioniFoglio = DimensioniDelFoglio(disegno);
                // UNA TABELLA FORI NELLA VISTA QUOTA GIA' I FORI (voce GT5, 26/9/2026).
                // La prima tavola con la tabella, GUARDATA, aveva i fori due volte:
                // posizioni e misure nella tabella, e di nuovo richiami e quote di
                // posizione sulla vista. «Ciascun elemento si quota una volta sola»:
                // se la tabella c'e', i fori sono suoi.
                foriNellaTabella = ForiNellaTabellaFori(disegno);
                var foriVeri = cerchi.FindAll(c => c.Tipo == "foro");
                if (foriVeri.Count > 0 && foriNellaTabella <= 0)
                    QuotaIFori(modello, disegno, foriVeri, tutti, gia, contorno, distacco, passo, scala,
                               ref sotto, ref diLato, ref aSinistra, dimensioniFoglio, nate, fori, altrove, falliti);
                QuotaIContorni(modello, disegno, cerchi.FindAll(c => c.Tipo == "contorno"), gia, contorno, distacco, passo,
                               nate, diametriEsterni, bordiNonQuotati, altrove, falliti);
                foreach (var c in cerchi)
                    if (c.Tipo == "di_taglio") diTaglio.Add("Ø" + c.DiametroMm.ToString("0.##", Ci));
                // IL TONDO VISTO DI FIANCO (voce GT14): cerchi di taglio coassiali
                // vogliono dire un pezzo tornito nella vista longitudinale —
                // lunghezze fra le facce e diametri dalle generatrici.
                tondo = QuotaIlTondoDiFianco(modello, disegno, vista, mu, tr, cerchi.FindAll(c => c.Tipo == "di_taglio"),
                                             gia, contorno, distacco, passo, ref sotto, ref diLato, nate, altrove, falliti);

                foreach (var s in misurati)
                {
                    int quotePrima = ContaQuote(disegno);
                    try
                    {
                        modello.ClearSelection2(true);
                        var ent = spigoli[s.Indice] as IEntity;
                        if (ent == null) { falliti.Add(s.Indice + " (non e' un'entita')"); continue; }
                        if (!ent.Select4(false, null)) { falliti.Add(s.Indice + " (non si seleziona)"); continue; }

                        // ── DOVE VA QUESTA QUOTA ────────────────────────
                        //
                        // «Devono essere parallele alla dimensione a cui si
                        // riferiscono» e «devono, per quanto possibile, essere
                        // disposte all'esterno delle figure» (scheda
                        // Zanichelli, DISPOSIZIONE DELLE LINEE DI MISURA).
                        // Quindi: orizzontali SOTTO, verticali DI LATO.
                        double x, y;
                        if (s.Verso == "horizontal")
                        {
                            x = s.Mezzo;
                            y = contorno[1] - distacco - sotto * passo;
                            sotto++;
                        }
                        else if (s.Verso == "vertical")
                        {
                            x = contorno[2] + distacco + diLato * passo;
                            y = s.Mezzo;
                            diLato++;
                            // DENTRO LA CORNICE: una vista sul bordo destro del
                            // foglio manda la fascia fuori dal disegno. Allora a
                            // sinistra — stessa regola dei fori.
                            if (dimensioniFoglio != null && x > dimensioniFoglio[0] - 0.015)
                            {
                                diLato--;
                                x = contorno[0] - distacco - aSinistra * passo;
                                aSinistra++;
                            }
                        }
                        else
                        {
                            // OBLIQUO: non c'e' una fascia giusta, e metterlo
                            // con le orizzontali lo farebbe sembrare tale. Va
                            // a sinistra, e la risposta lo dice.
                            x = contorno[0] - distacco - aSinistra * passo;
                            y = (contorno[1] + contorno[3]) / 2;
                            aSinistra++;
                        }

                        var natura = modello.Extension.AddDimension(x, y, 0, 0)
                                     as IDisplayDimension;
                        try { modello.EditRebuild3(); } catch { }

                        if (NonCresciuta(disegno, quotePrima, natura))
                        {
                            // NON SI GUARDA SE `AddDimension` HA RISPOSTA: si
                            // guarda se la quota c'e'. E' la regola di casa —
                            // il valore che torna da chi ha fatto la cosa non
                            // e' la misura della cosa.
                            falliti.Add(s.Indice + " (chiamata andata, quota non nata)");
                            continue;
                        }

                        var descritta = DescriviQuota(natura, s.Indice);
                        descritta["orientation"] = s.Verso;
                        descritta["position_mm"] = DovEFinita(natura);
                        nate.Add(descritta);
                    }
                    catch (Exception ex)
                    {
                        var m = ex.Message ?? "";
                        falliti.Add(s.Indice + " (" + (m.Length > 90 ? m.Substring(0, 90) + "..." : m) + ")");
                    }
                }

                // ── GLI SMUSSI, CON LA QUOTA DA SMUSSO (voce GT11) ─────
                QuotaGliSmussi(modello, disegno, spigoli, tutti, gia, contorno, distacco, nate, smussi, altrove, falliti);
            }
            finally
            {
                if (spenta)
                    try
                    {
                        _sw.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedevaIlValore);
                    }
                    catch { }
                try { modello.ClearSelection2(true); } catch { }
            }

            int dopo = ContaQuote(disegno);
            if (dopo < 0)
                return Fallito(
                    "Ho messo le quote nella vista «" + nomeVista + "», ma poi non riesco a " +
                    "ricontarle: non so dirti quante ne sono nate. Guarda la tavola prima di " +
                    "andare avanti.");
            int messe = dopo - prima;

            if (messe <= 0)
            {
                // NIENTE DA QUOTARE NON E' LA STESSA COSA DI «NON SONO RIUSCITO».
                // Se tutto cio' che la vista mostra e' gia' quotato altrove, o e'
                // un doppione, o sta nello smusso, si dice cosi' — e resta un
                // rifiuto, perche' zero quote non sono una risposta «fatto».
                if (falliti.Count == 0 && diTaglio.Count > 0 && misurati.Count == 0 && tondo == null &&
                    doppioni.Count + altrove.Count + diSmusso.Count == 0 && foriNellaTabella <= 0)
                    return Fallito(
                        "Nella vista «" + nomeVista + "» gli unici spigoli sono cerchi visti di taglio (" +
                        string.Join(", ", diTaglio) + ") e non stanno su uno stesso asse: non e' un pezzo tondo " +
                        "visto di fianco che io sappia quotare. Il diametro di un cerchio lo quoto dalla vista in " +
                        "cui e' un cerchio (di taglio, AddDiameterDimension2 dice 60 per un Ø30).");
                if (falliti.Count == 0 && ((doppioni.Count + altrove.Count + diSmusso.Count) > 0 || foriNellaTabella > 0))
                    return Fallito(
                        "Nella vista «" + nomeVista + "» non c'e' niente da quotare che non lo sia " +
                        "gia': " + doppioni.Count + " doppioni nella vista, " + altrove.Count +
                        " gia' quotati in un'altra vista, " + diSmusso.Count + " dello smusso" +
                        (foriNellaTabella > 0 ? ", " + foriNellaTabella + " fori nella tabella fori" : "") + ". " +
                        "Ciascun elemento si quota una volta sola in tutta la tavola (UNI ISO 129-1).");
                return Fallito(
                    "Nessuna quota e' nata (erano " + prima + ", sono " + dopo + "). " +
                    "La vista «" + nomeVista + "» mostra " + disponibili + " spigoli e ne " +
                    "ho provati " + scelti.Count +
                    (falliti.Count == 0 ? "" : ": " + string.Join(", ", falliti)) + ". " +
                    "Se la finestra «Modifica» e' rimasta accesa, la chiamata si appende " +
                    "invece di fallire: qui non e' successo, quindi e' altro.");
            }

            double[] dopoContorno = ContornoDi(vista);

            // ── I TESTI CHE SI ACCAVALLANO (voce GT12, punto 3) ─────────────
            // Il rettangolo vero di ogni testo, letto da IDisplayData
            // (OperazioniTestiQuote): testo su testo in tutta la tavola, testo
            // su una linea del pezzo in questa vista.
            var testiDelleQuote = TestiDelleQuote(disegno);
            var accavallati = TestiCheSiAccavallano(disegno, vista, testiDelleQuote);

            var misure = new Dictionary<string, object>
            {
                ["view"] = nomeVista,
                ["edges_available"] = disponibili,
                ["edges_tried"] = scelti.Count,
                ["dimensions_before"] = prima,
                ["dimensions_after"] = dopo,
                ["dimensions_placed"] = messe,
                // OGNI QUOTA COL SUO VALORE RILETTO, in millimetri. E' il pezzo
                // che distingue questa operazione da una che disegna numeri.
                ["dimensions"] = nate.ToArray(),
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
                ["dimensions_mm"] = MillimetriInFila(nate),
                ["edges_not_dimensioned"] = falliti.ToArray(),

                // ── FORI, SMUSSI, E CIO' CHE ERA GIA' QUOTATO (voce GT11) ──
                //
                // Nati il 25/9/2026 da una tavola GUARDATA: i fori non erano
                // quotati, lo smusso era un 7,071 obliquo, il 10 e il 120
                // comparivano in due viste. Le attese del banco erano tutte
                // verdi, perche' chiedevano i millimetri delle quote messe —
                // non se la tavola basta a fare il pezzo.
                ["holes"] = fori.ToArray(),
                ["holes_in_hole_table"] = foriNellaTabella > 0 ? (object)foriNellaTabella : null,
                // I CERCHI CHE NON SONO FORI (26/9/2026): il contorno di un pezzo
                // tondo ha una quota di diametro, non un richiamo; un cerchio visto
                // di taglio non si quota da qui.
                ["outer_diameters"] = diametriEsterni.ToArray(),
                ["borders_not_dimensioned"] = bordiNonQuotati.ToArray(),
                ["cut_circles"] = diTaglio.ToArray(),
                // IL TONDO VISTO DI FIANCO (voce GT14): lunghezze in parallelo dalla
                // prima faccia, diametri dalle generatrici; e i diametri che la
                // vista non espone, detti.
                ["round_axis"] = tondo?.Asse,
                ["round_lengths_mm"] = tondo == null ? null : tondo.Lunghezze.ToArray(),
                ["round_diameters_mm"] = tondo == null ? null : tondo.Diametri.ToArray(),
                ["round_diameters_not_reached"] = tondo == null ? null : tondo.NonRaggiunti.ToArray(),
                ["round_notes"] = tondo == null ? null : tondo.Note.ToArray(),
                ["round_generatrices_without_face"] = tondo == null ? null : (object)tondo.GeneratriciSenzaFaccia,
                ["chamfers"] = smussi.ToArray(),
                ["already_dimensioned_elsewhere"] = altrove.ToArray(),
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
                ["chamfer_edges"] = diSmusso.ToArray(),

                // ── I DOPPIONI SI DICONO, NON SI NASCONDONO ─────────────
                //
                // Chiedere quattro spigoli di un rettangolo e vedersene
                // quotare due e' sorprendente finche' non si sa perche'. La
                // ragione e' una regola di disegno, non un difetto: «ciascun
                // elemento dell'oggetto deve essere quotato non piu' di una
                // volta» (UNI ISO 129-1, scheda Sammarone/Zanichelli), e
                // 120/10/120/10 e' quotatura ripetuta — «quindi errata».
                ["edges_discarded_duplicates"] = doppioni.ToArray(),

                // IL CONTORNO PRIMA E DOPO. Non e' accertato che `GetOutline()`
                // cresca con le annotazioni attaccate alla vista: se i due
                // numeri differiscono, cresce, e chi mettera' altre quote dopo
                // di noi deve saperlo invece di scoprirlo.
                ["contour_view_mm"] = InMillimetri(contorno),
                ["contour_view_after_mm"] = InMillimetri(dopoContorno),
                // IL CONFRONTO LO FA IL CODICE, non chi scrive l'attesa. Un
                // caso di banco puo' pretendere un valore, non paragonare due
                // campi fra loro: mettere qui il paragone gia' fatto e' l'unico
                // modo perche' diventi una riga verificabile.
                ["contour_grown"] = ContornoCresciuto(contorno, dopoContorno),
                // A WALK THAT BROKE OFF IS «NOT KNOWN» (Z-C21.8, finding N47):
                // TestiDelleQuote gives null then, and the three fields are null,
                // not the count of the texts read before the break.
                ["texts_read"] = testiDelleQuote == null ? null : (object)testiDelleQuote.Count,
                ["overlapping_texts"] = accavallati == null ? null : accavallati.ToArray(),
                // Nessun testo letto su una tavola con quote e' «non lo so», non
                // «nessuno si accavalla» (revisione del 26/9/2026).
                ["how_many_texts_overlap"] = testiDelleQuote == null || accavallati == null ||
                                                  (testiDelleQuote.Count == 0 && dopo > 0)
                    ? null : (object)accavallati.Count,
                ["offset_used_mm"] = Math.Round(distacco * 1000, 2),
                ["pitch_used_mm"] = Math.Round(passo * 1000, 2),

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

            _log("[agente] " + messe + " quote sulla vista «" + nomeVista + "»: " +
                 ValoriInRiga(nate));
            return SenzaFeature(misure);
        }

        /// <summary>
        /// Uno spigolo, misurato SUL FOGLIO invece che nel modello.
        ///
        /// Serve a sapere due cose prima di quotare: da che parte sta — e
        /// quindi dove va la sua quota — e dove sta, per riconoscere che due
        /// spigoli diversi misurano lo stesso elemento.
        /// </summary>
        private sealed class Spigolo
        {
            public int Indice;
            /// <summary>«horizontal», «vertical» oppure «obliquo».</summary>
            public string Verso;
            /// <summary>Il centro della proiezione, in coordinate foglio.</summary>
            public double Mezzo;
            public double Lunghezza;
            /// <summary>
            /// Verso piu' intervallo proiettato, arrotondato al centesimo di
            /// millimetro. Due spigoli con la stessa chiave misurano lo stesso
            /// elemento: i lati opposti di un rettangolo. Due gradini uguali
            /// in posti diversi hanno chiavi diverse, e si quotano tutti e due.
            /// </summary>
            public string Chiave;
            /// <summary>Gli estremi sul foglio e nel modello, in metri.</summary>
            public double[] DaFoglio, AFoglio, DaModello, AModello;
            /// <summary>Asse e intervallo nel MODELLO: la chiave fra viste. Null se obliquo.</summary>
            public string NelModello;
            /// <summary>La lavorazione di smusso a cui appartiene, o null.</summary>
            public string DiSmusso;
            /// <summary>L'entita' come la espone la vista, per selezionarla.</summary>
            public object Entita;
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
        private static Spigolo LeggiSpigolo(IView vista, object entita, int indice, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var spigolo = entita as IEdge;
                if (spigolo == null) return null;
                var p = ApiHelpers.EdgeParameters(spigolo);
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
                double[] daV = SulFoglio(da, mu, tr), aV = SulFoglio(a, mu, tr);
                if (daV == null || aV == null) return null;
                double dx = aV[0] - daV[0], dy = aV[1] - daV[1];
                double lung = Math.Sqrt(dx * dx + dy * dy);
                if (lung < 1e-9) return null;

                // LA SOGLIA E' UNA SCELTA DI CODICE, NON UNA PRESCRIZIONE.
                // Le fonti parlano di parallelismo come criterio geometrico
                // esatto; un numero ci vuole lo stesso, e si dichiara. Un
                // millesimo sulla componente normalizzata: sotto, lo spigolo
                // e' parallelo a un asse entro un decimo di grado.
                const double SOGLIA = 1e-3;
                var s = new Spigolo { Indice = indice, Lunghezza = lung };
                double a0, a1;
                if (Math.Abs(dy) / lung < SOGLIA)
                {
                    s.Verso = "horizontal";
                    a0 = Math.Min(daV[0], aV[0]); a1 = Math.Max(daV[0], aV[0]);
                }
                else if (Math.Abs(dx) / lung < SOGLIA)
                {
                    s.Verso = "vertical";
                    a0 = Math.Min(daV[1], aV[1]); a1 = Math.Max(daV[1], aV[1]);
                }
                else
                {
                    s.Verso = "obliquo";
                    a0 = Math.Min(daV[0], aV[0]); a1 = Math.Max(daV[0], aV[0]);
                }
                s.Mezzo = (a0 + a1) / 2;
                s.Chiave = s.Verso + "|" + Math.Round(a0 * 100000) + "|" + Math.Round(a1 * 100000);
                s.DaFoglio = daV;
                s.AFoglio = aV;
                s.DaModello = da;
                s.AModello = a;
                s.NelModello = ChiaveNelModello(da, a);
                s.DiSmusso = TocCaUnoSmusso(spigolo);
                s.Entita = entita;
                return s;
            }
            catch { return null; }
        }

        /// <summary>Un cerchio intero nella vista: un foro, il contorno di un pezzo tondo, o un cerchio visto di taglio.</summary>
        private sealed class Cerchio
        {
            public int Indice;
            public IEdge Spigolo;
            public double[] CentroFoglio;   // metri
            public double DiametroMm;
            public string Tipo;             // "foro", "contorno", "di_taglio"
            public bool? Passante;          // dalla geometria; null se non si capisce
            public double ProfonditaMm = double.NaN;
            public double[] CentroModello;  // metri, nel modello
            public double[] AsseModello;    // versore, nel modello
        }

        private static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Il cerchio sul foglio, se lo spigolo e' un cerchio intero; null altrimenti.</summary>
        private static Cerchio CerchioSulFoglio(IEdge e, int indice, IMathUtility mu, MathTransform tr)
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
                var centro = SulFoglio(new[] { cp[0], cp[1], cp[2] }, mu, tr);
                if (centro == null) return null;
                // CHE CERCHIO E' (26/9/2026, una boccola tornita): il contorno Ø30
                // di un pezzo tondo prendeva il richiamo «Ø30 ↧30» come fosse un
                // foro, e i due cerchi della boccola visti di taglio finivano fra
                // i «fori il cui richiamo non e' nato». Un foro guarda chi legge
                // (l'asse, portato sul foglio, e' perpendicolare al foglio) ED e'
                // un anello interno di una faccia piana — il criterio delle croci
                // di centro (EUnForoCheSiVede).
                string tipo;
                var asse = ((IMathVector)((IMathVector)mu.CreateVector(new[] { cp[3], cp[4], cp[5] })).MultiplyTransform(tr)).ArrayData as double[];
                // ZERO-E-ZERO: un asse che non si porta sul foglio vale lunghezza zero
                // apposta — il cerchio finisce fra quelli di taglio, cioe' NON quotato
                // e detto nella risposta, invece di prendere un richiamo di foro.
                double l = asse == null ? 0 : Math.Sqrt(asse[0] * asse[0] + asse[1] * asse[1] + asse[2] * asse[2]);
                if (l < 1e-12 || Math.Abs(asse[2]) / l < 0.99) tipo = "di_taglio";
                // UN ANELLO INTERNO NON BASTA (revisione avversariale del 27/9/2026): lo
                // e' anche il piede di una borchia, e un cerchio di tangenza Ø24 al
                // piede di una borchia Ø20 raccordata diventava «un foro» con le sue
                // quote di posizione. Una borchia ha una quota di diametro.
                else tipo = EUnForoCheSiVede(e, curva, mu, tr) && !EIlPiedeDiUnaBorchia(e) ? "foro" : "contorno";
                var c = new Cerchio { Indice = indice, Spigolo = e, CentroFoglio = centro, DiametroMm = Math.Round(cp[6] * 2000.0, 3), Tipo = tipo,
                                      CentroModello = new[] { cp[0], cp[1], cp[2] }, AsseModello = new[] { cp[3], cp[4], cp[5] } };
                if (tipo == "foro")
                {
                    double prof;
                    c.Passante = PassanteDallaGeometria(e, out prof);
                    c.ProfonditaMm = prof;
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
        private static bool EGambaDiSmusso(Spigolo s, List<Spigolo> tutti)
        {
            foreach (var t in tutti)
            {
                if (t.DiSmusso == null || ReferenceEquals(t, s)) continue;
                foreach (var p in new[] { s.DaModello, s.AModello })
                    foreach (var q in new[] { t.DaModello, t.AModello })
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
        private static HashSet<string> ChiaviGiaQuotate(IDrawingDoc disegno, IView vista, out ExistingDimensionsReading reading)
        {
            var fuori = new HashSet<string>(StringComparer.Ordinal);
            reading = new ExistingDimensionsReading();
            // Le generatrici dicono la loro faccia solo a tavola ridisegnata (82zp).
            try { (disegno as IModelDoc2)?.GraphicsRedraw2(); } catch { }
            try
            {
                List<IView> views = null;
                try { views = ApiHelpers.ViewsOfEverySheet(disegno); } catch { }
                reading.EverySheet = views != null;
                if (views == null)
                {
                    var currentSheet = new List<IView>();
                    var w = (disegno.GetFirstView() as IView)?.GetNextView() as IView;   // [0] is the sheet
                    while (w != null) { currentSheet.Add(w); w = w.GetNextView() as IView; }
                    views = currentSheet;
                }
                string thisModel = null;
                try { thisModel = ApiHelpers.ReferencedModelKey(vista); } catch { }
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
                            var dritti = new List<IEdge>();
                            // DUE CERCHI COASSIALI SONO UNA LUNGHEZZA DEL TONDO (voce GT14),
                            // non due diametri gia' quotati; UNA GENERATRICE porta il
                            // diametro della sua faccia.
                            var cerchiAttaccati = new List<double[]>();
                            bool suGeneratrice = false, conUnaRetta = false;
                            string chiaveGeneratrice = null;
                            if (ents != null)
                                foreach (var o in ents)
                                {
                                    var ce = o as IEdge;
                                    var cc = ce == null ? null : ce.GetCurve() as ICurve;
                                    if (cc != null && cc.IsCircle()) cerchiAttaccati.Add(cc.CircleParams as double[]);
                                    if (cc != null && cc.IsLine()) conUnaRetta = true;
                                    var gen = o as ISilhouetteEdge;
                                    if (gen != null)
                                    {
                                        suGeneratrice = true;
                                        IFace2 fg = null;
                                        try { fg = gen.GetFace() as IFace2; } catch { }
                                        var kg = ChiaveDelCilindro(fg);
                                        if (kg != null) chiaveGeneratrice = kg;
                                    }
                                }
                            // UNA QUOTA SU GENERATRICI (revisione avversariale del 27/9/2026):
                            // col cilindro, la SUA chiave; SENZA faccia — sulla boccola GetFace
                            // resta null (82zp) — il diametro dal valore, se la quota porta il Ø:
                            // lo stesso criterio con cui la strada «generatrici senza faccia»
                            // l'ha tenuta. Prima non la si vedeva affatto, e la chiamata dopo
                            // rimetteva lo stesso diametro.
                            if (suGeneratrice)
                            {
                                if (chiaveGeneratrice != null)
                                {
                                    fuori.Add(chiaveGeneratrice);
                                    fuori.Add("foro|" + chiaveGeneratrice.Split('|')[1]);
                                }
                                else
                                {
                                    string pre = ""; double mm = double.NaN;
                                    try { pre = d.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
                                    try { mm = ApiHelpers.ReadDimensionInConfiguration(d.GetDimension2(0) as IDimension, "") * 1000.0; } catch { }
                                    if (pre.Contains("<MOD-DIAM>") && !double.IsNaN(mm))
                                    {
                                        string dd = Math.Round(mm, 2).ToString("0.00", Ci);
                                        fuori.Add("foro|" + dd);
                                        fuori.Add("cil?|" + dd);   // un Ø30, ma non si sa di quale cilindro
                                    }
                                }
                            }
                            bool lunghezzaDelTondo = cerchiAttaccati.Count == 2 && Coassiali(cerchiAttaccati[0], cerchiAttaccati[1]);
                            if (lunghezzaDelTondo)
                                fuori.Add(ChiaveTondo(new[] { cerchiAttaccati[0][0], cerchiAttaccati[0][1], cerchiAttaccati[0][2] },
                                                      new[] { cerchiAttaccati[1][0], cerchiAttaccati[1][1], cerchiAttaccati[1][2] }));
                            if (ents != null && !lunghezzaDelTondo)
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
                                        if (conUnaRetta) continue;
                                        var cp = c.CircleParams as double[];
                                        if (cp == null || cp.Length < 7) continue;
                                        string dd = Math.Round(cp[6] * 2000.0, 2).ToString("0.00", Ci);
                                        fuori.Add("foro|" + dd);
                                        // E IL SUO CILINDRO, se si trova: un gradino di un tondo e un
                                        // foro dello stesso Ø sono due elementi (revisione del 27/9).
                                        fuori.Add(ChiaveDelCilindroDelCerchio(e) ?? "cil?|" + dd);
                                        continue;
                                    }
                                    var sm = TocCaUnoSmusso(e);
                                    if (sm != null) fuori.Add("smusso|" + sm);
                                    if (c != null && c.IsLine()) dritti.Add(e);
                                }
                            // L'ESTENSIONE DI UNO SPIGOLO E' QUOTATA solo da una quota attaccata
                            // a LUI SOLO (revisione avversariale del 27/9/2026): la quota di
                            // posizione di un foro prende il bordo della piastra come
                            // riferimento, e segnarne l'estensione faceva scartare l'altezza
                            // vera nella vista dopo («gia' quotata in un'altra vista»).
                            if (dritti.Count == 1 && ents.Length == 1 && cerchiAttaccati.Count == 0 && !suGeneratrice)
                            {
                                var p = ApiHelpers.EdgeParameters(dritti[0]);
                                var k = ChiaveNelModello(p.StartPoint as double[], p.EndPoint as double[]);
                                if (k != null) fuori.Add(k);
                            }
                        }
                        catch { }
                    }
                }
                reading.Complete = true;
            }
            catch { }
            return fuori;
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
        private static bool EIlPiedeDiUnaBorchia(IEdge e)
        {
            try
            {
                var cb = (e.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 3) return false;
                IFace2 piana = null, altra = null;
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var f = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var su = f?.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (su.IsPlane() && piana == null) piana = f; else altra = f;
                }
                if (piana == null || altra == null) return false;
                var n = ApiHelpers.NormalIfPlanar(piana);
                if (n == null || n.Length < 3) return false;
                double piuLontano = 0;
                foreach (var o in (altra.GetEdges() as object[]) ?? new object[0])
                {
                    var ed = o as IEdge;
                    var cv = ed?.GetCurve() as ICurve;
                    if (cv == null) continue;
                    var punti = new List<double[]>();
                    if (cv.IsCircle()) { var cc = cv.CircleParams as double[]; if (cc != null) punti.Add(cc); }
                    else
                    {
                        var pr = ApiHelpers.EdgeParameters(ed);
                        if (pr?.StartPoint is double[] a) punti.Add(a);
                        if (pr?.EndPoint is double[] b) punti.Add(b);
                    }
                    foreach (var p in punti)
                    {
                        double h = (p[0] - cb[0]) * n[0] + (p[1] - cb[1]) * n[1] + (p[2] - cb[2]) * n[2];
                        if (Math.Abs(h) > Math.Abs(piuLontano)) piuLontano = h;
                    }
                }
                return piuLontano > 1e-7;
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
        private static bool? PassanteDallaGeometria(IEdge bordo, out double profonditaMm)
        {
            profonditaMm = double.NaN;
            try
            {
                var cb = (bordo.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 7) return null;
                foreach (var oc in (bordo.GetCoEdges() as object[]) ?? new object[0])
                {
                    var cil = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var sc = cil?.GetSurface() as ISurface;
                    if (sc == null || !sc.IsCylinder()) continue;
                    foreach (var o in (cil.GetEdges() as object[]) ?? new object[0])
                    {
                        var altro = o as IEdge;
                        var ca = altro?.GetCurve() as ICurve;
                        if (ca == null || !ca.IsCircle()) continue;
                        var cp = ca.CircleParams as double[];
                        if (cp == null || cp.Length < 7) continue;
                        double lungoAsse = Math.Abs((cp[0] - cb[0]) * cb[3] + (cp[1] - cb[1]) * cb[4] + (cp[2] - cb[2]) * cb[5]);
                        if (lungoAsse < 1e-7) continue;   // e' il cerchio da cui si parte
                        foreach (var oc2 in (altro.GetCoEdges() as object[]) ?? new object[0])
                        {
                            var an = (oc2 as ICoEdge)?.GetLoop() as ILoop2;
                            var s = (an?.GetFace() as IFace2)?.GetSurface() as ISurface;
                            if (s == null || s.IsCylinder()) continue;   // il cilindro del foro
                            if (s.IsCone()) { profonditaMm = Math.Round(lungoAsse * 1000, 3); return false; }
                            if (s.IsPlane())
                            {
                                if (!an.IsOuter()) return true;
                                profonditaMm = Math.Round(lungoAsse * 1000, 3);
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
        private void QuotaIContorni(
            IModelDoc2 modello, IDrawingDoc disegno, List<Cerchio> contorni, HashSet<string> gia,
            double[] contorno, double distacco, double passo, List<Dictionary<string, object>> nate,
            List<Dictionary<string, object>> diametri, List<string> bordi, List<string> altrove, List<string> falliti)
        {
            var fatti = new HashSet<string>(StringComparer.Ordinal);
            // UN PASSO PIU' IN ALTO PER OGNI DIAMETRO: i contorni di un tondo sono
            // concentrici, e alla stessa altezza i testi nascevano uno sull'altro
            // (l'albero a gradini visto di testa, 27/9/2026: quattro testi accavallati).
            int n = 0;
            foreach (var c in contorni)
            {
                string dm = c.DiametroMm.ToString("0.##", Ci);
                string chiave = "foro|" + Math.Round(c.DiametroMm, 2).ToString("0.00", Ci);
                if (!fatti.Add(chiave)) continue;
                // IL SUO CILINDRO, se si trova (revisione del 27/9/2026): il contorno
                // di un tondo e' gia' quotato se lo e' QUEL cilindro, non un Ø uguale
                // qualunque — un foro trasversale Ø30 richiamato in un'altra vista non
                // quota il gradino Ø30.
                string suo = ChiaveDelCilindroDelCerchio(c.Spigolo);
                // UN CERCHIO SENZA IL SUO CILINDRO non e' un diametro esterno: e' il
                // bordo di uno smusso (cono-piano, il Ø28 di un Ø30 smussato visto di
                // testa) o la linea di tangenza di un raccordo (il Ø24 al piede di una
                // borchia Ø20 raccordata). Quotarlo e' una quota che non si fabbrica.
                if (suo == null)
                {
                    bool diSmusso = false;
                    foreach (var oc in (c.Spigolo.GetCoEdges() as object[]) ?? new object[0])
                    {
                        var su = (((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2)?.GetSurface() as ISurface;
                        if (su != null && (su.IsCone() || su.IsTorus())) diSmusso = true;
                    }
                    if (diSmusso)
                    {
                        bordi.Add("Ø" + dm + " (bordo di uno smusso o di un raccordo: va con la sua quota, non con un diametro)");
                        continue;
                    }
                }
                bool quotato = suo != null
                    ? gia.Contains(suo) || gia.Contains("cil?|" + chiave.Substring(5))
                    : gia.Contains(chiave);
                if (quotato) { altrove.Add("diametro esterno Ø" + dm + " (gia' quotato in questa tavola)"); continue; }
                int quotePrima = ContaQuote(disegno);
                IDisplayDimension q = null;
                try
                {
                    modello.ClearSelection2(true);
                    if ((c.Spigolo as IEntity).Select4(false, null))
                        q = modello.AddDiameterDimension2(c.CentroFoglio[0] + 0.015 + n * 0.012, contorno[3] + distacco * 0.8 + n * passo, 0) as IDisplayDimension;
                }
                catch { }
                modello.ClearSelection2(true);
                if (NonCresciuta(disegno, quotePrima, q))
                {
                    falliti.Add("diametro esterno Ø" + dm + " (la quota non e' nata)");
                    continue;
                }
                // BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80): the count went up and
                // q is null. The dimension is on the sheet, so it goes on as the
                // others with its value unknown, not refused as a wrong value it
                // cannot be shown to be (see RecordBornWithoutObject).
                var descritta = DescriviQuota(q, c.Indice);
                object letto = descritta.ContainsKey("mm") ? descritta["mm"] : null;
                if (q != null && (!(letto is double) || Math.Abs((double)letto - c.DiametroMm) > 0.01))
                {
                    bool tolta = false;
                    try
                    {
                        var an = q.GetAnnotation() as IAnnotation;
                        if (an != null && an.Select3(false, null))
                            tolta = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        modello.ClearSelection2(true);
                    }
                    catch { }
                    falliti.Add("diametro esterno Ø" + dm + " (SOLIDWORKS ha scritto " + (letto ?? "?") +
                                (tolta ? ": l'ho tolta)" : ": toglila)"));
                    continue;
                }
                descritta["orientation"] = "outer diameter";
                descritta["position_mm"] = DovEFinita(q);
                if (q != null) nate.Add(descritta);
                else RecordBornWithoutObject(nate, descritta, falliti,
                    "external diameter Ø" + dm + " (the dimension was born but SOLIDWORKS did not hand it back: " +
                    "its value was not read back, check it on the sheet)");
                diametri.Add(new Dictionary<string, object> { ["diameter_mm"] = Math.Round(c.DiametroMm, 2), ["diameter_reread_mm"] = letto });
                gia.Add(chiave);
                if (suo != null) gia.Add(suo);
                n++;
            }
        }

        /// <summary>
        /// THE HOLES: one callout per diameter AND depth (through, blind with its
        /// depth, or unknown), with the count of equal holes in front, and the
        /// position dimensions from the two reference edges, in parallel
        /// (finding N81: the catalogue said one per diameter).
        /// </summary>
        private void QuotaIFori(
            IModelDoc2 modello, IDrawingDoc disegno, List<Cerchio> cerchi, List<Spigolo> tutti,
            HashSet<string> gia, double[] contorno, double distacco, double passo, double scala,
            ref int sotto, ref int diLato, ref int aSinistra, double[] foglio,
            List<Dictionary<string, object>> nate, List<Dictionary<string, object>> fori,
            List<string> altrove, List<string> falliti)
        {
            // UN RICHIAMO PER DIAMETRO *E PROFONDITA'*: «4× Ø8 ↧5» su quattro Ø8
            // di cui due passanti manderebbe l'officina a forarli tutti ciechi.
            // Il secondo termine viene dalla geometria: -1 passante, -2 non si
            // capisce, altrimenti la profondita'.
            var gruppi = new SortedDictionary<Tuple<double, double>, List<Cerchio>>();
            foreach (var c in cerchi)
            {
                var k = Tuple.Create(Math.Round(c.DiametroMm, 2),
                    c.Passante == true ? -1.0 : c.Passante == false && !double.IsNaN(c.ProfonditaMm) ? Math.Round(c.ProfonditaMm, 2) : -2.0);
                if (!gruppi.ContainsKey(k)) gruppi[k] = new List<Cerchio>();
                gruppi[k].Add(c);
            }

            var daPosizionare = new List<Cerchio>();
            // GIA' QUOTATO VUOL DIRE PRIMA DI QUESTA CHIAMATA (revisione avversariale
            // del 27/9/2026). I gruppi sono per diametro E profondita', e con la
            // chiave del solo diametro aggiunta dentro questo ciclo il secondo gruppo
            // — due Ø8 ciechi accanto a due Ø8 passanti — finiva «gia' quotato»:
            // senza richiamo, senza profondita', senza posizione. In officina si
            // foravano tutti e quattro passanti, cioe' proprio il caso che il
            // raggruppamento doveva evitare.
            var giaPrima = new HashSet<string>(gia, StringComparer.Ordinal);
            foreach (var g in gruppi)
            {
                string chiave = "foro|" + g.Key.Item1.ToString("0.00", Ci);
                // E PER CILINDRO, quando si trova: un gradino di un tondo dello
                // stesso Ø, quotato in un'altra vista, non e' questo foro.
                var suoi = new List<string>();
                bool tuttiTrovati = true;
                foreach (var c in g.Value)
                {
                    string k = ChiaveDelCilindroDelCerchio(c.Spigolo);
                    if (k == null) tuttiTrovati = false; else suoi.Add(k);
                }
                bool quotato = giaPrima.Contains("cil?|" + chiave.Substring(5));
                if (tuttiTrovati) { foreach (var k in suoi) if (giaPrima.Contains(k)) quotato = true; }
                else if (giaPrima.Contains(chiave)) quotato = true;
                if (quotato)
                {
                    altrove.Add("fori Ø" + g.Key.Item1.ToString("0.##", Ci) + " (gia' quotati in questa tavola)");
                    continue;
                }
                // Il richiamo va sul foro in alto a sinistra: e' il primo che si
                // legge, e la linea di richiamo esce verso l'alto, fuori dal pezzo.
                Cerchio capo = g.Value[0];
                foreach (var c in g.Value)
                    if (c.CentroFoglio[1] > capo.CentroFoglio[1] + 1e-6 ||
                        (Math.Abs(c.CentroFoglio[1] - capo.CentroFoglio[1]) < 1e-6 && c.CentroFoglio[0] < capo.CentroFoglio[0]))
                        capo = c;

                int quotePrima = ContaQuote(disegno);
                IDisplayDimension richiamo = null;
                // THE DIALOG OF THE GUIDE DOES NOT OPEN HERE (Z-C21.9, finding L15-2,
                // NOT REAL). IDrawingDoc~AddHoleCallout2.html, Remarks: «When you call
                // this method, the user must click OK in the dialog that shows the
                // system-generated hole callout». The call runs inside the try of
                // QuotaInTavola that switched swInputDimValOnCreate off, and it has
                // come back in every bench case with holes run so far: the four
                // drawing cases that dimension holes (two equal holes, one through
                // and one blind; the blind hole that states its depth; the turned
                // bush; the plate with holes and chamfer), all green on library
                // 0.144.0. A modal window would have hung the COM call instead of
                // failing it.
                try
                {
                    modello.ClearSelection2(true);
                    if ((capo.Spigolo as IEntity).Select4(false, null))
                        richiamo = disegno.AddHoleCallout2(
                            capo.CentroFoglio[0] - 0.015, contorno[3] + distacco * 0.8, 0) as IDisplayDimension;
                }
                catch { }
                string come = "richiamo del foro";
                // THE COUNT DECIDES, NOT THE OBJECT (Z-C21.8, finding N80). Until
                // library 0.144.0 a null object sent the hole to the fallback even
                // when the count of dimensions had gone up: the callout was on the
                // sheet, and the fallback put a diameter dimension on the same hole.
                bool calloutNotBorn = NonCresciuta(disegno, quotePrima, richiamo);
                bool fellBack = false;
                if (calloutNotBorn)
                {
                    // Senza richiamo (un foro che non viene da un taglio
                    // riconoscibile), il diametro semplice.
                    fellBack = true;
                    richiamo = null;   // an object from a call that made nothing is not the dimension
                    try
                    {
                        modello.ClearSelection2(true);
                        if ((capo.Spigolo as IEntity).Select4(false, null))
                            richiamo = modello.AddDiameterDimension2(
                                capo.CentroFoglio[0] - 0.015, contorno[3] + distacco * 0.8, 0) as IDisplayDimension;
                        come = "quota di diametro";
                    }
                    catch { }
                    calloutNotBorn = NonCresciuta(disegno, quotePrima, richiamo);
                }
                if (calloutNotBorn)
                {
                    falliti.Add("fori Ø" + g.Key.Item1.ToString("0.##", Ci) + " (il richiamo non e' nato)");
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
                        string prefisso = richiamo.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                        richiamo.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix,
                                         countPrefix + " " + prefisso);
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
                try { prefixRead = richiamo.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix); } catch { }
                if (countPrefix != null &&
                    (prefixRead == null || !prefixRead.TrimStart().StartsWith(countPrefix, StringComparison.Ordinal)))
                    falliti.Add("holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the «" + countPrefix + "» in front did not stick: " +
                                (prefixRead == null ? "the prefix could not be read back" : "the prefix reads «" + prefixRead + "»") +
                                ", so the sheet does not say how many holes there are; add the count by hand)");
                // IL RIPIEGO NON PORTA LA PROFONDITA' (revisione avversariale del
                // 26/9/2026): se il richiamo non nasce, la quota di diametro dice
                // «Ø8» e basta, e un cieco in officina si fora passante. La
                // profondita' allora la scrive il ponte, dalla geometria, nel
                // suffisso — lo stesso «<HOLE-DEPTH>» che usa il richiamo.
                string profonditaDa = come == "richiamo del foro" ? "richiamo" : null;
                if (come == "quota di diametro" && capo.Passante == false && !double.IsNaN(capo.ProfonditaMm))
                {
                    try
                    {
                        richiamo.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix,
                                         " <HOLE-DEPTH>" + capo.ProfonditaMm.ToString("0.##", Ci));
                        profonditaDa = "geometria";
                    }
                    catch { }
                }
                var descritto = DescriviQuota(richiamo, capo.Indice);
                descritto["orientation"] = come;
                descritto["position_mm"] = DovEFinita(richiamo);
                if (richiamo != null) nate.Add(descritto);
                else RecordBornWithoutObject(nate, descritto, falliti,
                    "holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the " + (fellBack ? "diameter dimension" : "hole callout") +
                    " was born but SOLIDWORKS did not hand it back: nothing could be written on it or read from it, " +
                    "check it on the sheet)");
                // LA PROFONDITA' DEL FORO CIECO (voce GT12, punto 1): il richiamo
                // di SOLIDWORKS la mette da se' nel suffisso — misurato il 26/9/2026,
                // «<MOD-DIAM>8» + «<HOLE-DEPTH>5» per un Ø8 profondo 5, «PASSANTE»
                // per il passante. Si rilegge e si dice: un foro cieco senza
                // profondita' in tavola si fora passante.
                string suffisso = "";
                try { suffisso = richiamo.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? ""; } catch { }
                object profondita = null;
                var mp = Regex.Match(suffisso, @"<HOLE-DEPTH>\s*([0-9]+(?:[.,][0-9]+)?)");
                double pv;
                if (mp.Success && double.TryParse(mp.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Ci, out pv))
                    profondita = pv;
                bool passante = suffisso.IndexOf("PASSANTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                suffisso.IndexOf("THRU", StringComparison.OrdinalIgnoreCase) >= 0;
                if (profondita == null) profonditaDa = null;
                if (capo.Passante == false && profondita == null)
                    // Un cieco, per la geometria, che esce dalla tavola senza
                    // profondita': non e' «fatto», e lo si dice.
                    falliti.Add("fori Ø" + g.Key.Item1.ToString("0.##", Ci) + " (cieco" +
                                (double.IsNaN(capo.ProfonditaMm) ? "" : ", profondo " + capo.ProfonditaMm.ToString("0.##", Ci)) +
                                ", ma la quota non porta la profondita': in officina si forerebbe passante)");
                fori.Add(new Dictionary<string, object>
                {
                    ["diameter_mm"] = g.Key.Item1,
                    ["how_many"] = g.Value.Count,
                    // The prefix as read back after writing «N×» (N48): the count on
                    // the sheet, next to the count of the geometry above.
                    ["prefix_read"] = prefixRead,
                    ["how"] = come,
                    ["diameter_reread_mm"] = descritto.ContainsKey("mm") ? descritto["mm"] : null,
                    // Null, not false, for a callout born without its object (N80):
                    // its suffix was never read.
                    ["through_all"] = richiamo == null ? null : come == "richiamo del foro" ? (object)passante : capo.Passante,
                    ["depth_mm"] = profondita,
                    ["depth_from"] = profonditaDa,
                    ["callout_suffix"] = richiamo == null ? null : suffisso,
                    // LA STESSA COSA LETTA DALLA GEOMETRIA, sempre: il cilindro del
                    // foro e il suo altro cerchio. Accanto a quella del richiamo, per
                    // chi vuole confrontarle.
                    ["through_all_from_geometry"] = capo.Passante,
                    ["depth_from_geometry_mm"] = double.IsNaN(capo.ProfonditaMm) ? null : (object)capo.ProfonditaMm,
                });
                gia.Add(chiave);
                foreach (var k in suoi) gia.Add(k);
                daPosizionare.AddRange(g.Value);
            }
            if (daPosizionare.Count == 0) return;

            // ── LE POSIZIONI, DAI DUE SPIGOLI DI RIFERIMENTO ─────────────
            //
            // Quello piu' a sinistra fra i verticali e quello piu' in basso fra
            // gli orizzontali (a parita', il piu' lungo): sono i bordi del pezzo
            // in questa vista. Uno smusso non fa da riferimento.
            Spigolo sx = null, giu = null;
            foreach (var s in tutti)
            {
                if (s.DiSmusso != null) continue;
                if (s.Verso == "vertical")
                {
                    double x = s.DaFoglio[0];
                    if (sx == null || x < sx.DaFoglio[0] - 1e-6 || (Math.Abs(x - sx.DaFoglio[0]) < 1e-6 && s.Lunghezza > sx.Lunghezza)) sx = s;
                }
                else if (s.Verso == "horizontal")
                {
                    double y = s.DaFoglio[1];
                    if (giu == null || y < giu.DaFoglio[1] - 1e-6 || (Math.Abs(y - giu.DaFoglio[1]) < 1e-6 && s.Lunghezza > giu.Lunghezza)) giu = s;
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
                var colonne = new SortedDictionary<double, Cerchio>();
                foreach (var c in daPosizionare)
                {
                    double d = Math.Round((c.CentroFoglio[0] - sx.DaFoglio[0]) * 1000.0, 2);
                    if (d <= 0.01) continue;
                    Cerchio suo;
                    // le orizzontali stanno SOTTO: il foro piu' in basso della colonna
                    if (!colonne.TryGetValue(d, out suo) || c.CentroFoglio[1] < suo.CentroFoglio[1]) colonne[d] = c;
                }
                foreach (var col in colonne)
                    PosizioneDelForo(modello, disegno, col.Value, sx, true, col.Key / scala,
                                     (col.Value.CentroFoglio[0] + sx.DaFoglio[0]) / 2,
                                     contorno[1] - distacco - sotto++ * passo, nate, falliti);
            }
            if (giu != null)
            {
                // DA CHE PARTE: dove c'e' posto DENTRO la cornice. La prima
                // stesura le metteva sempre a sinistra, e con la vista vicina al
                // bordo del foglio il 65 e' uscito dalla cornice.
                var righe = new SortedDictionary<double, List<Cerchio>>();
                foreach (var c in daPosizionare)
                {
                    double d = Math.Round((c.CentroFoglio[1] - giu.DaFoglio[1]) * 1000.0, 2);
                    if (d <= 0.01) continue;
                    if (!righe.ContainsKey(d)) righe[d] = new List<Cerchio>();
                    righe[d].Add(c);
                }
                double margine = 0.015;
                double ultimaDestra = contorno[2] + distacco + (diLato + righe.Count - 1) * passo;
                double ultimaSinistra = contorno[0] - distacco - (aSinistra + righe.Count - 1) * passo;
                bool aDestra = foglio == null || foglio.Length < 2 || foglio[0] <= 0
                    ? false
                    : ultimaDestra <= foglio[0] - margine || ultimaSinistra < margine;
                foreach (var riga in righe)
                {
                    // il foro della riga piu' vicino al lato della quota
                    Cerchio scelto = riga.Value[0];
                    foreach (var c in riga.Value)
                        if (aDestra ? c.CentroFoglio[0] > scelto.CentroFoglio[0] : c.CentroFoglio[0] < scelto.CentroFoglio[0]) scelto = c;
                    double x = aDestra
                        ? contorno[2] + distacco + diLato++ * passo
                        : contorno[0] - distacco - aSinistra++ * passo;
                    PosizioneDelForo(modello, disegno, scelto, giu, false, riga.Key / scala,
                                     x, (scelto.CentroFoglio[1] + giu.DaFoglio[1]) / 2, nate, falliti);
                }
            }
        }

        /// <summary>
        /// Larghezza e altezza del foglio corrente, in metri, o null. Servono a
        /// non mettere una quota fuori dalla cornice.
        /// </summary>
        private static double[] DimensioniDelFoglio(IDrawingDoc disegno)
        {
            try
            {
                var foglio = disegno.GetCurrentSheet() as ISheet;
                var p = foglio?.GetProperties2() as double[];
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
        private void PosizioneDelForo(
            IModelDoc2 modello, IDrawingDoc disegno, Cerchio foro, Spigolo riferimento, bool orizzontale,
            double attesoMm, double x, double y,
            List<Dictionary<string, object>> nate, List<string> falliti)
        {
            int quotePrima = ContaQuote(disegno);
            IDisplayDimension quota = null;
            try
            {
                modello.ClearSelection2(true);
                if ((foro.Spigolo as IEntity).Select4(false, null) && riferimento != null)
                {
                    // Lo spigolo di riferimento si prende dalla vista, come gli altri.
                    var ent = riferimento.Entita as IEntity;
                    if (ent != null && ent.Select4(true, null))
                        quota = (orizzontale
                            ? modello.AddHorizontalDimension2(x, y, 0)
                            : modello.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
                }
            }
            catch { }
            if (NonCresciuta(disegno, quotePrima, quota))
            {
                falliti.Add("posizione del foro a " + attesoMm.ToString("0.##", Ci) + " mm (la quota non e' nata)");
                return;
            }
            // A quota null here was BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80):
            // described with its value unknown, see RecordBornWithoutObject.
            var descritta = DescriviQuota(quota, foro.Indice);
            descritta["orientation"] = orizzontale ? "posizione orizzontale del foro" : "posizione verticale del foro";
            descritta["position_mm"] = DovEFinita(quota);
            object mm;
            if (descritta.TryGetValue("mm", out mm) && mm != null &&
                Math.Abs(Convert.ToDouble(mm, Ci) - attesoMm) > 0.05)
                descritta["warning"] = "vale " + Convert.ToDouble(mm, Ci).ToString("0.##", Ci) + " invece di " +
                                          attesoMm.ToString("0.##", Ci) + ": forse misura al bordo del foro e non al centro";
            if (quota != null) nate.Add(descritta);
            else RecordBornWithoutObject(nate, descritta, falliti,
                "hole position at " + attesoMm.ToString("0.##", Ci) + " mm (the dimension was born but SOLIDWORKS " +
                "did not hand it back: its value was not read back, check it on the sheet)");
        }

        /// <summary>
        /// GLI SMUSSI: uno per lavorazione, con la quota da smusso
        /// (`IDrawingDoc.AddChamferDim`, «5 x 45°»), dove lo smusso si vede di
        /// profilo — uno spigolo di smusso obliquo sulla vista. Si seleziona lo
        /// spigolo dello smusso e una delle sue «gambe», come a mano.
        /// </summary>
        private void QuotaGliSmussi(
            IModelDoc2 modello, IDrawingDoc disegno, object[] spigoli, List<Spigolo> tutti,
            HashSet<string> gia, double[] contorno, double distacco,
            List<Dictionary<string, object>> nate, List<Dictionary<string, object>> smussi,
            List<string> altrove, List<string> falliti)
        {
            var fatti = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in tutti)
            {
                if (s.DiSmusso == null || s.Verso != "obliquo" || fatti.Contains(s.DiSmusso)) continue;
                fatti.Add(s.DiSmusso);
                string chiave = "smusso|" + s.DiSmusso;
                if (gia.Contains(chiave))
                {
                    altrove.Add("smusso «" + s.DiSmusso + "» (gia' quotato in questa tavola)");
                    continue;
                }
                Spigolo gamba = null;
                foreach (var t in tutti)
                {
                    if (t.DiSmusso != null || t.Verso == "obliquo") continue;
                    foreach (var p in new[] { t.DaModello, t.AModello })
                        foreach (var q in new[] { s.DaModello, s.AModello })
                            if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) gamba = t;
                    if (gamba != null) break;
                }
                if (gamba == null)
                {
                    falliti.Add("smusso «" + s.DiSmusso + "» (nessuno spigolo a cui appoggiare la quota da smusso)");
                    continue;
                }
                // Il testo FUORI dal pezzo, dalla parte dello smusso: dal centro
                // della vista verso lo smusso, oltre il contorno.
                double mx = (s.DaFoglio[0] + s.AFoglio[0]) / 2, my = (s.DaFoglio[1] + s.AFoglio[1]) / 2;
                double cx = (contorno[0] + contorno[2]) / 2, cy = (contorno[1] + contorno[3]) / 2;
                double vx = mx - cx, vy = my - cy, l = Math.Sqrt(vx * vx + vy * vy);
                if (l < 1e-9) { vx = -1; vy = 1; l = Math.Sqrt(2); }
                double x = mx + vx / l * (distacco + 0.006), y = my + vy / l * (distacco + 0.006);

                int quotePrima = ContaQuote(disegno);
                IDisplayDimension quota = null;
                try
                {
                    modello.ClearSelection2(true);
                    if ((spigoli[s.Indice] as IEntity).Select4(false, null) &&
                        (spigoli[gamba.Indice] as IEntity).Select4(true, null))
                        quota = disegno.AddChamferDim(x, y, 0) as IDisplayDimension;
                }
                catch { }
                if (NonCresciuta(disegno, quotePrima, quota))
                {
                    falliti.Add("smusso «" + s.DiSmusso + "» (la quota da smusso non e' nata)");
                    continue;
                }
                // A quota null here was BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80).
                // AddChamferDim.html, Return Value: «Pointer to the newly created
                // chamfer dimension»; when it is null and the count went up, the
                // dimension is there and its two values cannot be read: the reads
                // below fail inside their try, and angle, side and width stay null.
                var descritta = DescriviQuota(quota, s.Indice);
                descritta["orientation"] = "chamfer dimension";
                descritta["position_mm"] = DovEFinita(quota);

                // LA QUOTA DA SMUSSO E' FATTA DI DUE QUOTE, e la prima e'
                // L'ANGOLO. Misurato il 25/9/2026: GetDimension2(0) = 0,785 rad
                // (45°), GetDimension2(1) = 7,071 mm, la larghezza della faccia
                // di smusso — e il numero sul foglio e' «5 x 45°». Il lato che si
                // legge e' quindi larghezza x cos(angolo). Per indice si
                // riportava 785,398 «mm», cioe' l'angolo in radianti per mille:
                // si distinguono per TIPO (`swDimensionParamType_e`: lineare 0,
                // angolare 1), non per posizione.
                double? angolo = null, larghezza = null;
                for (int k = 0; k < 2; k++)
                {
                    try
                    {
                        var dk = quota.GetDimension2(k) as IDimension;
                        if (dk == null) continue;
                        int tipo = dk.GetType();
                        if (tipo == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular) angolo = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                        else if (tipo == (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear) larghezza = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                    }
                    catch { }
                }
                object lato = null;
                if (angolo.HasValue && larghezza.HasValue)
                    lato = Math.Round(larghezza.Value * Math.Cos(angolo.Value) * 1000.0, 3);
                descritta["mm"] = lato;
                var riga = new Dictionary<string, object>
                {
                    ["feature"] = s.DiSmusso,
                    ["leg_mm"] = lato,
                    ["angle_deg"] = angolo.HasValue ? (object)Math.Round(angolo.Value * 180.0 / Math.PI, 2) : null,
                    ["face_width_mm"] = larghezza.HasValue ? (object)Math.Round(larghezza.Value * 1000.0, 3) : null,
                };
                smussi.Add(riga);
                if (quota != null) nate.Add(descritta);
                else RecordBornWithoutObject(nate, descritta, falliti,
                    "chamfer «" + s.DiSmusso + "» (the chamfer dimension was born but SOLIDWORKS did not hand it " +
                    "back: its values were not read back, check it on the sheet)");
                gia.Add(chiave);
            }
        }

        /// <summary>Un punto del modello sul foglio (metri), o null.</summary>
        private static double[] SulFoglio(double[] p, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var q = ((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr) as IMathPoint;
                var fuori = q?.ArrayData as double[];
                return fuori != null && fuori.Length >= 2 ? fuori : null;
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
        private static string ChiaveNelModello(double[] da, double[] a)
        {
            double dx = a[0] - da[0], dy = a[1] - da[1], dz = a[2] - da[2];
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l < 1e-9) return null;
            int asse = Math.Abs(dx) / l > 0.999 ? 0 : Math.Abs(dy) / l > 0.999 ? 1 : Math.Abs(dz) / l > 0.999 ? 2 : -1;
            if (asse < 0) return null;
            double v0 = Math.Min(da[asse], a[asse]) * 1000.0, v1 = Math.Max(da[asse], a[asse]) * 1000.0;
            return "XYZ"[asse] + "|" + Math.Round(v0, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                   "|" + Math.Round(v1, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Il nome della lavorazione di smusso a cui lo spigolo appartiene
        /// (una delle sue due facce viene da uno smusso), o null.
        /// </summary>
        private static string TocCaUnoSmusso(IEdge spigolo)
        {
            try
            {
                var facce = spigolo.GetTwoAdjacentFaces2() as object[];
                if (facce == null) return null;
                foreach (var o in facce)
                {
                    var f = (o as IFace2)?.GetFeature() as IFeature;
                    if (f != null && f.GetTypeName2() == "Chamfer") return f.Name;
                }
            }
            catch { }
            return null;
        }

        /// <summary>L'ingombro della vista adesso, o `null` se non si legge.</summary>
        private static double[] ContornoDi(IView vista)
        {
            try { return vista.GetOutline() as double[]; }
            catch { return null; }
        }

        /// <summary>
        /// L'ingombro della vista e' cambiato dopo aver messo le quote?
        ///
        /// «sconosciuto» quando una delle due letture non c'e': non «no». Un
        /// confronto che non si e' potuto fare non e' un confronto andato bene.
        /// </summary>
        private static string ContornoCresciuto(double[] prima, double[] dopo)
        {
            if (prima == null || dopo == null || prima.Length < 4 || dopo.Length < 4)
                return "sconosciuto";
            for (int i = 0; i < 4; i++)
                if (Math.Abs(prima[i] - dopo[i]) > 1e-9) return "si";
            return "no";
        }

        /// <summary>Da metri di foglio a millimetri, per la risposta.</summary>
        private static object InMillimetri(double[] v)
        {
            if (v == null) return null;
            var fuori = new object[v.Length];
            for (int i = 0; i < v.Length; i++) fuori[i] = Math.Round(v[i] * 1000, 2);
            return fuori;
        }

        /// <summary>Dove la quota e' finita davvero, in millimetri di foglio.</summary>
        private static object DovEFinita(IDisplayDimension d)
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
        /// of dimensions went up and the call returned null. <see cref="NonCresciuta"/>
        /// is asked first, and only a dimension that did not grow is «not born»;
        /// one that grew goes on as the others, described through
        /// <see cref="DescriviQuota"/> with null (as the edge dimensions of
        /// QuotaInTavola always were), and here it gets its row and a line of
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
        private static Dictionary<string, object> DescriviQuota(IDisplayDimension d, int spigolo)
        {
            var fuori = new Dictionary<string, object> { ["edge"] = spigolo };
            if (d == null)
            {
                // La quota c'e' — il conteggio e' salito — ma l'oggetto non e'
                // tornato. Si dice, invece di far sparire la riga: un elenco
                // piu' corto del conteggio farebbe cercare un difetto che non
                // c'e'. Every call that places a dimension can end here (N80), not
                // only AddDimension, so the text names none.
                fuori["name"] = "(born, but the call that placed it did not hand the object back)";
                fuori["mm"] = null;   // not read, not zero
                return fuori;
            }
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                if (dim != null)
                {
                    try { fuori["name"] = dim.FullName; } catch { }
                    try { fuori["mm"] = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000, 3); } catch { }
                }
            }
            catch { }
            if (!fuori.ContainsKey("mm"))
                fuori["mm"] = null;   // «non l'ho potuto leggere», non «zero»
            return fuori;
        }

        /// <summary>
        /// I millimetri delle quote nate, in fila e senza l'indice dello
        /// spigolo. Le quote che non si e' potuto rileggere **non entrano**:
        /// un `null` in mezzo a dei numeri si confronterebbe come stringa
        /// vuota e passerebbe qualunque «non contiene».
        /// </summary>
        private static object[] MillimetriInFila(List<Dictionary<string, object>> quote)
        {
            var fuori = new List<object>();
            foreach (var q in quote)
            {
                object v;
                if (q.TryGetValue("mm", out v) && v != null) fuori.Add(v);
            }
            return fuori.ToArray();
        }

        private static string ValoriInRiga(List<Dictionary<string, object>> quote)
        {
            var pezzi = new List<string>();
            foreach (var q in quote)
            {
                object v;
                pezzi.Add(q.TryGetValue("mm", out v) && v != null ? v + " mm" : "?");
            }
            return string.Join(", ", pezzi);
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
        private static IView RisolviVista(IDrawingDoc disegno, List<string> nomi, string voluta, out string motivo)
        {
            motivo = null;
            if (!string.IsNullOrWhiteSpace(voluta) && !NomeDiVistaConosciuto(nomi, voluta))
            {
                var uid = System.Text.RegularExpressions.Regex.Match(voluta.Trim(), @"^[Vv](\d+)$");
                if (uid.Success)
                {
                    int k = int.Parse(uid.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (k < 1 || k > nomi.Count)
                    {
                        motivo = "Non c'e' la vista «" + voluta + "»: la tavola ne ha " + nomi.Count +
                                 " (da V1 a V" + nomi.Count + ").";
                        return null;
                    }
                    voluta = nomi[k - 1];
                }
            }
            if (!string.IsNullOrWhiteSpace(voluta) && !NomeDiVistaConosciuto(nomi, voluta))
            {
                motivo = "Non c'e' nessuna vista che si chiami «" + voluta + "». Ci sono: " +
                         string.Join(", ", nomi) + ".";
                return null;
            }
            return VistaPerNome(disegno, voluta);
        }

        /// <summary>La vista chiesta per nome, o la prima vera se non si dice.</summary>
        private static IView VistaPerNome(IDrawingDoc disegno, string voluta)
        {
            try
            {
                // La prima «view» e' il FOGLIO: le viste vere cominciano dopo.
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                if (string.IsNullOrWhiteSpace(voluta)) return v;
                while (v != null)
                {
                    string suo = "";
                    try { suo = v.GetName2() ?? ""; } catch { }
                    if (StessoNome(suo, voluta)) return v;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }
    }
}
