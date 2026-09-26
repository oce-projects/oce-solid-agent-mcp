using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA VISTA DELLO SVILUPPO IN TAVOLA (voce GT13).
    ///
    /// ── MISURATA PRIMA DI SCRIVERLA, 26/9/2026 (`prova0089 sviltav`) ────────
    ///
    /// `IDrawingDoc.CreateFlatPatternViewFromModelView3(percorso,
    /// configurazione, X, Y, Z, NascondiPieghe, Ribalta)` sulla L dei casi di
    /// lamiera (30 + 50, spessore 2, raggio 2, profondita' 40): la vista nasce
    /// al primo colpo, `IsFlatPatternView` vero, una linea di piega.
    ///
    /// **L'ingombro NON si legge da `GetOutline`**: diceva 51,88 x 92,592 mm per
    /// uno sviluppo di 80,712 x 40 — il contorno della vista comprende la nota
    /// della piega e un margine. Si misura dagli SPIGOLI VISIBILI della vista
    /// (`GetVisibleEntities2`), portati sul foglio con `ModelToViewTransform` e
    /// divisi per la scala; e si confronta con lo sviluppo misurato sul pezzo
    /// PRIMA di mettere la vista (la lavorazione «FlatPattern» tolta di
    /// soppressione, il corpo misurato, rimessa).
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito VistaSviluppo(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito("Il documento attivo non e' una tavola: la vista dello sviluppo si mette in una " +
                               "tavola. Creala con «crea_disegno».");

            string percorsoModello = Str(a, "modello", null);
            if (string.IsNullOrWhiteSpace(percorsoModello))
            {
                string perche;
                percorsoModello = ModelloDaMettereInVista(out perche);
                if (percorsoModello == null) return Fallito(perche);
            }
            var percorsoSw = PercorsoCanonico(percorsoModello);
            if (percorsoSw == null)
                return Fallito("«" + percorsoModello + "» non risulta aperto: la vista nascerebbe vuota.");
            var pezzo = _sw.GetOpenDocumentByName(percorsoSw) as IModelDoc2;
            if (!(pezzo is IPartDoc))
                return Fallito("«" + percorsoSw + "» non e' una parte: lo sviluppo e' di una parte di lamiera.");
            if (DatiDellaLamiera(pezzo) == null)
                return Fallito("«" + System.IO.Path.GetFileName(percorsoSw) + "» non e' di lamiera (non ha la " +
                               "lavorazione «SheetMetal»): non c'e' uno sviluppo da mettere in tavola.");

            string configurazione = Str(a, "configurazione", null);
            if (string.IsNullOrWhiteSpace(configurazione))
                try { configurazione = (pezzo.GetActiveConfiguration() as IConfiguration)?.Name ?? ""; } catch { configurazione = ""; }
            double x = Unita.M(Num(a, "x_mm", 300));
            double y = Unita.M(Num(a, "y_mm", 90));
            bool nascondiPieghe = Bool(a, "nascondi_pieghe", false);

            // ── LO SVILUPPO DEL PEZZO, MISURATO PRIMA ────────────────────────
            double lungoPezzo = LunghezzaSviluppata(pezzo, out double _);

            int prima = ContaViste(disegno);
            object grezza = null;
            try
            {
                grezza = disegno.CreateFlatPatternViewFromModelView3(
                    percorsoSw, configurazione ?? "", x, y, 0, nascondiPieghe, false);
            }
            catch (Exception ex) { return Fallito("La vista dello sviluppo non e' nata: " + ex.Message); }
            var vista = grezza as IView;
            if (vista == null)
                return Fallito("SOLIDWORKS non ha creato la vista dello sviluppo e non ha detto perche' " +
                               "(configurazione «" + configurazione + "»).");
            string nome = "";
            try { nome = vista.GetName2() ?? ""; } catch { }

            bool piatta = false;
            try { piatta = vista.IsFlatPatternView(); } catch { }
            if (!piatta)
            {
                TogliLaVista(modello, nome);
                return Fallito("La vista «" + nome + "» e' nata ma non e' uno sviluppo (IsFlatPatternView falso): " +
                               "l'ho tolta.");
            }
            int pieghe = -1;
            try { pieghe = vista.GetBendLineCount(); } catch { }

            // ── UN POSTO LIBERO (visto nel PNG, 26/9/2026) ───────────────────
            //
            // Messa a 300/90 mm a scala 2:1, la L distesa scendeva dentro il
            // cartiglio, col banco verde. Si misura il contorno della vista (con
            // la nota di piega: e' quello che occupa il foglio) e la si sposta in
            // un posto libero, se «x_mm»/«y_mm» non l'hanno fissata.
            string dove, motivoPosto;
            if (!SviluppoAlSuoPosto(disegno, vista, a.ContainsKey("x_mm") || a.ContainsKey("y_mm"), out dove, out motivoPosto))
            {
                TogliLaVista(modello, nome);
                return Fallito(motivoPosto + " Ho tolto la vista.");
            }

            // ── L'INGOMBRO DAGLI SPIGOLI VISIBILI ────────────────────────────
            double[] ingombro = IngombroDagliSpigoli(vista);
            var misure = new Dictionary<string, object>
            {
                ["vista"] = nome,
                ["modello"] = percorsoSw,
                ["configurazione"] = configurazione,
                ["viste_prima"] = prima,
                ["viste_dopo"] = ContaViste(disegno),
                ["piatta"] = true,
                ["linee_di_piega"] = NumeroOppureNull(pieghe),
                ["ingombro_mm"] = ingombro == null ? null : new object[] { ingombro[0], ingombro[1] },
                ["lunghezza_sviluppo_mm"] = double.IsNaN(lungoPezzo) ? null : (object)Math.Round(lungoPezzo, 3),
            };
            try { misure["scala"] = vista.ScaleDecimal; } catch { }
            misure["dove_sta_la_vista"] = dove;
            try
            {
                var o = vista.GetOutline() as double[];
                if (o != null) misure["contorno_vista_mm"] = new object[] { Math.Round(o[0] * 1000, 1), Math.Round(o[1] * 1000, 1), Math.Round(o[2] * 1000, 1), Math.Round(o[3] * 1000, 1) };
            }
            catch { }
            misure["dentro_il_foglio"] = true;
            if (ingombro == null)
            {
                TogliLaVista(modello, nome);
                return Fallito("La vista dello sviluppo e' nata ma non riesco a misurarne gli spigoli: senza " +
                               "misura non so se e' lo sviluppo giusto, e l'ho tolta.");
            }
            if (!double.IsNaN(lungoPezzo) && Math.Abs(ingombro[0] - lungoPezzo) > 0.1)
            {
                TogliLaVista(modello, nome);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "La vista dello sviluppo misura {0:0.###} mm sul lato lungo e lo sviluppo del pezzo " +
                    "{1:0.###}: non e' lo sviluppo di questo pezzo (o non e' sviluppato), e l'ho tolta.",
                    ingombro[0], lungoPezzo));
            }
            if (double.IsNaN(lungoPezzo))
            {
                // SENZA LO SVILUPPO DEL PEZZO NON C'E' RISCONTRO (revisione del
                // 26/9/2026): la vista potrebbe essere di un'altra cosa.
                bool tolta = TogliLaVista(modello, nome);
                return Fallito("Non riesco a misurare lo sviluppo del pezzo (la lavorazione «FlatPattern» non si lascia " +
                               "misurare), e senza non so dire se la vista mostra lo sviluppo giusto. " +
                               (tolta ? "Ho tolto la vista." : "Togli la vista: " + Frasi.CosiSiRitira + "."));
            }
            misure["combacia_col_pezzo"] = true;
            return SenzaFeature(misure);
        }

        /// <summary>
        /// L'ingombro della vista in mm, dal suo disegno vero: gli spigoli
        /// visibili, campionati, portati sul foglio e divisi per la scala.
        /// [lato lungo, lato corto], o null se non si legge niente.
        /// </summary>
        private double[] IngombroDagliSpigoli(IView vista)
        {
            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = vista.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) return null;
            double scala = 1;
            try { scala = vista.ScaleDecimal; } catch { }
            if (scala <= 0) scala = 1;
            object[] spigoli = null;
            try { spigoli = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            if (spigoli == null || spigoli.Length == 0) return null;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            int punti = 0;
            foreach (var o in spigoli)
            {
                var e = o as IEdge;
                if (e == null) continue;
                var campioni = new List<double[]>();
                try
                {
                    var cp = e.GetCurveParams3() as CurveParamData;
                    var c = e.GetCurve() as ICurve;
                    if (cp != null && c != null)
                    {
                        var tess = c.GetTessPts(1e-5, 1e-3, cp.StartPoint, cp.EndPoint) as double[];
                        if (tess != null)
                            for (int i = 0; i + 2 < tess.Length; i += 3)
                                campioni.Add(new[] { tess[i], tess[i + 1], tess[i + 2] });
                        var s = cp.StartPoint as double[];
                        var f = cp.EndPoint as double[];
                        if (s != null) campioni.Add(s);
                        if (f != null) campioni.Add(f);
                    }
                }
                catch { }
                foreach (var p in campioni)
                {
                    double[] q;
                    try { q = ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[]; }
                    catch { continue; }
                    if (q == null) continue;
                    x0 = Math.Min(x0, q[0]); x1 = Math.Max(x1, q[0]);
                    y0 = Math.Min(y0, q[1]); y1 = Math.Max(y1, q[1]);
                    punti++;
                }
            }
            if (punti < 2) return null;
            double w = (x1 - x0) / scala * 1000.0, h = (y1 - y0) / scala * 1000.0;
            return new[] { Math.Round(Math.Max(w, h), 3), Math.Round(Math.Min(w, h), 3) };
        }

        /// <summary>
        /// Porta la vista in un posto libero del foglio (OperazioniPostoSulFoglio),
        /// o controlla che quello chiesto lo sia. False col motivo.
        /// </summary>
        private static bool SviluppoAlSuoPosto(IDrawingDoc disegno, IView vista, bool chiesto, out string dove, out string motivo)
        {
            dove = null; motivo = null;
            var o = vista.GetOutline() as double[];
            var p = vista.Position as double[];
            if (o == null || o.Length < 4 || p == null || p.Length < 2)
            {
                motivo = "La vista dello sviluppo e' nata ma non riesco a leggere dove sta sul foglio.";
                return false;
            }
            double w = o[2] - o[0], h = o[3] - o[1];
            var foglio = LeggiIlFoglio(disegno, vista);
            if (chiesto)
            {
                string perche = foglio.Tocca(o[0], o[1], o[2], o[3]);
                if (perche != null)
                {
                    motivo = string.Format(CultureInfo.InvariantCulture,
                        "La vista dello sviluppo ({0:0} x {1:0} mm sul foglio) dove l'hai chiesta {2}. Togli «x_mm» e " +
                        "«y_mm» e la metto io in un posto libero.", w * 1000, h * 1000, perche);
                    return false;
                }
                dove = "dove chiesto";
                return true;
            }
            var basso = PostoLibero(foglio, w, h, out dove);
            if (basso == null)
            {
                motivo = string.Format(CultureInfo.InvariantCulture,
                    "Sul foglio non c'e' un posto libero per la vista dello sviluppo ({0:0} x {1:0} mm) fuori dalle altre " +
                    "viste e dal cartiglio: usa un foglio piu' grande o un'altra scala.", w * 1000, h * 1000);
                return false;
            }
            // La posizione della vista non e' per forza il centro del contorno:
            // si sposta del tanto che serve all'angolo in basso a sinistra.
            try { vista.Position = new[] { p[0] + basso[0] - o[0], p[1] + basso[1] - o[1] }; } catch { }
            var o2 = vista.GetOutline() as double[];
            string ancora = o2 == null ? "non si legge" : foglio.Tocca(o2[0], o2[1], o2[2], o2[3]);
            if (ancora != null)
            {
                motivo = "Ho spostato la vista dello sviluppo " + dove + " ma " + ancora + ".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Toglie una vista appena messa, per nome. `DeleteSelection2` e non
        /// `EditDelete`: la seconda puo' chiedere conferma con una finestra, e
        /// una chiamata COM che aspetta una finestra non torna (vedi
        /// OperazioniSw, «DeleteSelection2 E NON EditDelete»).
        /// </summary>
        private static bool TogliLaVista(IModelDoc2 disegno, string nome)
        {
            if (string.IsNullOrEmpty(nome)) return false;
            try
            {
                disegno.ClearSelection2(true);
                if (disegno.Extension.SelectByID2(nome, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                    disegno.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                disegno.ClearSelection2(true);
                // TOLTA SI RILEGGE: la vista con quel nome non deve esserci piu'.
                var v = (disegno as IDrawingDoc)?.GetFirstView() as IView;
                while (v != null)
                {
                    if (string.Equals(v.GetName2(), nome, StringComparison.Ordinal)) return false;
                    v = v.GetNextView() as IView;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
