using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// GLI OCCHI SULLA TAVOLA.
    ///
    /// Lo stato del documento e' l'unica cosa che il modello vede: se una
    /// tavola gli arriva come «documento non di tipo parte», il modello puo'
    /// solo indovinare. Fino alla 0.39.0 era esattamente cosi' — una tavola
    /// era una riga di scuse.
    ///
    /// Qui si descrive quello su cui si lavora davvero: i FOGLI (formato,
    /// scala, quale e' attivo), le VISTE (nome, posizione, scala, quante quote
    /// portano, di quale modello sono) e i DOCUMENTI APERTI, che in questa
    /// famiglia non sono un dettaglio ma una precondizione.
    ///
    /// LA REGOLA GIA' PAGATA VALE ANCHE QUI: si scrive quello che AGGIUNGE.
    /// Non c'e' un elenco delle annotazioni una per una — sono centinaia su
    /// una tavola normale e nessuno le nomina; c'e' il loro CONTEGGIO, che e'
    /// quello che serve per accorgersi che una quota non e' arrivata.
    /// </summary>
    public partial class StatoDocumento
    {
        /// <summary>Quante viste si descrivono per esteso, al massimo.</summary>
        private const int MaxViste = 24;

        /// <summary>
        /// I documenti aperti adesso.
        ///
        /// Se `sw` e' null si torna almeno il documento che si sta leggendo:
        /// meglio una riga vera che un elenco assente, perche' l'assenza si
        /// legge come «non c'e' nient'altro» ed e' una bugia.
        /// </summary>
        private static List<Dictionary<string, object>> DocumentiAperti(
            ISldWorks sw, IModelDoc2 attivo)
        {
            var fuori = new List<Dictionary<string, object>>();
            string percorsoAttivo = "", titoloAttivo = "";
            try { percorsoAttivo = attivo?.GetPathName() ?? ""; } catch { }
            // IL TITOLO, NON SOLO IL PERCORSO. Un documento mai salvato
            // non ha percorso, e con il solo confronto sul percorso in un
            // elenco di documenti nuovi non risultava attivo NESSUNO.
            try { titoloAttivo = attivo?.GetTitle() ?? ""; } catch { }

            if (sw == null) return fuori;
            try
            {
                var d = sw.GetFirstDocument() as IModelDoc2;
                int guardia = 0;
                while (d != null && guardia++ < 200)
                {
                    string percorso = "", titolo = "";
                    int tipo = 0;
                    try { percorso = d.GetPathName() ?? ""; } catch { }
                    try { titolo = d.GetTitle() ?? ""; } catch { }
                    try { tipo = d.GetType(); } catch { }

                    var voce = new Dictionary<string, object>
                    {
                        ["titolo"] = titolo,
                        ["tipo"] = OperazioniSw.NomeTipoDocumento(tipo),
                    };
                    // Il percorso solo se c'e': un documento mai salvato non ne
                    // ha, e una stringa vuota in un elenco di percorsi e' un
                    // invito a usarla.
                    if (!string.IsNullOrWhiteSpace(percorso)) voce["percorso"] = percorso;
                    bool eLui =
                        !string.IsNullOrWhiteSpace(percorsoAttivo)
                            ? string.Equals(percorso, percorsoAttivo,
                                  StringComparison.OrdinalIgnoreCase)
                            : (!string.IsNullOrWhiteSpace(titoloAttivo) &&
                               string.Equals(titolo, titoloAttivo, StringComparison.Ordinal));
                    if (eLui) voce["attivo"] = true;

                    fuori.Add(voce);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>Fogli, viste e conteggi di una tavola.</summary>
        private static void DescriviTavola(IDrawingDoc disegno, Dictionary<string, object> dati, ISldWorks sw)
        {
            if (disegno == null)
            {
                dati["nota"] = "tavola illeggibile";
                return;
            }

            // ── i fogli ───────────────────────────────────────────────────
            string attivo = "";
            try { attivo = (disegno.GetCurrentSheet() as ISheet)?.GetName() ?? ""; } catch { }

            var fogli = new List<Dictionary<string, object>>();
            try
            {
                var nomi = disegno.GetSheetNames() as string[];
                if (nomi != null)
                {
                    for (int i = 0; i < nomi.Length; i++)
                    {
                        var voce = new Dictionary<string, object>
                        {
                            ["uid"] = "S" + (i + 1),
                            ["nome"] = nomi[i],
                        };
                        if (string.Equals(nomi[i], attivo, StringComparison.Ordinal))
                            voce["attivo"] = true;

                        // Le proprieta' del foglio si leggono solo da quello
                        // ATTIVO senza cambiare foglio, e cambiare foglio per
                        // leggere sarebbe una lettura che modifica: si legge
                        // quello che si puo' e si tace il resto.
                        if (voce.ContainsKey("attivo"))
                        {
                            var p = ProprietaFoglio(disegno);
                            if (p != null)
                            {
                                voce["formato_mm"] = new object[]
                                {
                                    Math.Round(Unita.Mm(p[5]), 1),
                                    Math.Round(Unita.Mm(p[6]), 1),
                                };
                                if (p[3] != 0)
                                    voce["scala"] = FormattaScala(p[2], p[3]);
                                // IL DIEDRO SI VEDE. E' l'unica cosa di una
                                // tavola che puo' essere sbagliata senza che
                                // nessuna misura se ne accorga: destra e
                                // sinistra scambiate, tavola plausibile,
                                // pezzo specchiato. Se non e' scritto qui, il
                                // modello non ha modo di guardarlo.
                                if (p.Length > 4)
                                    voce["diedro"] = p[4] != 0 ? "primo" : "terzo";
                            }
                        }
                        fogli.Add(voce);
                    }
                }
            }
            catch { }
            if (fogli.Count > 0) dati["fogli"] = fogli;

            // ── le viste ──────────────────────────────────────────────────
            //
            // LA PRIMA VISTA NON E' UNA VISTA: e' il FOGLIO. `GetFirstView()`
            // torna il foglio, e le viste vere cominciano dal `GetNextView()`.
            // Chi lo dimentica descrive un foglio come se fosse una vista, e
            // poi quota un oggetto che non ha geometria.
            var viste = new List<Dictionary<string, object>>();
            // I TOTALI SONO int? (voce Z-C8): una vista che non risponde fa
            // null il totale, non un totale piu' basso detto come vero. Solo le
            // viste si contano sempre — si stanno scorrendo.
            int visteTotali = 0;
            int? quoteTotali = 0, crociTotali = 0;
            int? gtolTot = 0, datumTot = 0, saldTot = 0, rugTot = 0, noteTot = 0, annTot = 0;
            int orfaneTot = 0;
            // Dimensions attached to a silhouette: not counted as orphans,
            // because IsSame cannot check them (N3, see QuoteSenzaSpigolo).
            int notVerifiableTotal = 0;
            int? numeriFalsiTot = 0;
            var numeriFalsi = new List<Dictionary<string, object>>();
            bool orfaneSconosciute = false;
            // ── IL FOGLIO SI CONTA A PARTE, E NON E' UNA RIFINITURA ────────
            //
            // Misurato il 18/9/2026: le note del cartiglio — trentanove su
            // questo modello di tavola, sessantasette in tutto — stanno sul
            // FOGLIO, non su una vista. E `InsertNote`, che oggi e' **l'unica**
            // API di annotazione che funziona fuori processo (voce E2), mette
            // la nota sul FOGLIO: 67 -> 68, mentre tutte e tre le viste restano
            // a zero.
            //
            // Contando solo le viste, `note` sarebbe un campo che NON PUO'
            // diventare vero con gli strumenti di oggi. **Un campo che non puo'
            // cambiare non e' una misura: e' una promessa.** Quindi il foglio
            // ha i suoi sei numeri, con lo stesso nome e in un cassetto suo.
            try
            {
                var foglioVista = disegno.GetFirstView() as IView;
                if (foglioVista != null)
                    dati["annotazioni_del_foglio"] = Annotazioni(foglioVista);
            }
            catch { }

            try
            {
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    visteTotali++;
                    int? quote = Letto(() => v.GetDimensionCount());
                    quoteTotali += quote;

                    // LE CROCI DI CENTRO SI CONTANO QUI, e non solo dentro
                    // l'operazione che le mette (voce W2.11). E' la regola di
                    // GA5: **lo stato e l'operazione devono raccontare la stessa
                    // cosa, leggendola per conto proprio.** Se un giorno
                    // divergessero, una delle due mentirebbe e non si saprebbe
                    // quale.
                    // ENUMERATE, non GetCenterMarkCount: quello non vede le croci
                    // messe da SOLIDWORKS alla creazione della vista (26/9/2026).
                    int? croci = Letto(() => { int n = ContaCroci.DellaVista(v); if (n < 0) throw new InvalidOperationException(); return n; });
                    crociTotali += croci;

                    // IL NUMERO CHE SI STAMPA E' QUELLO DEL MODELLO? (voce Z-C2)
                    int? falsi = QuoteColNumeroNonDelModello(v, numeriFalsi);
                    numeriFalsiTot += falsi;

                    // ── LE ANNOTAZIONI, UNA FAMIGLIA PER VOLTA (voce Z-E1) ──
                    //
                    // PERCHE' SERVONO PRIMA delle voci che le mettono, e non
                    // dopo. Dieci voci di tavola aspettano di scrivere
                    // rugosita', datum, simboli di saldatura, GD&T. Ognuna
                    // chiamerebbe un'API che **risponde di si' senza garantire
                    // niente** — e quattro di quelle API tornano `Void`, che e'
                    // un bottone e non un'API. Senza un contatore INDIPENDENTE
                    // letto dallo stato, ogni voce sarebbe verde per la parola
                    // dell'operazione stessa.
                    //
                    // E' la regola di GA5, applicata prima del bisogno: **lo
                    // stato e l'operazione devono raccontare la stessa cosa,
                    // leggendola per conto proprio.**
                    int? gtol = Letto(() => v.GetGTolCount());
                    int? datum = Letto(() => v.GetDatumTagCount());
                    int? saldature = Letto(() => v.GetWeldSymbolCount());
                    // `GetSFSymbolCount`, NON `GetSurfaceFinishSymbolCount`:
                    // il secondo e' il nome per simmetria con
                    // `InsertSurfaceFinishSymbol`, e non esiste.
                    int? rugosita = Letto(() => v.GetSFSymbolCount());
                    int? note = Letto(() => v.GetNoteCount());
                    int? annotazioni = Letto(() => v.GetAnnotationCount());
                    gtolTot += gtol; datumTot += datum; saldTot += saldature;
                    rugTot += rugosita; noteTot += note; annTot += annotazioni;

                    // LE QUOTE RIMASTE SENZA SPIGOLO (voce Z-A2).
                    //
                    // `IAnnotation.IsDangling` e' MORTO per le quote in tavola
                    // fuori processo: misurato il 24/9/2026 con
                    // `Controlli/Orfana`, resta falso su una quota il cui
                    // spigolo e' stato soppresso, mentre la quota continua a
                    // dire 20 mm. Quello che lo vede e' chiedersi se lo spigolo
                    // a cui e' attaccata e' ancora fra quelli che la vista
                    // MOSTRA (1 prima, 0 dopo). null vuol dire «non lo so»
                    // (voce Z-C8), non zero.
                    int notVerifiable;
                    int? orfane = QuoteSenzaSpigolo(sw, v, out notVerifiable);
                    if (orfane.HasValue) orfaneTot += orfane.Value; else orfaneSconosciute = true;
                    notVerifiableTotal += notVerifiable;

                    if (viste.Count < MaxViste)
                    {
                        var voce = new Dictionary<string, object>
                        {
                            ["uid"] = "V" + visteTotali,
                        };
                        try { voce["nome"] = v.Name; } catch { }
                        try { voce["tipo"] = NomeTipoVista(v.Type); } catch { }

                        var pos = PosizioneVista(v);
                        if (pos != null) voce["posizione_mm"] = pos;

                        try
                        {
                            double s = v.ScaleDecimal;
                            if (s > 0) voce["scala"] = Math.Round(s, 4);
                        }
                        catch { }

                        // Zero quote non e' un dettaglio: e' la differenza fra
                        // una tavola e un disegnino. Si scrive sempre.
                        voce["quote"] = quote;
                        // E zero croci non e' un dettaglio nemmeno lui: e' la
                        // differenza fra una tavola che si puo' forare e una
                        // che si puo' solo guardare. Si scrive sempre, per lo
                        // stesso motivo — **un campo che compare solo quando ha
                        // un valore interessante insegna a non cercarlo.**
                        voce["croci_di_centro"] = croci;
                        voce["quote_numero_non_del_modello"] = falsi;
                        voce["quote_orfane"] = orfane;
                        // Unknown when the orphans are unknown: the same reading.
                        voce["dimensions_not_verifiable"] = orfane.HasValue ? (object)notVerifiable : null;

                        // SEMPRE, ANCHE TUTTE A ZERO, e per la stessa ragione
                        // delle due righe qui sopra: un campo che compare solo
                        // quando ha un valore interessante insegna a non
                        // cercarlo, e chi non lo trova conclude che l'API non
                        // sappia rispondere invece che il numero sia zero.
                        voce["annotazioni"] = Sei(gtol, datum, saldature, rugosita, note, annotazioni);

                        try
                        {
                            var rif = v.ReferencedDocument as IModelDoc2;
                            var p = rif?.GetPathName();
                            if (!string.IsNullOrWhiteSpace(p)) voce["modello"] = p;
                        }
                        catch { }

                        viste.Add(voce);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch
            {
                // Il giro si e' interrotto a meta': i totali sono di una parte
                // delle viste, e nessuno lo saprebbe. Diventano «non lo so».
                quoteTotali = crociTotali = null;
                gtolTot = datumTot = saldTot = rugTot = noteTot = annTot = null;
                dati["viste_lette_a_meta"] = true;
            }

            if (viste.Count > 0) dati["viste"] = viste;
            dati["viste_totali"] = visteTotali;
            dati["quote_totali"] = quoteTotali;
            dati["croci_di_centro_totali"] = crociTotali;
            dati["quote_numero_non_del_modello_totali"] = numeriFalsiTot;
            if (numeriFalsi.Count > 0)
            {
                dati["quote_numero_non_del_modello"] = numeriFalsi;
                dati["attenzione_numeri"] =
                    numeriFalsi.Count + (numeriFalsi.Count == 1 ? " quota stampa" : " quote stampano") +
                    " un numero che non e' quello del modello (sovrascritto, o il valore spento " +
                    "con del testo al suo posto). La geometria e' giusta, la carta no: in officina " +
                    "si lavora col numero stampato.";
            }
            dati["annotazioni_totali"] = Sei(gtolTot, datumTot, saldTot, rugTot, noteTot, annTot);
            dati["quote_orfane_totali"] = orfaneSconosciute && orfaneTot == 0 ? (object)null : orfaneTot;
            dati["dimensions_not_verifiable_total"] =
                orfaneSconosciute && notVerifiableTotal == 0 ? (object)null : notVerifiableTotal;
            if (orfaneTot > 0)
                dati["attenzione_quote"] = orfaneTot + " quote sono attaccate a spigoli che le viste non " +
                    "mostrano piu': sul foglio si stampano col numero di prima, appese al niente. Di " +
                    "solito il pezzo e' cambiato dopo che la tavola e' stata quotata.";

            // DUE CECITA' DICHIARATE, e tutte e due porterebbero a conclusioni
            // sbagliate se restassero implicite.
            dati["cosa_non_so_delle_annotazioni"] =
                // 1. IL CARTIGLIO NON E' QUI DENTRO. Questi conteggi girano
                //    sulle VISTE VERE; le note del formato del foglio stanno
                //    sulla prima «vista», che e' il foglio, e sono decine — su
                //    questo modello di tavola, trentanove. Chi somma le viste e
                //    confronta col totale del documento trova una differenza
                //    grande e conclude che si sia perso qualcosa.
                "«annotazioni_totali» somma le VISTE; le note del cartiglio " +
                "stanno sul FOGLIO e si contano a parte, in " +
                "«annotazioni_del_foglio» — su questo modello di tavola sono " +
                "sessantasette, e sommarle alle viste farebbe sembrare quotata " +
                "una tavola vuota. E un'annotazione su un layer non stampabile " +
                "esiste, viene contata, e nel PDF non c'e': il conteggio dice " +
                "cosa c'e' nel documento, non cosa esce dalla stampante. " +
                "«quote_orfane» checks whether the edges and vertices each dimension is " +
                "attached to are among those the view shows. A dimension attached to a " +
                "SILHOUETTE (the apparent outline of a cylinder: the diameters of a turned " +
                "part seen from the side) cannot be checked, because SOLIDWORKS does not " +
                "match it with any silhouette the view shows: it is not counted as an " +
                "orphan but in «dimensions_not_verifiable». A dimension attached to a hidden " +
                "edge still reads as an orphan without being one.";
            if (visteTotali > viste.Count) dati["viste_troncate"] = true;

            // LA TAVOLA VUOTA VA DETTA, non lasciata dedurre da un elenco che
            // manca. E' lo stato in cui nasce ogni tavola, ed e' quello in cui
            // resta se `Create1stAngleViews2` ha detto «si'» senza fare niente.
            if (visteTotali == 0)
                dati["nota"] = "la tavola non ha ancora nessuna vista";
            else if (quoteTotali.HasValue && quoteTotali.Value == 0)
                dati["nota"] = "le viste ci sono ma non c'e' nessuna quota";
        }

        /// <summary>
        /// Quante quote della vista sono attaccate SOLO a spigoli o vertici che
        /// la vista non mostra piu' (voce Z-A2). null se non si puo' sapere:
        /// senza SOLIDWORKS in mano per `IsSame`, o su una vista troppo carica
        /// per confrontare tutto con tutto in un tempo ragionevole.
        ///
        /// Una quota senza nessun aggancio (una quota libera) non e' orfana: e'
        /// libera, e non si conta.
        ///
        /// SILHOUETTES ARE NOT VERIFIABLE (Z-C21.3, finding N3). The diameters of
        /// a turned part seen from the side hang on two silhouette edges
        /// (IAnnotation~GetAttachedEntities3.html: «swSelSilhouettes |
        /// ISilhouetteEdge»), and ISldWorks.IsSame never recognises them: probe
        /// probe_silhouette_issame (28/9/2026), stepped shaft Ø20/Ø30, a Ø20
        /// born on two silhouettes (attached types [46, 46], swSelSILHOUETTES):
        /// 0 hits against the silhouettes the view shows, right after the
        /// dimension, after a rebuild and after a model change, while
        /// silhouette against silhouette answered Same for all four (too loose
        /// to be used). With Edge and Vertex only, that Ø20 was 1 orphan out of
        /// 2 dimensions, and the length beside it 0. So a dimension that no
        /// visible edge or vertex holds and that is attached to a silhouette
        /// (IAnnotation~GetAttachedEntityTypes.html: «Array of longs ... that
        /// indicate the attached entity types as defined in swSelectType_e») is
        /// not an orphan: it goes in <paramref name="notVerifiable"/>.
        /// </summary>
        private static int? QuoteSenzaSpigolo(ISldWorks sw, IView v, out int notVerifiable)
        {
            notVerifiable = 0;
            if (sw == null) return null;
            try
            {
                var dims = v.GetDisplayDimensions() as object[];
                if (dims == null || dims.Length == 0) return 0;
                if (dims.Length > 60) return null;
                var visibili = new List<object>();
                foreach (int tipo in new[] { (int)swViewEntityType_e.swViewEntityType_Edge,
                                             (int)swViewEntityType_e.swViewEntityType_Vertex })
                {
                    var e = v.GetVisibleEntities2(null, tipo) as object[];
                    if (e != null) visibili.AddRange(e);
                }
                if (visibili.Count > 800) return null;
                int orfane = 0;
                int onSilhouettes = 0;
                foreach (var o in dims)
                {
                    var ann = (o as IDisplayDimension)?.GetAnnotation() as IAnnotation;
                    var attaccati = ann?.GetAttachedEntities3() as object[];
                    if (attaccati == null || attaccati.Length == 0) continue;
                    bool visto = false;
                    foreach (var a in attaccati)
                    {
                        foreach (var vv in visibili)
                            if (sw.IsSame(a, vv) == (int)swObjectEquality.swObjectSame) { visto = true; break; }
                        if (visto) break;
                    }
                    if (visto) continue;
                    if (IsAttachedToSilhouette(ann)) onSilhouettes++;
                    else orfane++;
                }
                notVerifiable = onSilhouettes;
                return orfane;
            }
            catch { notVerifiable = 0; return null; }
        }

        /// <summary>
        /// True when at least one attached entity of the annotation is a
        /// silhouette edge (swSelSILHOUETTES, 46 in the probe of N3). A types
        /// array that cannot be read is not a silhouette: the dimension keeps
        /// the old reading, orphan when nothing visible holds it.
        /// </summary>
        private static bool IsAttachedToSilhouette(IAnnotation ann)
        {
            try
            {
                var types = ann.GetAttachedEntityTypes() as Array;
                if (types == null) return false;
                foreach (var t in types)
                    if (Convert.ToInt32(t, System.Globalization.CultureInfo.InvariantCulture) ==
                        (int)swSelectType_e.swSelSILHOUETTES)
                        return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// I sei numeri di una vista (o del foglio), sempre tutti e sei.
        ///
        /// `tutte` si chiama cosi' e non «totale»: `GetAnnotationCount` **non
        /// e' la somma** delle altre cinque — conta anche le quote e cio' che
        /// nessuna delle cinque famiglie nomina. Chiamarlo «totale» farebbe
        /// aspettare una somma che non torna, e chi la vede non tornare cerca
        /// un difetto dove non c'e'.
        /// </summary>
        /// <summary>
        /// Quante quote della vista stampano un numero che NON e' quello del
        /// modello (voce Z-C2), o null se non si puo' leggere. Le trova, e ne
        /// mette al piu' dieci in `dettagli` con il modo.
        ///
        /// «La quota viene dal modello, non puo' mentire» vale finche' nessuno
        /// scrive sopra il numero. Misurato il 25/9/2026 (`prova0089
        /// testoquote`) sulle tre strade che l'interop offre:
        ///
        ///   appena nata                 GetOverride falso, ShowDimensionValue vero,
        ///                               GetText(0..4) tutti vuoti
        ///   SetOverride(true, 0,099)    GetOverride VERO, GetOverrideValue 99 mm
        ///   SetText(0, "99")            scrive il PREFISSO e SPEGNE il valore:
        ///                               ShowDimensionValue falso, GetText(1) «99»
        ///   ShowDimensionValue = false  il valore non si stampa
        ///
        /// Quindi bastano due letture, e prendono anche il caso che la voce
        /// temeva di piu': il numero sovrascritto che per caso combacia col
        /// modello resta segnalato, perche' si guarda la sovrascrittura, non il
        /// confronto dei valori.
        /// </summary>
        private static int? QuoteColNumeroNonDelModello(IView v, List<Dictionary<string, object>> dettagli)
        {
            int n = 0;
            try
            {
                var q = v.GetFirstDisplayDimension5() as IDisplayDimension;
                while (q != null)
                {
                    bool sovrascritta = q.GetOverride();
                    bool spento = !q.ShowDimensionValue;
                    if (sovrascritta || spento)
                    {
                        n++;
                        if (dettagli.Count < 10)
                        {
                            var dim = q.GetDimension2(0);
                            var voce = new Dictionary<string, object>();
                            try { voce["quota"] = dim.FullName; } catch { }
                            try { voce["vista"] = v.Name; } catch { }
                            try { voce["del_modello_mm"] = Math.Round(ApiHelpers.ReadDimensionInView(dim, v) * 1000.0, 3); } catch { }   // in the configuration of the view (Z-C21.7, S1)
                            if (sovrascritta)
                                voce["come"] = "numero sovrascritto: stampa " +
                                    Math.Round(q.GetOverrideValue() * 1000.0, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm";
                            else
                            {
                                string testo = "";
                                try { testo = (q.GetText(1) ?? "") + (q.GetText(2) ?? ""); } catch { }
                                voce["come"] = "valore spento" + (testo.Length > 0 ? ": al suo posto «" + testo + "»" : "");
                            }
                            dettagli.Add(voce);
                        }
                    }
                    q = q.GetNext5() as IDisplayDimension;
                }
            }
            catch { return null; }
            return n;
        }

        /// <summary>
        /// Un conteggio letto da una vista, o null se la chiamata non risponde
        /// (voce Z-C8). Il null si porta dietro nelle somme: `int?` sommato a
        /// null fa null, ed e' proprio quello che si vuole.
        /// </summary>
        private static int? Letto(Func<int> leggi)
        {
            try { return leggi(); }
            catch { return null; }
        }

        private static Dictionary<string, object> Sei(
            int? gtol, int? datum, int? saldature, int? rugosita, int? note, int? tutte)
        {
            return new Dictionary<string, object>
            {
                ["gtol"] = gtol,
                ["datum"] = datum,
                ["saldature"] = saldature,
                ["rugosita"] = rugosita,
                ["note"] = note,
                ["tutte"] = tutte,
            };
        }

        /// <summary>I sei numeri letti da una vista, ognuno per conto suo.</summary>
        private static Dictionary<string, object> Annotazioni(IView v)
        {
            return Sei(
                Letto(() => v.GetGTolCount()), Letto(() => v.GetDatumTagCount()),
                Letto(() => v.GetWeldSymbolCount()), Letto(() => v.GetSFSymbolCount()),
                Letto(() => v.GetNoteCount()), Letto(() => v.GetAnnotationCount()));
        }

        private static double[] ProprietaFoglio(IDrawingDoc disegno)
        {
            try
            {
                var foglio = disegno.GetCurrentSheet() as ISheet;
                var p = foglio?.GetProperties2() as double[];
                return (p != null && p.Length >= 7) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>«1:2» invece di «0,5»: e' come si scrive su una tavola.</summary>
        private static string FormattaScala(double numeratore, double denominatore)
        {
            try
            {
                return Arrotonda(numeratore) + ":" + Arrotonda(denominatore);
            }
            catch { return null; }
        }

        private static string Arrotonda(double x)
        {
            double r = Math.Round(x, 4);
            return r == Math.Floor(r)
                ? ((long)r).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : r.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object[] PosizioneVista(IView v)
        {
            try
            {
                var p = v.Position as double[];
                if (p == null || p.Length < 2) return null;
                return new object[]
                {
                    Math.Round(Unita.Mm(p[0]), 2),
                    Math.Round(Unita.Mm(p[1]), 2),
                };
            }
            catch { return null; }
        }

        private static string NomeTipoVista(int tipo)
        {
            switch (tipo)
            {
                case (int)swDrawingViewTypes_e.swDrawingSheet: return "foglio";
                case (int)swDrawingViewTypes_e.swDrawingProjectedView: return "proiezione";
                case (int)swDrawingViewTypes_e.swDrawingSectionView: return "sezione";
                case (int)swDrawingViewTypes_e.swDrawingDetailView: return "dettaglio";
                case (int)swDrawingViewTypes_e.swDrawingNamedView: return "vista con nome";
                case (int)swDrawingViewTypes_e.swDrawingAuxiliaryView: return "ausiliaria";
                case (int)swDrawingViewTypes_e.swDrawingAlternatePositionView: return "posizione alternativa";
                case (int)swDrawingViewTypes_e.swDrawingRelativeView: return "relativa";
                case (int)swDrawingViewTypes_e.swDrawingStandardView: return "standard";
                case (int)swDrawingViewTypes_e.swDrawingDetachedView: return "staccata";
                default: return "vista";
            }
        }
    }
}
