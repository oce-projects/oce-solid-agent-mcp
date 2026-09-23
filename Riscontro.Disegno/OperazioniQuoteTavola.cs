using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// QUOTE SU UNA VISTA DI TAVOLA, CREATE — non importate (voce K10).
    ///
    /// ── PERCHE' ESISTE, E PERCHE' NON SI CHIAMA «IMPORTA» ───────────────
    ///
    /// `quote_dal_modello` **importa** le quote che il modello ha gia', con
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
                    "vista di disegno. Crea prima la tavola con «crea_disegno».");

            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito(
                    "La tavola non ha viste: le quote non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «viste_standard» o «vista_modello».");

            string voluta = Str(a, "vista", null);
            if (!string.IsNullOrWhiteSpace(voluta) && !NomeDiVistaConosciuto(nomi, voluta))
                return Fallito(
                    "Non c'e' nessuna vista che si chiami «" + voluta + "». Ci sono: " +
                    string.Join(", ", nomi) + ".");

            IView vista = VistaPerNome(disegno, voluta);
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

            int disponibili = spigoli == null ? 0 : spigoli.Length;
            if (disponibili == 0)
                return Fallito(
                    "La vista «" + nomeVista + "» non mostra nessuno spigolo quotabile. " +
                    "Su una vista vuota, o su una vista in cui il modello non e' " +
                    "risolto, non c'e' niente da quotare — e mettere zero quote " +
                    "rispondendo «fatto» sarebbe peggio.");

            // QUALI SPIGOLI. Senza dirlo si quotano i primi, e quanti si dice.
            var scelti = new List<int>();
            var elencoChiesto = Lista(a, "spigoli");
            if (elencoChiesto.Count > 0)
            {
                foreach (var o in elencoChiesto)
                {
                    int i;
                    if (!int.TryParse((o ?? "").Trim(), out i))
                        return Fallito("«spigoli» vuole numeri, e «" + o + "» non lo e'.");
                    if (i < 0 || i >= disponibili)
                        return Fallito(
                            "Lo spigolo " + i + " non c'e': la vista «" + nomeVista +
                            "» ne mostra " + disponibili + " (da 0 a " + (disponibili - 1) + ").");
                    if (!scelti.Contains(i)) scelti.Add(i);
                }
            }
            else
            {
                int quante = (int)Num(a, "quante", 4);
                if (quante <= 0)
                    return Fallito("«quante» dev'essere almeno 1: zero quote non sono una quota.");
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

            double distacco = Num(a, "distacco_mm", 12) / 1000.0;
            double passo = Num(a, "passo_mm", 10) / 1000.0;

            // ── COME STA OGNI SPIGOLO SUL FOGLIO ────────────────────────
            //
            // Prima si guarda, poi si decide dove va la quota. La stesura
            // precedente saltava questo passo e metteva TUTTE le quote alla
            // stessa X — il centro della vista — scendendo di un passo per
            // ognuna: per uno spigolo verticale SOLIDWORKS costruisce allora
            // una quota la cui linea di misura passa per quella X, cioe' **in
            // mezzo al disegno**. E' il difetto che si vedeva a occhio.
            var falliti = new List<string>();
            var misurati = new List<Spigolo>();
            var doppioni = new List<string>();
            var visti = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in scelti)
            {
                var s = LeggiSpigolo(vista, spigoli[i], i);
                if (s == null) { falliti.Add(i + " (non riesco a misurarlo sul foglio)"); continue; }

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
                visti[s.Chiave] = i;
                misurati.Add(s);
            }

            if (misurati.Count == 0)
                return Fallito(
                    "Nessuno spigolo da quotare e' rimasto: " + scelti.Count + " provati, " +
                    doppioni.Count + " erano doppioni di altri" +
                    (falliti.Count == 0 ? "" : ", " + falliti.Count + " non misurabili") +
                    ". Su un rettangolo bastano due quote, e chiederne quattro ne " +
                    "lascia due sole: e' la regola, non un difetto.");

            // DALLE PICCOLE ALLE GRANDI, allontanandosi dal pezzo: «le linee di
            // misura andranno disegnate in ordine progressivo, dalle minori
            // alle maggiori, allontanandosi dal contorno delle figure».
            misurati.Sort((x, y) => x.Lunghezza.CompareTo(y.Lunghezza));

            int prima = ContaQuote(disegno);

            // ── LA TRAPPOLA DEL MODALE, spenta e rimessa ────────────────
            bool chiedevaIlValore = true;
            bool spenta = false;
            var nate = new List<Dictionary<string, object>>();
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
                int sotto = 0, diLato = 0, oblique = 0;

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
                        //
                        // La frase «orizzontali sotto, verticali di lato» non
                        // sta testualmente in nessuna fonte: e' la conseguenza
                        // obbligata dei due criteri, e si dice qui perche' chi
                        // legge non debba dedurla di nuovo.
                        double x, y;
                        if (s.Verso == "orizzontale")
                        {
                            x = s.Mezzo;
                            y = contorno[1] - distacco - sotto * passo;
                            sotto++;
                        }
                        else if (s.Verso == "verticale")
                        {
                            x = contorno[2] + distacco + diLato * passo;
                            y = s.Mezzo;
                            diLato++;
                        }
                        else
                        {
                            // OBLIQUO: non c'e' una fascia giusta, e metterlo
                            // con le orizzontali lo farebbe sembrare tale. Va
                            // a sinistra, da solo, e la risposta lo dice.
                            x = contorno[0] - distacco - oblique * passo;
                            y = (contorno[1] + contorno[3]) / 2;
                            oblique++;
                        }

                        var natura = modello.Extension.AddDimension(x, y, 0, 0)
                                     as IDisplayDimension;
                        try { modello.EditRebuild3(); } catch { }

                        if (ContaQuote(disegno) <= quotePrima)
                        {
                            // NON SI GUARDA SE `AddDimension` HA RISPOSTA: si
                            // guarda se la quota c'e'. E' la regola di casa —
                            // il valore che torna da chi ha fatto la cosa non
                            // e' la misura della cosa.
                            falliti.Add(s.Indice + " (chiamata andata, quota non nata)");
                            continue;
                        }

                        var descritta = DescriviQuota(natura, s.Indice);
                        descritta["orientamento"] = s.Verso;
                        descritta["posizione_mm"] = DovEFinita(natura);
                        nate.Add(descritta);
                    }
                    catch (Exception ex)
                    {
                        var m = ex.Message ?? "";
                        falliti.Add(s.Indice + " (" + (m.Length > 90 ? m.Substring(0, 90) + "..." : m) + ")");
                    }
                }
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
            int messe = dopo - prima;

            if (messe <= 0)
                return Fallito(
                    "Nessuna quota e' nata (erano " + prima + ", sono " + dopo + "). " +
                    "La vista «" + nomeVista + "» mostra " + disponibili + " spigoli e ne " +
                    "ho provati " + scelti.Count +
                    (falliti.Count == 0 ? "" : ": " + string.Join(", ", falliti)) + ". " +
                    "Se la finestra «Modifica» e' rimasta accesa, la chiamata si appende " +
                    "invece di fallire: qui non e' successo, quindi e' altro.");

            double[] dopoContorno = ContornoDi(vista);

            var misure = new Dictionary<string, object>
            {
                ["vista"] = nomeVista,
                ["spigoli_disponibili"] = disponibili,
                ["spigoli_provati"] = scelti.Count,
                ["quote_prima"] = prima,
                ["quote_dopo"] = dopo,
                ["quote_messe"] = messe,
                // OGNI QUOTA COL SUO VALORE RILETTO, in millimetri. E' il pezzo
                // che distingue questa operazione da una che disegna numeri.
                ["quote"] = nate.ToArray(),
                // E LE STESSE MISURE IN FILA, SENZA L'ORDINE.
                //
                // `quote[0].mm` costringe chi prova a sapere **in che ordine**
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
                ["quote_mm"] = MillimetriInFila(nate),
                ["spigoli_non_quotati"] = falliti.ToArray(),

                // ── I DOPPIONI SI DICONO, NON SI NASCONDONO ─────────────
                //
                // Chiedere quattro spigoli di un rettangolo e vedersene
                // quotare due e' sorprendente finche' non si sa perche'. La
                // ragione e' una regola di disegno, non un difetto: «ciascun
                // elemento dell'oggetto deve essere quotato non piu' di una
                // volta» (UNI ISO 129-1, scheda Sammarone/Zanichelli), e
                // 120/10/120/10 e' quotatura ripetuta — «quindi errata».
                ["spigoli_scartati_doppioni"] = doppioni.ToArray(),

                // IL CONTORNO PRIMA E DOPO. Non e' accertato che `GetOutline()`
                // cresca con le annotazioni attaccate alla vista: se i due
                // numeri differiscono, cresce, e chi mettera' altre quote dopo
                // di noi deve saperlo invece di scoprirlo.
                ["contorno_vista_mm"] = InMillimetri(contorno),
                ["contorno_vista_dopo_mm"] = InMillimetri(dopoContorno),
                // IL CONFRONTO LO FA IL CODICE, non chi scrive l'attesa. Un
                // caso di banco puo' pretendere un valore, non paragonare due
                // campi fra loro: mettere qui il paragone gia' fatto e' l'unico
                // modo perche' diventi una riga verificabile.
                ["contorno_cresciuto"] = ContornoCresciuto(contorno, dopoContorno),
                ["distacco_usato_mm"] = Math.Round(distacco * 1000, 2),
                ["passo_usato_mm"] = Math.Round(passo * 1000, 2),

                ["dove_le_ho_messe"] =
                    "FUORI dalla sagoma della vista, in coordinate foglio: le " +
                    "orizzontali sotto, le verticali di lato, le oblique a sinistra, " +
                    "in ordine crescente allontanandosi dal pezzo. Viene da due " +
                    "criteri di norma — la linea di misura e' parallela alla " +
                    "dimensione a cui si riferisce, e sta per quanto possibile " +
                    "all'esterno delle figure (UNI ISO 129-1, nella scheda " +
                    "Sammarone/Zanichelli) — piu' la distanza: oltre 10 mm dal " +
                    "contorno e passo costante fra 7 e 12 su A3/A4.",

                ["cosa_non_guarda"] =
                    "QUALI quote servono, e se bastano. La norma distingue quote " +
                    "funzionali, non funzionali e ausiliarie, e una quota funzionale " +
                    "non si deve dedurre da altre: quello lo sa il progettista. So " +
                    "dire quante ne ho messe, non se il pezzo e' definito.\n" +
                    "E non guardo: se il TESTO di una quota si sovrappone alle linee " +
                    "del disegno (l'ingombro del riquadro di testo non si legge da " +
                    "una quota, solo da una nota); se si accavalla con quote messe da " +
                    "qualcun altro, col cartiglio o con un'altra vista; quale sistema " +
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
            /// <summary>«orizzontale», «verticale» oppure «obliquo».</summary>
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
        private static Spigolo LeggiSpigolo(IView vista, object entita, int indice)
        {
            try
            {
                var spigolo = entita as IEdge;
                if (spigolo == null) return null;
                var p = spigolo.GetCurveParams3() as ICurveParamData;
                if (p == null) return null;
                var da = p.StartPoint as double[];
                var a = p.EndPoint as double[];
                if (da == null || a == null || da.Length < 3 || a.Length < 3) return null;

                double[] m = null;
                try { m = vista.ModelToViewTransform?.ArrayData as double[]; } catch { }
                if (m == null || m.Length < 13) return null;

                // LA POSIZIONE DELLA VISTA SUL FOGLIO VA SOMMATA, e non e' una
                // rifinitura: senza, la prima prova ha messo la quota
                // orizzontale a x=0 e la verticale a y=5 mm, cioe' nell'angolo
                // in basso a sinistra del foglio, mentre la vista stava a
                // x 28..160, y 218..240. `ModelToViewTransform` porta il punto
                // nel sistema della VISTA, dove l'origine e' il centro della
                // vista; il foglio non lo sa.
                double[] pos = null;
                try { pos = vista.Position as double[]; } catch { }
                double ox = pos != null && pos.Length > 0 ? pos[0] : 0;
                double oy = pos != null && pos.Length > 1 ? pos[1] : 0;

                double[] daV = InVista(da, m), aV = InVista(a, m);
                daV[0] += ox; daV[1] += oy;
                aV[0] += ox; aV[1] += oy;
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
                    s.Verso = "orizzontale";
                    a0 = Math.Min(daV[0], aV[0]); a1 = Math.Max(daV[0], aV[0]);
                }
                else if (Math.Abs(dx) / lung < SOGLIA)
                {
                    s.Verso = "verticale";
                    a0 = Math.Min(daV[1], aV[1]); a1 = Math.Max(daV[1], aV[1]);
                }
                else
                {
                    s.Verso = "obliquo";
                    a0 = Math.Min(daV[0], aV[0]); a1 = Math.Max(daV[0], aV[0]);
                }
                s.Mezzo = (a0 + a1) / 2;
                s.Chiave = s.Verso + "|" + Math.Round(a0 * 100000) + "|" + Math.Round(a1 * 100000);
                return s;
            }
            catch { return null; }
        }

        /// <summary>Un punto del modello, ruotato e scalato come lo vede la vista.</summary>
        private static double[] InVista(double[] p, double[] m)
        {
            // `ArrayData` di una MathTransform: [0..8] la rotazione per righe,
            // [9..11] la traslazione, [12] la scala.
            double sc = m.Length > 12 && Math.Abs(m[12]) > 1e-12 ? m[12] : 1.0;
            return new[]
            {
                (m[0] * p[0] + m[1] * p[1] + m[2] * p[2]) * sc,
                (m[3] * p[0] + m[4] * p[1] + m[5] * p[2]) * sc,
                (m[6] * p[0] + m[7] * p[1] + m[8] * p[2]) * sc,
            };
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
            var fuori = new Dictionary<string, object> { ["spigolo"] = spigolo };
            if (d == null)
            {
                // La quota c'e' — il conteggio e' salito — ma l'oggetto non e'
                // tornato. Si dice, invece di far sparire la riga: un elenco
                // piu' corto del conteggio farebbe cercare un difetto che non
                // c'e'.
                fuori["nome"] = "(nata, ma AddDimension non ha risposto l'oggetto)";
                return fuori;
            }
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                if (dim != null)
                {
                    try { fuori["nome"] = dim.FullName; } catch { }
                    try { fuori["mm"] = Math.Round(dim.GetSystemValue2("") * 1000, 3); } catch { }
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

        /// <summary>La vista chiesta per nome, o la prima vera se non si dice.</summary>
        private static IView VistaPerNome(IDrawingDoc disegno, string voluta)
        {
            try
            {
                // La prima «vista» e' il FOGLIO: le viste vere cominciano dopo.
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
