using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA TABELLA FORI IN TAVOLA (voce GT5).
    ///
    /// ── MISURATA PRIMA DI SCRIVERLA, 26/9/2026 (`prova0089 tabellafori`) ────
    ///
    /// `IView.InsertHoleTable2(UseAnchorPoint, X, Y, AnchorType, StartValue,
    /// TableTemplate)` non ha argomenti per l'origine ne' per la faccia: vanno
    /// SELEZIONATE prima, nella vista (`ISelectData.View`). Piastra 120 x 80 con
    /// quattro fori Ø8 a 15 dai bordi, vista dall'alto:
    ///
    ///   vertice d'angolo col marchio 1, faccia forata col marchio 2
    ///       -> 5 righe: A1 (15,15), A2 (15,65), A3 (105,15), A4 (105,65),
    ///          tutti «Ø8 PASSANTE» — le posizioni dall'angolo, giuste
    ///   marchi 2/1, oppure 0/0
    ///       -> la tabella NASCE VUOTA (solo l'intestazione), senza errore
    ///
    /// Per questo l'operazione pretende righe = fori + 1, e una tabella nata
    /// vuota la toglie e rifiuta. E rilegge ogni riga contro il modello: la
    /// posizione di ogni foro dall'origine, misurata sulla vista, deve stare
    /// nella tabella.
    ///
    /// Il 17/9 nessuna tabella nasceva su questa installazione (GT2): il 26/9
    /// nascono tutte tranne la distinta di taglio. Vedi la voce GT2 del TODO.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class ForoInVista
        {
            public double X, Y;          // centro sul foglio, metri
            public double DiametroMm;
        }

        private Esito TabellaForiInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito("Il documento attivo non e' una tavola: la tabella fori sta su una vista. " +
                               "Crea prima la tavola con «crea_disegno».");
            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito("La tavola non ha viste: la tabella fori si aggancia a una vista del pezzo. " +
                               "Metti prima le viste con «viste_standard».");

            string origine = Str(a, "origine", "basso_sinistra");
            int sx, sy;
            switch (origine)
            {
                case "basso_sinistra": sx = -1; sy = -1; break;
                case "basso_destra": sx = 1; sy = -1; break;
                case "alto_sinistra": sx = -1; sy = 1; break;
                case "alto_destra": sx = 1; sy = 1; break;
                default:
                    return Fallito("«origine» e' uno fra basso_sinistra, basso_destra, alto_sinistra, " +
                                   "alto_destra, non «" + origine + "».");
            }

            string motivo;
            IView vista = RisolviVista(disegno, nomi, Str(a, "vista", null), out motivo);
            if (motivo != null) return Fallito(motivo);
            if (vista == null) return Fallito("Non riesco a prendere la vista.");
            string nomeVista = "";
            try { nomeVista = vista.GetName2() ?? ""; } catch { }

            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = vista.ModelToViewTransform; } catch { }
            if (mu == null || tr == null)
                return Fallito("Non riesco a leggere come la vista porta il modello sul foglio.");
            double scala = 1;
            try { scala = vista.ScaleDecimal; } catch { }
            if (scala <= 0) scala = 1;

            // ── LA FACCIA FORATA CHE GUARDA CHI LEGGE ───────────────────────
            //
            // Fra le facce piane del pezzo dietro la vista, quelle la cui normale,
            // portata sul foglio, esce verso chi guarda (z > 0,99); di queste, quella
            // con piu' anelli interni fatti di cerchi — i fori.
            var pezzo = vista.ReferencedDocument as IModelDoc2;
            var parte = pezzo as IPartDoc;
            if (parte == null)
                return Fallito("Dietro la vista «" + nomeVista + "» non c'e' una parte: la tabella fori si fa " +
                               "sui fori di un pezzo.");
            Func<double[], double[]> sulFoglio = p =>
                ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[];
            Func<double[], double[]> direzioneSulFoglio = v =>
                ((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(tr)).ArrayData as double[];

            IFace2 faccia = null;
            var fori = new List<ForoInVista>();
            try
            {
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                foreach (var oc in corpi ?? new object[0])
                {
                    foreach (var of in ((oc as IBody2)?.GetFaces() as object[]) ?? new object[0])
                    {
                        var f = of as IFace2;
                        var n = ApiHelpers.NormalIfPlanar(f);
                        if (n == null) continue;
                        var nv = direzioneSulFoglio(n);
                        double lung = Math.Sqrt(nv[0] * nv[0] + nv[1] * nv[1] + nv[2] * nv[2]);
                        if (lung < 1e-12 || nv[2] / lung < 0.99) continue;
                        var qui = ForiDellaFaccia(f, sulFoglio);
                        if (qui.Count > fori.Count) { fori = qui; faccia = f; }
                    }
                }
            }
            catch (Exception ex) { return Fallito("Non riesco a leggere le facce del pezzo: " + ex.Message); }
            if (faccia == null || fori.Count == 0)
                return Fallito("Nella vista «" + nomeVista + "» nessuna faccia con dei fori guarda verso chi legge: " +
                               "la tabella fori si fa sulla vista in cui i fori si vedono come cerchi (di solito " +
                               "quella dall'alto, V2).");

            // ── L'ORIGINE: il vertice del contorno esterno piu' nell'angolo chiesto ──
            IVertex vertice = null;
            double vx = 0, vy = 0, meglio = double.MinValue;
            try
            {
                foreach (var oe in (faccia.GetEdges() as object[]) ?? new object[0])
                {
                    var e = oe as IEdge;
                    foreach (var ov in new[] { e?.GetStartVertex(), e?.GetEndVertex() })
                    {
                        var v = ov as IVertex;
                        var p = v?.GetPoint() as double[];
                        if (p == null) continue;
                        var s = sulFoglio(p);
                        double punteggio = s[0] * sx + s[1] * sy;
                        if (punteggio > meglio + 1e-9) { meglio = punteggio; vertice = v; vx = s[0]; vy = s[1]; }
                    }
                }
            }
            catch { }
            if (vertice == null) return Fallito("Non trovo un vertice d'angolo sulla faccia forata.");

            // ── DOVE VA LA TABELLA: in alto a sinistra del foglio, se non si dice ──
            double larghezzaFoglio = 0.420, altezzaFoglio = 0.297;
            try
            {
                var foglio = disegno.GetCurrentSheet() as ISheet;
                var pr = foglio?.GetProperties2() as double[];
                if (pr != null && pr.Length >= 7 && pr[5] > 0 && pr[6] > 0) { larghezzaFoglio = pr[5]; altezzaFoglio = pr[6]; }
            }
            catch { }
            double x = Unita.M(Num(a, "x_mm", 15)), y = Unita.M(Num(a, "y_mm", Unita.Mm(altezzaFoglio) - 15));
            string primoTag = Str(a, "inizio_tag", "A");

            int richiamiPrima = RichiamiDiForoNellaVista(vista);
            int tabellePrima = TabelleDellaTavola(disegno);
            modello.ClearSelection2(true);
            var sel = modello.SelectionManager as ISelectionMgr;
            var sdV = sel?.CreateSelectData() as SelectData;
            var sdF = sel?.CreateSelectData() as SelectData;
            if (sdV == null || sdF == null) return Fallito("Non riesco a preparare la selezione nella vista.");
            sdV.Mark = SelectionMarks.HoleTableDatumOrigin; sdV.View = (View)vista;
            sdF.Mark = SelectionMarks.HoleTableHoles; sdF.View = (View)vista;
            bool okV = (vertice as IEntity).Select4(false, sdV);
            bool okF = (faccia as IEntity).Select4(true, sdF);
            if (!okV || !okF)
                return Fallito("Non riesco a selezionare nella vista l'origine e la faccia forata (vertice " + okV +
                               ", faccia " + okF + ").");

            string modelloTabella = PrimoModelloDiTabella((int)swUserPreferenceStringValue_e.swFileLocationsHoleTableTemplates, ".sldholtbt");
            object grezza;
            try
            {
                grezza = vista.InsertHoleTable2(false, x, y,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft, primoTag, modelloTabella);
            }
            catch (Exception ex) { return Fallito("La tabella fori non e' nata: " + ex.Message); }
            modello.ClearSelection2(true);
            var tabella = grezza as ITableAnnotation;
            if (tabella == null)
                return Fallito("SOLIDWORKS non ha creato la tabella fori e non ha detto perche'.");

            int righe = -1;
            try { righe = tabella.RowCount; } catch { }
            // NATA VUOTA: e' cio' che succede coi marchi sbagliati, misurato. Una
            // tabella di sola intestazione si stampa e sembra che il pezzo non abbia fori.
            if (righe != fori.Count + 1)
            {
                string tolta = TogliLaTabella(modello, disegno, tabella, tabellePrima);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "La tabella fori e' nata con {0} righe e i fori della faccia sono {1}: ne servivano {2} " +
                    "(l'intestazione piu' un foro per riga).{3}", righe, fori.Count, fori.Count + 1, tolta));
            }

            // ── LA RILETTURA: ogni riga contro il modello ───────────────────
            var voci = new List<object>();
            int combaciano = 0;
            for (int r = 1; r < righe; r++)
            {
                string tag = PulisciCella(CellaTabella(tabella, r, 0));
                string sx0 = PulisciCella(CellaTabella(tabella, r, 1));
                string sy0 = PulisciCella(CellaTabella(tabella, r, 2));
                string misura = PulisciCella(CellaTabella(tabella, r, 3));
                double lx, ly;
                bool numeri = double.TryParse(sx0.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out lx) &
                              double.TryParse(sy0.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out ly);
                bool trovato = false;
                if (numeri)
                    foreach (var f in fori)
                    {
                        double dx = Math.Abs(f.X - vx) / scala * 1000.0, dy = Math.Abs(f.Y - vy) / scala * 1000.0;
                        if (Math.Abs(dx - Math.Abs(lx)) <= 0.05 && Math.Abs(dy - Math.Abs(ly)) <= 0.05) { trovato = true; break; }
                    }
                if (trovato) combaciano++;
                voci.Add(new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["x_mm"] = numeri ? (object)lx : sx0,
                    ["y_mm"] = numeri ? (object)ly : sy0,
                    ["misura"] = misura,
                    ["nel_modello"] = trovato,
                });
            }
            if (combaciano != fori.Count)
            {
                string tolta = TogliLaTabella(modello, disegno, tabella, tabellePrima);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "La tabella fori dice {0} posizioni e solo {1} corrispondono a un foro del pezzo misurato " +
                    "dall'origine sulla vista: non e' la tabella di questi fori.{2}", fori.Count, combaciano, tolta));
            }

            var misure = new Dictionary<string, object>
            {
                ["vista"] = nomeVista,
                ["origine"] = origine,
                ["fori"] = fori.Count,
                ["righe"] = righe,
                ["voci"] = voci.ToArray(),
                ["posizioni_verificate"] = true,
                ["tabelle_nella_tavola"] = TabelleDellaTavola(disegno),
            };
            if (!string.IsNullOrEmpty(modelloTabella)) misure["modello_di_tabella"] = modelloTabella;
            // I FORI DUE VOLTE (26/9/2026, la prima tavola guardata con la tabella):
            // se la vista aveva gia' i richiami dei fori, la tabella li ripete.
            // «Ciascun elemento si quota una volta sola»: lo si dice. Nell'ordine
            // giusto — tabella prima, poi quota_in_tavola — non succede, perche'
            // quota_in_tavola lascia i fori alla tabella.
            if (richiamiPrima > 0)
                misure["attenzione"] = "Nella vista c'erano gia' " + richiamiPrima + " richiami di foro (e di solito le " +
                                       "quote di posizione dei fori): con la tabella i fori sono quotati due volte. " +
                                       "Metti la tabella PRIMA di «quota_in_tavola», che allora lascia i fori alla tabella.";
            return SenzaFeature(misure);
        }

        /// <summary>
        /// I fori di una faccia: gli anelli interni fatti di un cerchio intero,
        /// col centro portato sul foglio.
        /// </summary>
        private static List<ForoInVista> ForiDellaFaccia(IFace2 f, Func<double[], double[]> sulFoglio)
        {
            var fuori = new List<ForoInVista>();
            try
            {
                foreach (var ol in (f.GetLoops() as object[]) ?? new object[0])
                {
                    var anello = ol as ILoop2;
                    if (anello == null || anello.IsOuter()) continue;
                    var spigoli = anello.GetEdges() as object[];
                    if (spigoli == null || spigoli.Length == 0) continue;
                    var curva = (spigoli[0] as IEdge)?.GetCurve() as ICurve;
                    if (curva == null || !curva.IsCircle()) continue;
                    var cp = curva.CircleParams as double[];   // centro x,y,z, asse, raggio
                    if (cp == null || cp.Length < 7) continue;
                    var c = sulFoglio(new[] { cp[0], cp[1], cp[2] });
                    fuori.Add(new ForoInVista { X = c[0], Y = c[1], DiametroMm = cp[6] * 2000.0 });
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>
        /// Quanti fori elencano le tabelle fori del foglio corrente (righe meno
        /// l'intestazione); 0 se non ce ne sono, -1 se non si legge.
        ///
        /// SI GUARDA TUTTO IL FOGLIO, NON LA VISTA. Misurato il 26/9/2026: la
        /// tabella fori messa sulla vista dall'alto appartiene al FOGLIO (la
        /// «vista» zero di `GetFirstView`), non alla vista — cercandola nella
        /// vista, `quota_in_tavola` non la vedeva e quotava i fori una seconda
        /// volta. Una tavola e' di un pezzo: una tabella fori sul foglio copre i
        /// fori delle sue viste.
        /// </summary>
        private static int ForiNellaTabellaFori(IDrawingDoc disegno)
        {
            try
            {
                int n = 0;
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var t = o as ITableAnnotation;
                        if (t != null && t.Type == (int)swTableAnnotationType_e.swTableAnnotation_HoleChart)
                            n += Math.Max(0, t.RowCount - 1);
                    }
                    v = v.GetNextView() as IView;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>Quanti richiami di foro ha gia' una vista; -1 se non si legge.</summary>
        private static int RichiamiDiForoNellaVista(IView vista)
        {
            try
            {
                int n = 0;
                var d = vista.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null)
                {
                    if (d.IsHoleCallout()) n++;
                    d = d.GetNext5() as IDisplayDimension;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>Quante tabelle ha la tavola, contate vista per vista; -1 se non si legge.</summary>
        private static int TabelleDellaTavola(IDrawingDoc disegno)
        {
            try
            {
                int n = 0;
                var v = disegno.GetFirstView() as IView;
                while (v != null) { n += v.GetTableAnnotationCount(); v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Toglie una tabella nata sbagliata e dice come e' andata: la coda della
        /// frase di rifiuto. Si conta prima e dopo, perche' «ho cancellato» detto
        /// senza rileggere non e' una misura.
        /// </summary>
        private static string TogliLaTabella(IModelDoc2 modello, IDrawingDoc disegno, ITableAnnotation tabella, int prima)
        {
            try
            {
                var ann = tabella.GetAnnotation() as IAnnotation;
                modello.ClearSelection2(true);
                // DeleteSelection2 E NON EditDelete: EditDelete chiede conferma con
                // una finestra, e la chiamata COM non torna (vedi OperazioniSw,
                // SenzaLoSchizzo). Era l'unica rimasta, trovata dal workflow W-S il
                // 27/9/2026; adesso il divieto e' in Prove/ProveLeFirmeVietate.
                if (ann != null && ann.Select3(false, null))
                    modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                modello.ClearSelection2(true);
            }
            catch { }
            int dopo = TabelleDellaTavola(disegno);
            if (prima >= 0 && dopo == prima) return " L'ho tolta: la tavola e' com'era.";
            return " Non sono riuscito a toglierla (tabelle " + prima + " -> " + dopo + "): toglila a mano.";
        }

        private static string CellaTabella(ITableAnnotation t, int r, int c)
        {
            try { return t.get_Text(r, c) ?? ""; } catch { return ""; }
        }

        /// <summary>Il testo di una cella senza i codici di formato: «&lt;MOD-DIAM&gt;» diventa «Ø».</summary>
        private static string PulisciCella(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("<MOD-DIAM>", "Ø");
            s = Regex.Replace(s, "<[^>]*>", "");
            return s.Trim();
        }

        /// <summary>
        /// Il primo modello di tabella nelle cartelle che SOLIDWORKS dichiara per
        /// quel tipo; vuoto se non ce n'e'. Si chiede, non si scrive a mano (voce
        /// GT5: la costante giusta serve a fare la domanda, non a saltarla).
        /// </summary>
        private string PrimoModelloDiTabella(int preferenza, string estensione)
        {
            try
            {
                string cartelle = _sw.GetUserPreferenceStringValue(preferenza) ?? "";
                foreach (var c in cartelle.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(c) || !System.IO.Directory.Exists(c)) continue;
                    var f = System.IO.Directory.GetFiles(c, "*" + estensione, System.IO.SearchOption.AllDirectories);
                    if (f.Length > 0) { Array.Sort(f, StringComparer.OrdinalIgnoreCase); return f[0]; }
                }
            }
            catch { }
            return "";
        }
    }
}
