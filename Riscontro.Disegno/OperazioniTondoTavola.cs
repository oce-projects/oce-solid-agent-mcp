using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// IL PEZZO TONDO VISTO DI FIANCO (voce GT14): le lunghezze fra le facce e i
    /// diametri, nella vista longitudinale di una boccola, di un perno, di un
    /// albero a gradini.
    ///
    /// ── COSA SI E' MISURATO PRIMA (27/9/2026, `prova0089 tornito`..`tornito8`) ──
    ///
    /// Sull'albero a gradini dell'esempio di «rivoluzione» (Ø30x40, Ø20x30,
    /// Ø34x20), nella vista frontale:
    ///
    ///  * DUE CERCHI VISTI DI TAGLIO selezionati e `AddDimension2`: nasce la
    ///    distanza fra le due facce (20, 50, 40 — giuste). E' la lunghezza.
    ///  * DUE GENERATRICI DELLA STESSA FACCIA (`GetVisibleEntities2` col tipo
    ///    `swViewEntityType_SilhouetteEdge` = 4) e `AddDimension2`: nasce da sola
    ///    la quota di diametro, col prefisso «<MOD-DIAM>» (Ø34, Ø30).
    ///  * MA NON SEMPRE SI PUO': la faccia del Ø20 non compare fra le facce
    ///    visibili della vista, una sua generatrice ha `GetFace` null e l'altra
    ///    non si seleziona (in `GetPolylines7` l'oggetto e' null). Di quel
    ///    diametro si dice che non e' raggiunto, invece di tacerlo.
    ///  * STRADE PROVATE E CHIUSE: `ISilhouetteEdge.GetStartPoint` da fuori
    ///    processo fa sollevare un'eccezione a SOLIDWORKS (RPC_E_SERVERFAULT) e
    ///    `GetCurve` risponde null; la selezione per posizione (`SelectByID2`
    ///    «SILHOUETTE») prende una generatrice sola per volta; la faccia
    ///    cilindrica selezionata non fa nascere niente; la linea d'asse
    ///    (`InsertCenterLine2`) piu' una generatrice fa una quota misurata
    ///    dall'origine del FOGLIO (212,49), e `Diametric` non si lascia
    ///    accendere. Di taglio `AddDiameterDimension2` dice 60 per un Ø30.
    ///
    /// NEL CODICE non si usa `IModelDoc2.AddDimension2` delle sonde (vietata per
    /// contratto: niente direzione, voce GP6) e `Extension.AddDimension` con due
    /// entita' non fa nascere niente: si usano le quote ORIZZONTALE e VERTICALE
    /// dichiarate (`AddHorizontalDimension2` 20 sui due cerchi,
    /// `AddVerticalDimension2` Ø34 sulle due generatrici, col Ø messo da
    /// SOLIDWORKS — sonda `orvert`).
    ///
    /// Le lunghezze sono IN PARALLELO dalla faccia di un'estremita' — lo stesso
    /// criterio dello zero unico per tutte le quote (deciso il 26/9/2026 per i
    /// fori): la faccia piu' a sinistra, o piu' in basso se l'asse e' verticale.
    /// Ogni quota si rilegge contro la geometria prima di restare in tavola.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class EsitoTondo
        {
            public readonly List<double> Lunghezze = new List<double>();
            public readonly List<double> Diametri = new List<double>();
            public readonly List<string> NonRaggiunti = new List<string>();
            public readonly List<string> Note = new List<string>();
            public int GeneratriciSenzaFaccia;
            public int CoppieScartate;
            public string Asse;
        }

        /// <summary>
        /// Quota il tondo visto di fianco, se i cerchi di taglio della vista sono
        /// coassiali (almeno due). Null se la vista non e' quella di un tondo.
        /// </summary>
        private EsitoTondo QuotaIlTondoDiFianco(
            IModelDoc2 modello, IDrawingDoc disegno, IView vista, IMathUtility mu, MathTransform tr,
            List<Cerchio> diTaglio, HashSet<string> gia, double[] contorno, double distacco, double passo,
            ref int sotto, ref int diLato, List<Dictionary<string, object>> nate,
            List<string> altrove, List<string> falliti)
        {
            if (diTaglio.Count < 2 || diTaglio[0].AsseModello == null) return null;
            var asse = diTaglio[0].AsseModello;
            var o = diTaglio[0].CentroModello;
            // COASSIALI: stesso asse, centri sulla stessa retta.
            var suAsse = new List<Tuple<double, Cerchio>>();
            foreach (var c in diTaglio)
            {
                if (c.AsseModello == null || c.CentroModello == null) return null;
                double par = Math.Abs(Dot(c.AsseModello, asse));
                if (par < 0.9999) return null;
                var d = Sub(c.CentroModello, o);
                double t = Dot(d, asse);
                var fuori = Sub(d, Mul(asse, t));
                if (Norma(fuori) > 1e-6) return null;
                suAsse.Add(Tuple.Create(t, c));
            }
            var esito = new EsitoTondo();
            var asseFoglio = VettoreSulFoglio(asse, mu, tr);
            if (asseFoglio == null) return null;
            bool orizzontale = Math.Abs(asseFoglio[0]) >= Math.Abs(asseFoglio[1]);
            esito.Asse = orizzontale ? "orizzontale" : "verticale";
            // Il verso del parametro t sul foglio: da sinistra a destra (o dal
            // basso in alto), per partire dalla faccia giusta.
            double versoFoglio = orizzontale ? asseFoglio[0] : asseFoglio[1];
            suAsse.Sort((a, b) => (a.Item1 * Math.Sign(versoFoglio)).CompareTo(b.Item1 * Math.Sign(versoFoglio)));
            // Una posizione per faccia: due cerchi alla stessa t sono la stessa faccia.
            //
            // E SOLO I CERCHI DI UNA FACCIA PIANA DI TESTA O DI SPALLAMENTO
            // (revisione avversariale del 27/9/2026). Uno smusso fa due cerchi,
            // cono-piano e cono-cilindro, e un raccordo con gli spigoli tangenti
            // visibili fa lo stesso: contati come facce, su un Ø30x40 smussato 1x45°
            // nascevano 1, 39 e 40 — le «gambe» dello smusso che il resto della
            // tavola scarta come quote in piu' (EGambaDiSmusso). Un cerchio e' una
            // faccia del tondo se borda una faccia PIANA perpendicolare all'asse;
            // gli altri si dicono, non si quotano.
            var facce = new List<Tuple<double, Cerchio>>();
            var smussi = new List<string>();
            foreach (var p in suAsse)
            {
                string bordo = CosaBordaIlCerchio(p.Item2.Spigolo, asse);
                if (bordo != "piano")
                {
                    if (bordo == "cono" || bordo == "toro")
                    {
                        string detto = (bordo == "cono" ? "smusso" : "raccordo") + " sul Ø" + p.Item2.DiametroMm.ToString("0.##", Ci);
                        if (!smussi.Contains(detto)) smussi.Add(detto);
                    }
                    continue;
                }
                if (facce.Count == 0 || Math.Abs(p.Item1 - facce[facce.Count - 1].Item1) > 1e-6) facce.Add(p);
            }
            foreach (var sm in smussi)
                esito.Note.Add(sm + " (non e' una faccia del tondo: la sua misura va con la quota da smusso o " +
                                       "da raccordo, che questa vista non mette)");
            if (facce.Count < 2)
                esito.Note.Add("lunghezze (meno di due facce piane di testa o di spallamento viste di taglio)");

            var selMgr = modello.SelectionManager as ISelectionMgr;
            // ── LE LUNGHEZZE, IN PARALLELO DALLA PRIMA FACCIA ──────────────────
            var zero = facce.Count > 0 ? facce[0] : null;
            for (int i = 1; i < facce.Count; i++)
            {
                var f = facce[i];
                double attesaMm = Math.Round(Math.Abs(f.Item1 - zero.Item1) * 1000.0, 3);
                string chiave = ChiaveTondo(zero.Item2.CentroModello, f.Item2.CentroModello);
                string cosa = "lunghezza " + attesaMm.ToString("0.##", Ci);
                if (gia.Contains(chiave)) { altrove.Add(cosa + " (gia' quotata in questa tavola)"); continue; }
                double x, y;
                if (orizzontale)
                {
                    x = (zero.Item2.CentroFoglio[0] + f.Item2.CentroFoglio[0]) / 2;
                    y = contorno[1] - distacco - sotto * passo;
                }
                else
                {
                    x = contorno[2] + distacco + diLato * passo;
                    y = (zero.Item2.CentroFoglio[1] + f.Item2.CentroFoglio[1]) / 2;
                }
                bool bornWithoutObject;
                var q = QuotaFraDue(modello, disegno, vista, selMgr,
                                    e => (zero.Item2.Spigolo as IEntity).Select4(false, e) &&
                                         (f.Item2.Spigolo as IEntity).Select4(true, e), x, y, orizzontale,
                                    out bornWithoutObject);
                if (q == null && !bornWithoutObject) { falliti.Add(cosa + " (la quota non e' nata)"); continue; }
                // BORN WITHOUT ITS OBJECT (finding N80, the site Z-C21.8/9 left
                // out): the dimension is on the sheet and its value cannot be read
                // back. It keeps its place (the next length goes one step
                // further out), its row with the value unknown, and its key in
                // `gia`: another view of the sheet must not dimension it again.
                if (q != null && !RilettaGiusta(modello, q, attesaMm, false, cosa, falliti)) continue;
                if (orizzontale) sotto++; else diLato++;
                var descritta = DescriviQuota(q, f.Item2.Indice);
                descritta["orientamento"] = "lunghezza del tondo";
                descritta["posizione_mm"] = DovEFinita(q);
                if (q != null)
                {
                    nate.Add(descritta);
                    esito.Lunghezze.Add(attesaMm);
                }
                else RecordBornWithoutObject(nate, descritta, falliti,
                    "turned length " + attesaMm.ToString("0.##", Ci) + " (the dimension was born but SOLIDWORKS did not " +
                    "hand it back: its value was not read back, check it on the sheet)");
                gia.Add(chiave);
            }

            // I CILINDRI COASSIALI DEL PEZZO (diametro, e il punto a meta' lungo
            // l'asse): dicono quali diametri mancano, e riconoscono una quota nata
            // da due generatrici di cui SOLIDWORKS non dice la faccia.
            var cilindri = CilindriCoassiali(vista, asse, o);

            // ── I DIAMETRI, DALLE DUE GENERATRICI DI OGNI FACCIA ────────────────
            //
            // LE GENERATRICI SI RILEGGONO PRIMA DI OGNI QUOTA: sono oggetti della
            // vista, non del modello, e dopo una quota nuova quelle lette prima
            // possono non selezionarsi piu' (misurato il 27/9/2026, `orvert`: la
            // seconda coppia «presa True/False»). La quota e' ORIZZONTALE o
            // VERTICALE dichiarata (AddVerticalDimension2 per un asse orizzontale):
            // su due generatrici della stessa faccia SOLIDWORKS ci mette da se' il
            // Ø. `Extension.AddDimension` con due entita' non fa nascere niente.
            bool quotaDiametroOrizzontale = !orizzontale;
            var fatti = new HashSet<string>(StringComparer.Ordinal);
            for (int giro = 0; giro < 12; giro++)
            {
                var lette = LeggiLeGeneratrici(modello, vista);
                esito.GeneratriciSenzaFaccia = lette.SenzaFaccia.Count;
                string scelta = null;
                foreach (var kv in lette.Gruppi)
                    if (kv.Value.Count == 2 && !fatti.Contains(kv.Key)) { scelta = kv.Key; break; }
                if (scelta == null) break;
                fatti.Add(scelta);
                var m = lette.Meta[scelta];
                double attesaMm = Math.Round(m[0] * 2000.0, 3);
                string cosa = "diametro Ø" + attesaMm.ToString("0.##", Ci);
                string chiave = "foro|" + Math.Round(attesaMm, 2).ToString("0.00", Ci);
                // A META' DELLA FACCIA, sull'asse: la linea di misura attraversa il
                // suo cilindro, come si quota un albero. Lo stesso punto fa la
                // CHIAVE DEL CILINDRO (revisione del 27/9/2026): due perni Ø30
                // separati da un collare sono due quote, non una.
                double tm = (m[1] + m[2]) / 2;
                string delCilindro = ChiaveDelCilindro(m[0] * 2000.0, new[] { m[3] + tm * m[6], m[4] + tm * m[7], m[5] + tm * m[8] });
                if (gia.Contains(delCilindro) || gia.Contains("cil?|" + chiave.Substring(5)))
                { altrove.Add(cosa + " (gia' quotato in questa tavola)"); continue; }
                var mezzo = SulFoglio(new[] { m[3] + tm * m[6], m[4] + tm * m[7], m[5] + tm * m[8] }, mu, tr);
                if (mezzo == null) { falliti.Add(cosa + " (non so dove sta sul foglio)"); continue; }
                var a = lette.Gruppi[scelta][0]; var b = lette.Gruppi[scelta][1];
                bool bornWithoutObject;
                var q = QuotaFraDue(modello, disegno, vista, selMgr, e => a.Select2(false, e) && b.Select2(true, e),
                                    mezzo[0], mezzo[1], quotaDiametroOrizzontale, out bornWithoutObject);
                if (q == null && !bornWithoutObject) { falliti.Add(cosa + " (la quota non e' nata)"); continue; }
                // BORN WITHOUT ITS OBJECT (N80), as the lengths above: the row
                // with the value unknown, not in `tondo_diametri_mm` (which lists
                // the diameters read back with their Ø), and the keys of this
                // cylinder in `gia`, so that it is neither dimensioned again nor
                // reported as not reached.
                if (q != null && !RilettaGiusta(modello, q, attesaMm, true, cosa, falliti)) continue;
                var descritta = DescriviQuota(q, -1);
                descritta["orientamento"] = "diametro del tondo";
                descritta["posizione_mm"] = DovEFinita(q);
                if (q != null)
                {
                    nate.Add(descritta);
                    esito.Diametri.Add(attesaMm);
                }
                else RecordBornWithoutObject(nate, descritta, falliti,
                    "turned diameter Ø" + attesaMm.ToString("0.##", Ci) + " (the dimension was born but SOLIDWORKS did not " +
                    "hand it back: its value and its Ø symbol were not read back, check it on the sheet)");
                gia.Add(chiave);
                gia.Add(delCilindro);
            }

            // ── LE GENERATRICI SENZA FACCIA, PROVATE A COPPIE ───────────────────
            // Misurato il 27/9/2026: sulla boccola GetFace resta null anche dopo
            // tre giri, e quelle generatrici si selezionano lo stesso. Si prova
            // ogni coppia (per posizione nell'elenco, che si rilegge ogni volta)
            // e la quota resta SOLO se SOLIDWORKS stesso ci mette il Ø (lo fa
            // quando le due generatrici sono dello stesso cilindro) E il valore e'
            // il diametro di un cilindro coassiale del pezzo non ancora quotato.
            // Altrimenti si toglie. Poi il testo va a meta' del cilindro.
            // Le generatrici con la faccia ma rimaste sole entrano nella prova: la
            // compagna e' fra quelle senza.
            var provate = new HashSet<string>(StringComparer.Ordinal);
            double cx = (contorno[0] + contorno[2]) / 2, cy = (contorno[1] + contorno[3]) / 2;
            for (int giro = 0; giro < 30; giro++)
            {
                var lette = LeggiLeGeneratrici(modello, vista);
                var candidate = new List<int>(lette.SenzaFaccia);
                foreach (var kv in lette.Gruppi) if (kv.Value.Count == 1) candidate.Add(lette.Indici[kv.Key][0]);
                candidate.Sort();
                int ia = -1, ib = -1;
                for (int x = 0; x < candidate.Count && ia < 0; x++)
                    for (int y = x + 1; y < candidate.Count; y++)
                        if (provate.Add(candidate[x] + "|" + candidate[y])) { ia = candidate[x]; ib = candidate[y]; break; }
                if (ia < 0) break;
                var a = lette[ia]; var b = lette[ib];
                if (a == null || b == null) continue;
                bool bornWithoutObject;
                var q = QuotaFraDue(modello, disegno, vista, selMgr, e => a.Select2(false, e) && b.Select2(true, e),
                                    cx, cy, quotaDiametroOrizzontale, out bornWithoutObject);
                if (bornWithoutObject)
                {
                    // BORN WITHOUT ITS OBJECT (N80), and here it cannot be judged:
                    // without the object there is no value to match to a
                    // cylinder, no Ø to look for, and nothing to remove
                    // (TogliQuota selects the annotation of the object). Until
                    // library 0.145.0 this was «not born» and the next pair was
                    // tried: the dimension stayed on the sheet unreported, and a
                    // later pair of the same cylinder could dimension it a second
                    // time. Now it is said, and no further pair is tried.
                    RecordBornWithoutObject(nate, DescriviQuota(null, -1), falliti,
                        "a dimension between two silhouette edges without a face (it was born but SOLIDWORKS did not " +
                        "hand it back: it could not be read, matched to a cylinder or removed, and the remaining pairs " +
                        "were not tried, so as not to dimension the same cylinder twice; it may be one of the diameters " +
                        "reported as not reached. Check it on the sheet)");
                    break;
                }
                if (q == null) continue;
                double letto = double.NaN; string prefisso = "";
                try { letto = Math.Round(ApiHelpers.ReadDimensionInConfiguration(q.GetDimension2(0) as IDimension, "") * 1000.0, 3); } catch { }
                try { prefisso = q.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
                CilindroDelTondo suo = null;
                foreach (var c in cilindri)
                    if (!double.IsNaN(letto) && Math.Abs(c.DiametroMm - letto) <= 0.01 && !CilindroQuotato(gia, c)) { suo = c; break; }
                if (prefisso.Contains("<MOD-DIAM>") && suo == null && !double.IsNaN(letto) &&
                    cilindri.Any(c => Math.Abs(c.DiametroMm - letto) <= 0.01))
                {
                    // UN DIAMETRO VERO, ma gia' quotato altrove: si toglie e si dice.
                    TogliQuota(modello, q);
                    string detto = "diametro Ø" + letto.ToString("0.##", Ci) + " (gia' quotato in questa tavola)";
                    if (!altrove.Contains(detto)) altrove.Add(detto);
                    continue;
                }
                if (!prefisso.Contains("<MOD-DIAM>") || suo == null) { TogliQuota(modello, q); esito.CoppieScartate++; continue; }
                var mezzo = SulFoglio(suo.Meta, mu, tr);
                try { if (mezzo != null) (q.GetAnnotation() as IAnnotation)?.SetPosition2(mezzo[0], mezzo[1], 0); } catch { }
                var descritta = DescriviQuota(q, -1);
                descritta["orientamento"] = "diametro del tondo (generatrici senza faccia, riconosciuto dal valore)";
                descritta["posizione_mm"] = DovEFinita(q);
                nate.Add(descritta);
                esito.Diametri.Add(Math.Round(suo.DiametroMm, 3));
                gia.Add("foro|" + Math.Round(suo.DiametroMm, 2).ToString("0.00", Ci));
                gia.Add(suo.Chiave);
            }

            // ── E QUELLI CHE LA VISTA NON ESPONE ────────────────────────────────
            // I cilindri coassiali del pezzo il cui diametro, a questo punto, non
            // e' quotato in nessuna vista della tavola.
            var detti = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cilindri)
            {
                string nome = "Ø" + Math.Round(c.DiametroMm, 2).ToString("0.##", Ci);
                if (CilindroQuotato(gia, c) || !detti.Add(c.Chiave)) continue;
                esito.NonRaggiunti.Add(nome + " (non raggiunto da questa vista: generatrici nascoste, o che SOLIDWORKS " +
                                       "non da'. Se e' un foro, quotalo dalla vista in cui e' un cerchio)");
            }
            return esito;
        }

        /// <summary>Un cilindro del tondo: il diametro, il punto a meta' lungo l'asse (m, nel modello), la chiave.</summary>
        private sealed class CilindroDelTondo
        {
            public double DiametroMm;
            public double[] Meta;
            public string Chiave;
        }

        /// <summary>Quel cilindro e' gia' quotato: la sua chiave, o un Ø uguale di cui non si sa il cilindro.</summary>
        private static bool CilindroQuotato(HashSet<string> gia, CilindroDelTondo c)
        {
            return gia.Contains(c.Chiave) || gia.Contains("cil?|" + Math.Round(c.DiametroMm, 2).ToString("0.00", Ci));
        }

        /// <summary>
        /// I cilindri del pezzo coassiali col tondo, ciascuno col punto a meta' lungo
        /// l'asse e la sua chiave. Vuoto se la vista non e' di una parte.
        /// </summary>
        private static List<CilindroDelTondo> CilindriCoassiali(IView vista, double[] asse, double[] o)
        {
            var fuori = new List<CilindroDelTondo>();
            try
            {
                var parte = vista.ReferencedDocument as IPartDoc;
                if (parte == null) return fuori;
                foreach (var ob in (parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[]) ?? new object[0])
                    foreach (var fo in ((ob as IBody2)?.GetFaces() as object[]) ?? new object[0])
                    {
                        var faccia = fo as IFace2;
                        var su = faccia?.GetSurface() as ISurface;
                        if (su == null || !su.IsCylinder()) continue;
                        var cp = su.CylinderParams as double[];
                        if (Math.Abs(Dot(new[] { cp[3], cp[4], cp[5] }, asse)) < 0.9999) continue;
                        var d = Sub(new[] { cp[0], cp[1], cp[2] }, o);
                        if (Norma(Sub(d, Mul(asse, Dot(d, asse)))) > 1e-6) continue;
                        double tmin, tmax;
                        if (!EstremiDellaFaccia(faccia, cp, out tmin, out tmax)) continue;
                        double tm = (tmin + tmax) / 2;
                        var meta = new[] { cp[0] + tm * cp[3], cp[1] + tm * cp[4], cp[2] + tm * cp[5] };
                        fuori.Add(new CilindroDelTondo { DiametroMm = Math.Round(cp[6] * 2000.0, 3), Meta = meta, Chiave = ChiaveDelCilindro(cp[6] * 2000.0, meta) });
                    }
            }
            catch { }
            return fuori;
        }

        /// <summary>Toglie una quota appena nata. DeleteSelection2, non EditDelete (puo' aprire una finestra).</summary>
        private static bool TogliQuota(IModelDoc2 modello, IDisplayDimension q)
        {
            bool tolta = false;
            try
            {
                modello.ClearSelection2(true);
                var an = q.GetAnnotation() as IAnnotation;
                if (an != null && an.Select3(false, null))
                    tolta = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                modello.ClearSelection2(true);
            }
            catch { }
            return tolta;
        }

        private sealed class Generatrici
        {
            public object[] Tutte = new object[0];
            public readonly Dictionary<string, List<ISilhouetteEdge>> Gruppi = new Dictionary<string, List<ISilhouetteEdge>>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<int>> Indici = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            public readonly Dictionary<string, double[]> Meta = new Dictionary<string, double[]>(StringComparer.Ordinal);
            public readonly List<int> SenzaFaccia = new List<int>();
            public ISilhouetteEdge this[int i] { get { return Tutte[i] as ISilhouetteEdge; } }
        }

        /// <summary>
        /// Le generatrici della vista, raggruppate per faccia (raggio ed estremi
        /// lungo l'asse). PRIMA SI RIDISEGNA E SI CHIEDE LA FACCIA PIU' VOLTE:
        /// misurato il 27/9/2026 (`prova0089 gendiag`), `GetFace` risponde null
        /// su tutte finche' la tavola non e' ridisegnata e la domanda ripetuta.
        /// </summary>
        private static Generatrici LeggiLeGeneratrici(IModelDoc2 modello, IView vista)
        {
            var g = new Generatrici();
            // OBSOLETE-MEASURED: stepped_shaft_dimensioned_from_the_side — IModelDoc2~GraphicsRedraw2.html:
            // "Obsolete. Superseded by IModelView::GraphicsRedraw". The redraw is what makes
            // ISilhouetteEdge.GetFace answer (null on every edge until then: probe prova0089
            // gendiag, 27/9/2026). The replacement was not measured here: remeasure in Z-C21.7 (N20).
            try { modello.GraphicsRedraw2(); } catch { }
            try { g.Tutte = (vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge) as object[]) ?? new object[0]; } catch { }
            var facce = new IFace2[g.Tutte.Length];
            for (int giro = 0; giro < 3; giro++)
            {
                bool manca = false;
                for (int i = 0; i < g.Tutte.Length; i++)
                {
                    if (facce[i] != null) continue;
                    try { facce[i] = (g.Tutte[i] as ISilhouetteEdge)?.GetFace() as IFace2; } catch { }
                    if (facce[i] == null) manca = true;
                }
                if (!manca) break;
                // OBSOLETE-MEASURED: stepped_shaft_dimensioned_from_the_side — same measure as above.
                try { modello.GraphicsRedraw2(); } catch { }
            }
            for (int i = 0; i < g.Tutte.Length; i++)
            {
                var se = g.Tutte[i] as ISilhouetteEdge;
                if (se == null) continue;
                var su = facce[i]?.GetSurface() as ISurface;
                double tmin, tmax;
                double[] cp = su != null && su.IsCylinder() ? su.CylinderParams as double[] : null;
                if (cp == null || !EstremiDellaFaccia(facce[i], cp, out tmin, out tmax)) { g.SenzaFaccia.Add(i); continue; }
                string k = Math.Round(cp[6] * 1000, 3).ToString("0.###", Ci) + "|" +
                           Math.Round(tmin * 1000, 2).ToString("0.##", Ci) + "|" + Math.Round(tmax * 1000, 2).ToString("0.##", Ci);
                if (!g.Gruppi.ContainsKey(k))
                {
                    g.Gruppi[k] = new List<ISilhouetteEdge>();
                    g.Indici[k] = new List<int>();
                    g.Meta[k] = new[] { cp[6], tmin, tmax, cp[0], cp[1], cp[2], cp[3], cp[4], cp[5] };
                }
                g.Gruppi[k].Add(se);
                g.Indici[k].Add(i);
            }
            return g;
        }

        /// <summary>
        /// Seleziona (con la vista nel SelectData) e fa nascere una quota ORIZZONTALE
        /// o VERTICALE, dichiarata; null se non nasce.
        ///
        /// «Not born» is the count of dimensions, asked FIRST (finding N80 of
        /// Z-C21.8, at the one site that lot left out). When the count went up
        /// and the call returned null, the dimension is on the sheet without its
        /// object: the answer is null with <paramref name="bornWithoutObject"/>
        /// true, and each caller describes it instead of taking it for «not
        /// born». Until library 0.145.0 this read «q == null || not grown» and
        /// answered null for both, so the three callers reported as missing a
        /// dimension that was on the sheet and left its key out of `gia`: another
        /// view, or another pair of silhouette edges, could then dimension the
        /// same thing a second time.
        /// </summary>
        private IDisplayDimension QuotaFraDue(IModelDoc2 modello, IDrawingDoc disegno, IView vista, ISelectionMgr selMgr,
                                              Func<SelectData, bool> seleziona, double x, double y, bool orizzontale,
                                              out bool bornWithoutObject)
        {
            bornWithoutObject = false;
            int prima = ContaQuote(disegno);
            IDisplayDimension q = null;
            try
            {
                modello.ClearSelection2(true);
                var sd = selMgr?.CreateSelectData() as SelectData;
                if (sd == null) return null;
                sd.View = (View)vista;
                // ORIZZONTALE O VERTICALE DICHIARATA: IModelDoc2.AddDimension2 e'
                // vietata (niente direzione, voce GP6) e Extension.AddDimension con
                // due entita' selezionate non fa nascere niente (misurato).
                if (seleziona(sd))
                    q = (orizzontale ? modello.AddHorizontalDimension2(x, y, 0)
                                     : modello.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
            }
            catch { }
            try { modello.ClearSelection2(true); } catch { }
            if (NonCresciuta(disegno, prima, q)) return null;
            bornWithoutObject = q == null;
            return q;
        }

        /// <summary>
        /// La quota riletta vale quanto la geometria (e, se e' un diametro, porta
        /// il simbolo)? Se no si toglie: un numero sbagliato e plausibile non
        /// resta in tavola.
        /// </summary>
        private static bool RilettaGiusta(IModelDoc2 modello, IDisplayDimension q, double attesaMm, bool diametro,
                                          string cosa, List<string> falliti)
        {
            double letto = double.NaN;
            string prefisso = "";
            try { letto = ApiHelpers.ReadDimensionInConfiguration(q.GetDimension2(0) as IDimension, "") * 1000.0; } catch { }
            try { prefisso = q.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
            bool giusta = !double.IsNaN(letto) && Math.Abs(letto - attesaMm) <= 0.01 &&
                          (!diametro || prefisso.Contains("<MOD-DIAM>"));
            if (giusta) return true;
            bool tolta = TogliQuota(modello, q);
            falliti.Add(cosa + " (SOLIDWORKS ha scritto " +
                        (double.IsNaN(letto) ? "?" : Math.Round(letto, 3).ToString("0.###", Ci)) +
                        (diametro && !prefisso.Contains("<MOD-DIAM>") ? " senza Ø" : "") +
                        (tolta ? ": l'ho tolta)" : ": toglila)"));
            return false;
        }

        /// <summary>Gli estremi della faccia cilindrica lungo il suo asse, dai suoi cerchi.</summary>
        private static bool EstremiDellaFaccia(IFace2 faccia, double[] cp, out double tmin, out double tmax)
        {
            tmin = double.MaxValue; tmax = double.MinValue;
            try
            {
                foreach (var oe in (faccia.GetEdges() as object[]) ?? new object[0])
                {
                    var c = (oe as IEdge)?.GetCurve() as ICurve;
                    if (c == null || !c.IsCircle()) continue;
                    var cc = c.CircleParams as double[];
                    double t = (cc[0] - cp[0]) * cp[3] + (cc[1] - cp[1]) * cp[4] + (cc[2] - cp[2]) * cp[5];
                    tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
                }
            }
            catch { return false; }
            return tmax > tmin;
        }

        /// <summary>
        /// La chiave di UN cilindro del pezzo: il diametro e il punto a meta' lungo
        /// l'asse, nel modello («cil|30.00|x,y,z», millimetri al decimo). Due
        /// gradini dello stesso Ø, o un foro dello stesso Ø di un gradino, sono
        /// cilindri diversi e hanno chiavi diverse: con la chiave del solo
        /// diametro uno cancellava l'altro (revisione avversariale del 27/9/2026).
        /// Il punto a meta' non dipende dall'origine che la superficie sceglie
        /// sull'asse, quindi le due meta' di una faccia spezzata danno la stessa.
        /// </summary>
        private static string ChiaveDelCilindro(double diametroMm, double[] meta)
        {
            return "cil|" + Math.Round(diametroMm, 2).ToString("0.00", Ci) + "|" +
                   string.Join(",", meta.Take(3).Select(v => (Math.Round(v * 1000, 1) + 0.0).ToString("0.0", Ci)));
        }

        /// <summary>La chiave del cilindro di una faccia, o null se la faccia non e' un cilindro che si legge.</summary>
        private static string ChiaveDelCilindro(IFace2 faccia)
        {
            try
            {
                var su = faccia?.GetSurface() as ISurface;
                if (su == null || !su.IsCylinder()) return null;
                var cp = su.CylinderParams as double[];
                double tmin, tmax;
                if (cp == null || cp.Length < 7 || !EstremiDellaFaccia(faccia, cp, out tmin, out tmax)) return null;
                double tm = (tmin + tmax) / 2;
                return ChiaveDelCilindro(cp[6] * 2000.0, new[] { cp[0] + tm * cp[3], cp[1] + tm * cp[4], cp[2] + tm * cp[5] });
            }
            catch { return null; }
        }

        /// <summary>
        /// La chiave del cilindro di cui il cerchio e' un bordo (stesso raggio), o
        /// null. Per co-spigoli e anelli, come PassanteDallaGeometria: e' la strada
        /// che da fuori processo risponde.
        /// </summary>
        private static string ChiaveDelCilindroDelCerchio(IEdge e)
        {
            try
            {
                var cb = (e?.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 7) return null;
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var f = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var su = f?.GetSurface() as ISurface;
                    if (su == null || !su.IsCylinder()) continue;
                    var cp = su.CylinderParams as double[];
                    if (cp == null || cp.Length < 7 || Math.Abs(cp[6] - cb[6]) > 1e-8) continue;
                    return ChiaveDelCilindro(f);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Che faccia borda un cerchio del tondo: «piano» se una delle sue facce e' un
        /// piano perpendicolare all'asse (una testa, uno spallamento), altrimenti
        /// «cono» (uno smusso), «toro» (un raccordo) o «altro».
        /// </summary>
        private static string CosaBordaIlCerchio(IEdge spigolo, double[] asse)
        {
            string trovato = "altro";
            try
            {
                foreach (var oc in (spigolo?.GetCoEdges() as object[]) ?? new object[0])
                {
                    var su = (((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2)?.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (su.IsPlane())
                    {
                        var pp = su.PlaneParams as double[];
                        if (pp != null && pp.Length >= 3 && Math.Abs(Dot(new[] { pp[0], pp[1], pp[2] }, asse)) > 0.9999) return "piano";
                    }
                    else if (su.IsCone()) trovato = "cono";
                    else if (su.IsTorus() && trovato != "cono") trovato = "toro";
                }
            }
            catch { }
            return trovato;
        }

        /// <summary>La chiave di una lunghezza del tondo: i centri delle due facce, nel modello.</summary>
        private static string ChiaveTondo(double[] a, double[] b)
        {
            Func<double[], string> s = p => string.Join(",", p.Take(3).Select(v => Math.Round(v * 1000, 2).ToString("0.##", Ci)));
            var x = s(a); var y = s(b);
            return "tondo|" + (string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x);
        }

        /// <summary>Due cerchi (CircleParams) sullo stesso asse, con centri diversi?</summary>
        private static bool Coassiali(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 6 || b.Length < 6) return false;
            var asse = new[] { a[3], a[4], a[5] };
            if (Math.Abs(Dot(asse, new[] { b[3], b[4], b[5] })) < 0.9999) return false;
            var d = new[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
            double t = Dot(d, asse);
            return Math.Abs(t) > 1e-7 && Norma(Sub(d, Mul(asse, t))) < 1e-6;
        }

        private static double[] VettoreSulFoglio(double[] v, IMathUtility mu, MathTransform tr)
        {
            try { return ((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(tr)).ArrayData as double[]; }
            catch { return null; }
        }

        private static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        private static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        private static double[] Mul(double[] a, double k) { return new[] { a[0] * k, a[1] * k, a[2] * k }; }
        private static double Norma(double[] a) { return Math.Sqrt(Dot(a, a)); }
    }
}
