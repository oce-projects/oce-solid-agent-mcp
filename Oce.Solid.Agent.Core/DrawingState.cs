using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
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
    public partial class DocumentState
    {
        /// <summary>Quante viste si descrivono per esteso, al massimo.</summary>
        private const int MaxViews = 24;

        /// <summary>
        /// I documenti aperti adesso.
        ///
        /// Se `sw` e' null si torna almeno il documento che si sta leggendo:
        /// meglio una riga vera che un elenco assente, perche' l'assenza si
        /// legge come «non c'e' nient'altro» ed e' una bugia.
        /// </summary>
        private static List<Dictionary<string, object>> OpenDocuments(
            ISldWorks sw, IModelDoc2 active)
        {
            var outside = new List<Dictionary<string, object>>();
            string activePath = "", activeTitle = "";
            try { activePath = active?.GetPathName() ?? ""; } catch { }
            // IL TITOLO, NON SOLO IL PERCORSO. Un documento mai salvato
            // non ha percorso, e con il solo confronto sul percorso in un
            // elenco di documenti nuovi non risultava attivo NESSUNO.
            try { activeTitle = active?.GetTitle() ?? ""; } catch { }

            if (sw == null) return outside;
            try
            {
                var d = sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 200)
                {
                    string filePath = "", docTitle = "";
                    int typ = 0;
                    try { filePath = d.GetPathName() ?? ""; } catch { }
                    try { docTitle = d.GetTitle() ?? ""; } catch { }
                    try { typ = d.GetType(); } catch { }

                    var entry = new Dictionary<string, object>
                    {
                        ["document_title"] = docTitle,
                        ["kind"] = SwOperations.DocumentTypeName(typ),
                    };
                    // Il percorso solo se c'e': un documento mai salvato non ne
                    // ha, e una stringa vuota in un elenco di percorsi e' un
                    // invito a usarla.
                    if (!string.IsNullOrWhiteSpace(filePath)) entry["path"] = filePath;
                    bool isIt =
                        !string.IsNullOrWhiteSpace(activePath)
                            ? string.Equals(filePath, activePath,
                                  StringComparison.OrdinalIgnoreCase)
                            : (!string.IsNullOrWhiteSpace(activeTitle) &&
                               string.Equals(docTitle, activeTitle, StringComparison.Ordinal));
                    if (isIt) entry["active"] = true;

                    outside.Add(entry);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>Fogli, viste e conteggi di una tavola.</summary>
        private static void DescribeDrawing(IDrawingDoc drawing, Dictionary<string, object> data, ISldWorks sw)
        {
            if (drawing == null)
            {
                data["note"] = "unreadable drawing";
                return;
            }

            // ── i fogli ───────────────────────────────────────────────────
            string active = "";
            try { active = (drawing.GetCurrentSheet() as ISheet)?.GetName() ?? ""; } catch { }

            var sheets = new List<Dictionary<string, object>>();
            try
            {
                var names = drawing.GetSheetNames() as string[];
                if (names != null)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        var entry = new Dictionary<string, object>
                        {
                            ["uid"] = "S" + (i + 1),
                            ["name"] = names[i],
                        };
                        if (string.Equals(names[i], active, StringComparison.Ordinal))
                            entry["active"] = true;

                        // Le proprieta' del foglio si leggono solo da quello
                        // ATTIVO senza cambiare foglio, e cambiare foglio per
                        // leggere sarebbe una lettura che modifica: si legge
                        // quello che si puo' e si tace il resto.
                        if (entry.ContainsKey("active"))
                        {
                            var p = SheetPropertyOf(drawing);
                            if (p != null)
                            {
                                entry["paper_size_mm"] = new object[]
                                {
                                    Math.Round(Units.Mm(p[5]), 1),
                                    Math.Round(Units.Mm(p[6]), 1),
                                };
                                if (p[3] != 0)
                                    entry["scale"] = FormatScale(p[2], p[3]);
                                // IL DIEDRO SI VEDE. E' l'unica cosa di una
                                // tavola che puo' essere sbagliata senza che
                                // nessuna misura se ne accorga: destra e
                                // sinistra scambiate, tavola plausibile,
                                // pezzo specchiato. Se non e' scritto qui, il
                                // modello non ha modo di guardarlo.
                                if (p.Length > 4)
                                    entry["projection_angle"] = p[4] != 0 ? "first" : "third";
                            }
                        }
                        sheets.Add(entry);
                    }
                }
            }
            catch { }
            if (sheets.Count > 0) data["sheets"] = sheets;

            // ── le viste ──────────────────────────────────────────────────
            //
            // LA PRIMA VISTA NON E' UNA VISTA: e' il FOGLIO. `GetFirstView()`
            // torna il foglio, e le viste vere cominciano dal `GetNextView()`.
            // Chi lo dimentica descrive un foglio come se fosse una vista, e
            // poi quota un oggetto che non ha geometria.
            var views = new List<Dictionary<string, object>>();
            // I TOTALI SONO int? (voce Z-C8): una vista che non risponde fa
            // null il totale, non un totale piu' basso detto come vero. Solo le
            // viste si contano sempre — si stanno scorrendo.
            int totalViews = 0;
            int? quoteTotali = 0, totalCenterMarks = 0;
            int? gtolTot = 0, datumTot = 0, totalWelds = 0, rugTot = 0, noteTot = 0, annTot = 0;
            int orfaneTot = 0;
            // Dimensions attached to a silhouette: not counted as orphans,
            // because IsSame cannot check them (N3, see DimensionsWithoutEdge).
            int notVerifiableTotal = 0;
            int? totalBogusNumbers = 0;
            var bogusNumbers = new List<Dictionary<string, object>>();
            bool unknownOrphans = false;
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
                var viewSheet = drawing.GetFirstView() as IView;
                if (viewSheet != null)
                    data["sheet_annotations"] = Annotations(viewSheet);
            }
            catch { }

            try
            {
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    totalViews++;
                    int? quote = ReadValue(() => v.GetDimensionCount());
                    quoteTotali += quote;

                    // LE CROCI DI CENTRO SI CONTANO QUI, e non solo dentro
                    // l'operazione che le mette (voce W2.11). E' la regola di
                    // GA5: **lo stato e l'operazione devono raccontare la stessa
                    // cosa, leggendola per conto proprio.** Se un giorno
                    // divergessero, una delle due mentirebbe e non si saprebbe
                    // quale.
                    // ENUMERATE, non GetCenterMarkCount: quello non vede le croci
                    // messe da SOLIDWORKS alla creazione della vista (26/9/2026).
                    int? centerMarks = ReadValue(() => { int n = CountCenterMarks.OfView(v); if (n < 0) throw new InvalidOperationException(); return n; });
                    totalCenterMarks += centerMarks;

                    // IL NUMERO CHE SI STAMPA E' QUELLO DEL MODELLO? (voce Z-C2)
                    int? bogus = DimensionsByNumberNotFromTheModel(v, bogusNumbers);
                    totalBogusNumbers += bogus;

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
                    int? gtol = ReadValue(() => v.GetGTolCount());
                    int? datum = ReadValue(() => v.GetDatumTagCount());
                    int? weldments = ReadValue(() => v.GetWeldSymbolCount());
                    // `GetSFSymbolCount`, NON `GetSurfaceFinishSymbolCount`:
                    // il secondo e' il nome per simmetria con
                    // `InsertSurfaceFinishSymbol`, e non esiste.
                    int? roughness = ReadValue(() => v.GetSFSymbolCount());
                    int? note = ReadValue(() => v.GetNoteCount());
                    int? annotations = ReadValue(() => v.GetAnnotationCount());
                    gtolTot += gtol; datumTot += datum; totalWelds += weldments;
                    rugTot += roughness; noteTot += note; annTot += annotations;

                    // LE QUOTE RIMASTE SENZA SPIGOLO (voce Z-A2).
                    //
                    // `IAnnotation.IsDangling` e' MORTO per le quote in tavola
                    // fuori processo: misurato il 24/9/2026 con
                    // `Checks/Orphan`, resta falso su una quota il cui
                    // spigolo e' stato soppresso, mentre la quota continua a
                    // dire 20 mm. Quello che lo vede e' chiedersi se lo spigolo
                    // a cui e' attaccata e' ancora fra quelli che la vista
                    // MOSTRA (1 prima, 0 dopo). null vuol dire «non lo so»
                    // (voce Z-C8), non zero.
                    int notVerifiable;
                    int? orfane = DimensionsWithoutEdge(sw, v, out notVerifiable);
                    if (orfane.HasValue) orfaneTot += orfane.Value; else unknownOrphans = true;
                    notVerifiableTotal += notVerifiable;

                    if (views.Count < MaxViews)
                    {
                        var entry = new Dictionary<string, object>
                        {
                            ["uid"] = "V" + totalViews,
                        };
                        try { entry["name"] = v.Name; } catch { }
                        try { entry["kind"] = ViewTypeName(v.Type); } catch { }

                        var pos = ViewPosition(v);
                        if (pos != null) entry["position_mm"] = pos;

                        try
                        {
                            double s = v.ScaleDecimal;
                            if (s > 0) entry["scale"] = Math.Round(s, 4);
                        }
                        catch { }

                        // Zero quote non e' un dettaglio: e' la differenza fra
                        // una tavola e un disegnino. Si scrive sempre.
                        entry["dimensions"] = quote;
                        // E zero croci non e' un dettaglio nemmeno lui: e' la
                        // differenza fra una tavola che si puo' forare e una
                        // che si puo' solo guardare. Si scrive sempre, per lo
                        // stesso motivo — **un campo che compare solo quando ha
                        // un valore interessante insegna a non cercarlo.**
                        entry["center_marks"] = centerMarks;
                        entry["dimensions_with_number_not_from_model"] = bogus;
                        entry["dimensions_orphan"] = orfane;
                        // Unknown when the orphans are unknown: the same reading.
                        entry["dimensions_not_verifiable"] = orfane.HasValue ? (object)notVerifiable : null;

                        // SEMPRE, ANCHE TUTTE A ZERO, e per la stessa ragione
                        // delle due righe qui sopra: un campo che compare solo
                        // quando ha un valore interessante insegna a non
                        // cercarlo, e chi non lo trova conclude che l'API non
                        // sappia rispondere invece che il numero sia zero.
                        entry["annotations"] = Six(gtol, datum, weldments, roughness, note, annotations);

                        try
                        {
                            var refObj = v.ReferencedDocument as IModelDoc2;
                            var p = refObj?.GetPathName();
                            if (!string.IsNullOrWhiteSpace(p)) entry["model"] = p;
                        }
                        catch { }

                        views.Add(entry);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch
            {
                // Il giro si e' interrotto a meta': i totali sono di una parte
                // delle viste, e nessuno lo saprebbe. Diventano «non lo so».
                quoteTotali = totalCenterMarks = null;
                gtolTot = datumTot = totalWelds = rugTot = noteTot = annTot = null;
                data["views_half_read"] = true;
            }

            if (views.Count > 0) data["views"] = views;
            data["views_total"] = totalViews;
            data["dimensions_total"] = quoteTotali;
            data["center_marks_total"] = totalCenterMarks;
            data["dimensions_with_number_not_from_model_total"] = totalBogusNumbers;
            if (bogusNumbers.Count > 0)
            {
                data["dimensions_with_number_not_from_model"] = bogusNumbers;
                data["warning_numbers"] =
                    bogusNumbers.Count + (bogusNumbers.Count == 1 ? " quota stampa" : " quote stampano") +
                    " un numero che non e' quello del modello (sovrascritto, o il valore spento " +
                    "con del testo al suo posto). La geometria e' giusta, la carta no: in officina " +
                    "si lavora col numero stampato.";
            }
            data["annotations_total"] = Six(gtolTot, datumTot, totalWelds, rugTot, noteTot, annTot);
            data["dimensions_orphan_total"] = unknownOrphans && orfaneTot == 0 ? (object)null : orfaneTot;
            data["dimensions_not_verifiable_total"] =
                unknownOrphans && notVerifiableTotal == 0 ? (object)null : notVerifiableTotal;
            if (orfaneTot > 0)
                data["warning_dimensions"] = orfaneTot + " quote sono attaccate a spigoli che le viste non " +
                    "mostrano piu': sul foglio si stampano col numero di prima, appese al niente. Di " +
                    "solito il pezzo e' cambiato dopo che la tavola e' stata quotata.";

            // DUE CECITA' DICHIARATE, e tutte e due porterebbero a conclusioni
            // sbagliate se restassero implicite.
            data["what_is_not_known_about_annotations"] =
                // 1. IL CARTIGLIO NON E' QUI DENTRO. Questi conteggi girano
                //    sulle VISTE VERE; le note del formato del foglio stanno
                //    sulla prima «view», che e' il foglio, e sono decine — su
                //    questo modello di tavola, trentanove. Chi somma le viste e
                //    confronta col totale del documento trova una differenza
                //    grande e conclude che si sia perso qualcosa.
                "«annotations_total» somma le VISTE; le note del cartiglio " +
                "stanno sul FOGLIO e si contano a parte, in " +
                "«sheet_annotations» — su questo modello di tavola sono " +
                "sessantasette, e sommarle alle viste farebbe sembrare quotata " +
                "una tavola vuota. E un'annotazione su un layer non stampabile " +
                "esiste, viene contata, e nel PDF non c'e': il conteggio dice " +
                "cosa c'e' nel documento, non cosa esce dalla stampante. " +
                "«dimensions_orphan» checks whether the edges and vertices each dimension is " +
                "attached to are among those the view shows. A dimension attached to a " +
                "SILHOUETTE (the apparent outline of a cylinder: the diameters of a turned " +
                "part seen from the side) cannot be checked, because SOLIDWORKS does not " +
                "match it with any silhouette the view shows: it is not counted as an " +
                "orphan but in «dimensions_not_verifiable». A dimension attached to a hidden " +
                "edge still reads as an orphan without being one.";
            if (totalViews > views.Count) data["views_truncated"] = true;

            // LA TAVOLA VUOTA VA DETTA, non lasciata dedurre da un elenco che
            // manca. E' lo stato in cui nasce ogni tavola, ed e' quello in cui
            // resta se `Create1stAngleViews2` ha detto «si'» senza fare niente.
            if (totalViews == 0)
                data["note"] = "la tavola non ha ancora nessuna vista";
            else if (quoteTotali.HasValue && quoteTotali.Value == 0)
                data["note"] = "le viste ci sono ma non c'e' nessuna quota";
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
        private static int? DimensionsWithoutEdge(ISldWorks sw, IView v, out int notVerifiable)
        {
            notVerifiable = 0;
            if (sw == null) return null;
            try
            {
                var dims = v.GetDisplayDimensions() as object[];
                if (dims == null || dims.Length == 0) return 0;
                if (dims.Length > 60) return null;
                var visible = new List<object>();
                foreach (int typ in new[] { (int)swViewEntityType_e.swViewEntityType_Edge,
                                             (int)swViewEntityType_e.swViewEntityType_Vertex })
                {
                    var e = v.GetVisibleEntities2(null, typ) as object[];
                    if (e != null) visible.AddRange(e);
                }
                if (visible.Count > 800) return null;
                int orfane = 0;
                int onSilhouettes = 0;
                foreach (var o in dims)
                {
                    var ann = (o as IDisplayDimension)?.GetAnnotation() as IAnnotation;
                    var attached = ann?.GetAttachedEntities3() as object[];
                    if (attached == null || attached.Length == 0) continue;
                    bool seen = false;
                    foreach (var a in attached)
                    {
                        // A NULL ENTITY IS SKIPPED, AND THE COUNT DOES NOT CHANGE
                        // (Z-C21.3, finding C3-4: null seen, harmless; the filter is
                        // Z-C21.8). IAnnotation~GetAttachedEntities3.html: «swSelNOTHING
                        // | NULL (annotation is dangling or unsupported)». The Z-C21.3
                        // probe of C3-4 (28/9/2026), a dimension on the top
                        // edge of a block: with the block suppressed, and again with
                        // it deleted (IsDangling true), GetAttachedEntities3 gave one
                        // null of type 1, not swSelNOTHING. IsSame(null, edge)
                        // answered swObjectNotSame, and the orphan count was 1 with
                        // and without this filter, 0 with the block back. So the
                        // filter only saves one COM call per visible entity: the
                        // plain null passed here answered NotSame, while in the same
                        // probe a null wrapped in a DispatchWrapper threw
                        // RPC_E_SERVERFAULT, which through the catch below would have
                        // turned the whole view into «not known».
                        if (a == null) continue;
                        foreach (var vv in visible)
                            if (sw.IsSame(a, vv) == (int)swObjectEquality.swObjectSame) { seen = true; break; }
                        if (seen) break;
                    }
                    if (seen) continue;
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
        /// `all` si chiama cosi' e non «total»: `GetAnnotationCount` **non
        /// e' la somma** delle altre cinque — conta anche le quote e cio' che
        /// nessuna delle cinque famiglie nomina. Chiamarlo «total» farebbe
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
        private static int? DimensionsByNumberNotFromTheModel(IView v, List<Dictionary<string, object>> details)
        {
            int n = 0;
            try
            {
                var q = v.GetFirstDisplayDimension5() as IDisplayDimension;
                while (q != null)
                {
                    bool overwritten = q.GetOverride();
                    bool off = !q.ShowDimensionValue;
                    if (overwritten || off)
                    {
                        n++;
                        if (details.Count < 10)
                        {
                            var dim = q.GetDimension2(0);
                            var entry = new Dictionary<string, object>();
                            try { entry["dimension"] = dim.FullName; } catch { }
                            try { entry["view"] = v.Name; } catch { }
                            try { entry["of_model_mm"] = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000.0, 3); } catch { }
                            if (overwritten)
                                entry["how"] = "numero sovrascritto: stampa " +
                                    Math.Round(q.GetOverrideValue() * 1000.0, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm";
                            else
                            {
                                string text = "";
                                try { text = (q.GetText(1) ?? "") + (q.GetText(2) ?? ""); } catch { }
                                entry["how"] = "valore spento" + (text.Length > 0 ? ": al suo posto «" + text + "»" : "");
                            }
                            details.Add(entry);
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
        private static int? ReadValue(Func<int> read)
        {
            try { return read(); }
            catch { return null; }
        }

        private static Dictionary<string, object> Six(
            int? gtol, int? datum, int? weldments, int? roughness, int? note, int? all)
        {
            return new Dictionary<string, object>
            {
                ["gtol"] = gtol,
                ["datum"] = datum,
                ["welds"] = weldments,
                ["surface_finish"] = roughness,
                ["note"] = note,
                ["all"] = all,
            };
        }

        /// <summary>I sei numeri letti da una vista, ognuno per conto suo.</summary>
        private static Dictionary<string, object> Annotations(IView v)
        {
            return Six(
                ReadValue(() => v.GetGTolCount()), ReadValue(() => v.GetDatumTagCount()),
                ReadValue(() => v.GetWeldSymbolCount()), ReadValue(() => v.GetSFSymbolCount()),
                ReadValue(() => v.GetNoteCount()), ReadValue(() => v.GetAnnotationCount()));
        }

        private static double[] SheetPropertyOf(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetCurrentSheet() as ISheet;
                var p = sheet?.GetProperties2() as double[];
                return (p != null && p.Length >= 7) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>«1:2» invece di «0,5»: e' come si scrive su una tavola.</summary>
        private static string FormatScale(double numerator, double denominator)
        {
            try
            {
                return RoundOff(numerator) + ":" + RoundOff(denominator);
            }
            catch { return null; }
        }

        private static string RoundOff(double x)
        {
            double r = Math.Round(x, 4);
            return r == Math.Floor(r)
                ? ((long)r).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : r.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static object[] ViewPosition(IView v)
        {
            try
            {
                var p = v.Position as double[];
                if (p == null || p.Length < 2) return null;
                return new object[]
                {
                    Math.Round(Units.Mm(p[0]), 2),
                    Math.Round(Units.Mm(p[1]), 2),
                };
            }
            catch { return null; }
        }

        private static string ViewTypeName(int typ)
        {
            switch (typ)
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
