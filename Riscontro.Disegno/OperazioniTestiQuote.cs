using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// IL TESTO DELLE QUOTE, E DOVE CADE (voce GT12, punto 3).
    ///
    /// ── L'INGOMBRO SI LEGGE, NON SI STIMA (26/9/2026, `prova0089 ciecotav`) ──
    ///
    /// GT11 aveva scritto «l'ingombro del testo di una quota non si legge da
    /// nessuna parte». Per riflessione sull'interop non c'e' un `GetExtent` sulle
    /// quote (c'e' solo su `INote`); ma `IDisplayDimension.GetDisplayData()`
    /// torna un `IDisplayData` che per ogni testo dice posizione, altezza,
    /// angolo, punto di riferimento e — `GetTextInBoxWidthAtIndex` — la
    /// LARGHEZZA, anche senza riquadro. Misurato: « 10 » 7,76 mm, « 120 »
    /// 10,34 mm, altezza 3,5; riferimento 1 = `swLOWER_LEFT` (la posizione e'
    /// l'angolo in basso a sinistra del testo, nel suo verso).
    ///
    /// Con quelli, il rettangolo vero di ogni testo sul foglio: si guarda se
    /// cade su un altro testo, o sugli spigoli della propria vista.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class TestoDiQuota
        {
            public string Vista, Nome, Testo;
            public double[][] Angoli;    // quattro punti sul foglio, metri
        }

        /// <summary>I testi di tutte le quote della tavola, col loro rettangolo.</summary>
        private static List<TestoDiQuota> TestiDelleQuote(IDrawingDoc disegno)
        {
            var fuori = new List<TestoDiQuota>();
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    string nomeVista = "";
                    try { nomeVista = v.GetName2() ?? ""; } catch { }
                    var dd = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (dd != null)
                    {
                        try
                        {
                            var dati = dd.GetDisplayData() as IDisplayData;
                            // ZERO-E-ZERO: una quota senza dati di visualizzazione non ha testi da
                            // confrontare, e non si inventano; se non se ne legge nessuno,
                            // quota_in_tavola dice null («non lo so»), non «nessuno si accavalla».
                            int n = dati == null ? 0 : dati.GetTextCount();
                            for (int i = 0; i < n; i++)
                            {
                                var p = dati.GetTextPositionAtIndex(i) as double[];
                                double w = dati.GetTextInBoxWidthAtIndex(i), h = dati.GetTextHeightAtIndex(i);
                                double ang = dati.GetTextAngleAtIndex(i);
                                string t = dati.GetTextAtIndex(i) ?? "";
                                if (p == null || w <= 0 || h <= 0 || t.Trim().Length == 0) continue;
                                // Il riferimento: in basso a sinistra (1), che e' l'unico visto.
                                // Gli altri si portano all'angolo in basso a sinistra.
                                double ux = Math.Cos(ang), uy = Math.Sin(ang), vx = -uy, vy = ux;
                                double x0 = p[0], y0 = p[1];
                                switch (dati.GetTextRefPositionAtIndex(i))
                                {
                                    case (int)swTextPosition_e.swUPPER_LEFT: x0 -= vx * h; y0 -= vy * h; break;
                                    case (int)swTextPosition_e.swCENTER: x0 -= ux * w / 2 + vx * h / 2; y0 -= uy * w / 2 + vy * h / 2; break;
                                    case (int)swTextPosition_e.swUPPER_RIGHT: x0 -= ux * w + vx * h; y0 -= uy * w + vy * h; break;
                                    case (int)swTextPosition_e.swLOWER_RIGHT: x0 -= ux * w; y0 -= uy * w; break;
                                    case (int)swTextPosition_e.swUPPER_CENTER: x0 -= ux * w / 2 + vx * h; y0 -= uy * w / 2 + vy * h; break;
                                }
                                fuori.Add(new TestoDiQuota
                                {
                                    Vista = nomeVista,
                                    Nome = SafeNome(dd),
                                    Testo = t.Trim(),
                                    Angoli = new[]
                                    {
                                        new[] { x0, y0 },
                                        new[] { x0 + ux * w, y0 + uy * w },
                                        new[] { x0 + ux * w + vx * h, y0 + uy * w + vy * h },
                                        new[] { x0 + vx * h, y0 + vy * h },
                                    },
                                });
                            }
                        }
                        catch { }
                        dd = dd.GetNext5() as IDisplayDimension;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return fuori;
        }

        private static string SafeNome(IDisplayDimension dd)
        {
            try { return dd.GetNameForSelection() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// Chi si accavalla con chi: testo su testo (tutta la tavola), e testo su
        /// uno spigolo della propria vista. Le righe dicono i due nomi e i testi.
        /// Un margine di 0,3 mm: due testi che si sfiorano non si leggono male.
        /// </summary>
        private List<string> TestiCheSiAccavallano(IDrawingDoc disegno, IView vista, List<TestoDiQuota> testi)
        {
            var fuori = new List<string>();
            const double margine = 0.0003;
            // I PEZZI DI UNA STESSA QUOTA NON SI CONFRONTANO FRA LORO (visto al
            // primo banco, 26/9/2026): il richiamo «Ø8 ↧5» sono quattro testi
            // attaccati — <MOD-DIAM>, 8, <HOLE-DEPTH>, 5 — e il controllo ne
            // contava quattro sovrapposizioni che sul foglio sono una scritta sola.
            for (int i = 0; i < testi.Count; i++)
                for (int j = i + 1; j < testi.Count; j++)
                    if (!(testi[i].Vista == testi[j].Vista && testi[i].Nome == testi[j].Nome && testi[i].Nome.Length > 0) &&
                        SiToccano(testi[i].Angoli, testi[j].Angoli, margine))
                        fuori.Add("«" + testi[i].Testo + "» (" + testi[i].Vista + ") sopra «" + testi[j].Testo + "» (" + testi[j].Vista + ")");

            // Gli spigoli della vista, portati sul foglio come segmenti (le curve a tratti).
            string nomeVista = "";
            try { nomeVista = vista.GetName2() ?? ""; } catch { }
            var segmenti = SegmentiDellaVista(vista);
            foreach (var t in testi)
            {
                if (t.Vista != nomeVista) continue;
                foreach (var s in segmenti)
                    if (SegmentoNelRettangolo(s, t.Angoli, -margine))
                    {
                        fuori.Add("«" + t.Testo + "» (" + t.Vista + ") sopra una linea del pezzo");
                        break;
                    }
            }
            return fuori;
        }

        private List<double[]> SegmentiDellaVista(IView vista)
        {
            var fuori = new List<double[]>();
            IMathUtility mu = null; MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = vista.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) return fuori;
            object[] spigoli = null;
            try { spigoli = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            foreach (var o in spigoli ?? new object[0])
            {
                try
                {
                    var e = o as IEdge;
                    var cp = ApiHelpers.EdgeParameters(e, out ICurve c);
                    if (c == null || cp == null) continue;
                    var tess = c.GetTessPts(1e-4, 1e-3, cp.StartPoint, cp.EndPoint) as double[];
                    if (tess == null || tess.Length < 6) continue;
                    double[] prec = null;
                    for (int i = 0; i + 2 < tess.Length; i += 3)
                    {
                        var q = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { tess[i], tess[i + 1], tess[i + 2] })).MultiplyTransform(tr)).ArrayData as double[];
                        if (q == null) continue;
                        if (prec != null) fuori.Add(new[] { prec[0], prec[1], q[0], q[1] });
                        prec = q;
                    }
                }
                catch { }
            }
            return fuori;
        }

        // ── Geometria piana: rettangoli orientati come poligoni convessi ─────

        private static bool SiToccano(double[][] a, double[][] b, double margine)
        {
            // Assi separatori (SAT) sui lati dei due rettangoli.
            foreach (var poli in new[] { a, b })
                for (int i = 0; i < 4; i++)
                {
                    var p = poli[i]; var q = poli[(i + 1) % 4];
                    double nx = -(q[1] - p[1]), ny = q[0] - p[0];
                    double l = Math.Sqrt(nx * nx + ny * ny);
                    if (l < 1e-12) continue;
                    nx /= l; ny /= l;
                    double amin = double.MaxValue, amax = double.MinValue, bmin = double.MaxValue, bmax = double.MinValue;
                    foreach (var r in a) { double d = r[0] * nx + r[1] * ny; amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
                    foreach (var r in b) { double d = r[0] * nx + r[1] * ny; bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
                    if (amax + margine < bmin || bmax + margine < amin) return false;
                }
            return true;
        }

        /// <summary>
        /// Il segmento passa DENTRO il rettangolo (ristretto di `-margine` se
        /// negativo: una linea che sfiora il bordo del testo non lo copre).
        /// </summary>
        private static bool SegmentoNelRettangolo(double[] s, double[][] r, double margine)
        {
            // Sistema del rettangolo: origine r[0], assi u (r0->r1), v (r0->r3).
            double ux = r[1][0] - r[0][0], uy = r[1][1] - r[0][1], vx = r[3][0] - r[0][0], vy = r[3][1] - r[0][1];
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-12 || lv < 1e-12) return false;
            ux /= lu; uy /= lu; vx /= lv; vy /= lv;
            double m = -margine;   // positivo: quanto si restringe
            Func<double, double, double[]> loc = (x, y) => new[] { (x - r[0][0]) * ux + (y - r[0][1]) * uy, (x - r[0][0]) * vx + (y - r[0][1]) * vy };
            var a = loc(s[0], s[1]); var b = loc(s[2], s[3]);
            // Clipping di Liang-Barsky sul rettangolo [m, lu-m] x [m, lv-m].
            double t0 = 0, t1 = 1, dx = b[0] - a[0], dy = b[1] - a[1];
            double[] pp = { -dx, dx, -dy, dy };
            double[] qq = { a[0] - m, (lu - m) - a[0], a[1] - m, (lv - m) - a[1] };
            for (int k = 0; k < 4; k++)
            {
                if (Math.Abs(pp[k]) < 1e-15) { if (qq[k] < 0) return false; continue; }
                double t = qq[k] / pp[k];
                if (pp[k] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return t0 <= t1;
        }
    }
}
