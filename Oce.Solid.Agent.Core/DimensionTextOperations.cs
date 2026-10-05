using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
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
    public partial class SwOperations
    {
        private sealed class DimensionText
        {
            public string View, Name, Text;
            public double[][] Angles;    // quattro punti sul foglio, metri
        }

        /// <summary>
        /// I testi di tutte le quote della tavola, col loro rettangolo; null
        /// when the walk over views and dimensions broke off (Z-C21.8, finding
        /// N47). GetFirstView, GetNextView, GetFirstDisplayDimension5 and
        /// GetNext5 sit outside the try of each dimension: an exception there
        /// used to end the walk with the texts read so far, handed over as all
        /// the texts of the drawing, and drawing_dimension counted overlaps on
        /// that part as if it were the whole. A dimension whose texts cannot be
        /// read is still skipped inside the walk, as before.
        /// </summary>
        private static List<DimensionText> DimensionTexts(IDrawingDoc drawing)
        {
            var outside = new List<DimensionText>();
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    string viewName = "";
                    try { viewName = v.GetName2() ?? ""; } catch { }
                    var dd = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (dd != null)
                    {
                        try
                        {
                            var data = dd.GetDisplayData() as IDisplayData;
                            // ZERO-E-ZERO: una quota senza dati di visualizzazione non ha testi da
                            // confrontare, e non si inventano; se non se ne legge nessuno,
                            // drawing_dimension dice null («non lo so»), non «nessuno si accavalla».
                            int n = data == null ? 0 : data.GetTextCount();
                            // A POINT ON THE SHEET, NOT AN OFFSET (Z-C21.3, finding C3-1,
                            // NOT REAL). Every GetTextPositionAtIndex page says «Gets the
                            // text item offset relative to the note text point»; for the
                            // texts of a display dimension that sentence does not hold.
                            // The Z-C21.3 probe of C3-1 (28/9/2026): a toleranced 100
                            // (6 texts) and a plain 10 (1 text) moved with SetPosition2 by
                            // (20, 10) mm, then with their view by (30, -10) mm, and every
                            // text position moved by exactly the same; each stayed within
                            // 8 mm of IAnnotation.GetPosition while lying 260-310 mm from
                            // the sheet origin. So p is used as it is, in sheet metres.
                            for (int i = 0; i < n; i++)
                            {
                                var p = data.GetTextPositionAtIndex(i) as double[];
                                double w = data.GetTextInBoxWidthAtIndex(i), h = data.GetTextHeightAtIndex(i);
                                double angle = data.GetTextAngleAtIndex(i);
                                string t = data.GetTextAtIndex(i) ?? "";
                                if (p == null || w <= 0 || h <= 0 || t.Trim().Length == 0) continue;
                                // Il riferimento: in basso a sinistra (1), che e' l'unico visto.
                                // Gli altri si portano all'angolo in basso a sinistra.
                                double ux = Math.Cos(angle), uy = Math.Sin(angle), vx = -uy, vy = ux;
                                double x0 = p[0], y0 = p[1];
                                switch (data.GetTextRefPositionAtIndex(i))
                                {
                                    case (int)swTextPosition_e.swUPPER_LEFT: x0 -= vx * h; y0 -= vy * h; break;
                                    case (int)swTextPosition_e.swCENTER: x0 -= ux * w / 2 + vx * h / 2; y0 -= uy * w / 2 + vy * h / 2; break;
                                    case (int)swTextPosition_e.swUPPER_RIGHT: x0 -= ux * w + vx * h; y0 -= uy * w + vy * h; break;
                                    case (int)swTextPosition_e.swLOWER_RIGHT: x0 -= ux * w; y0 -= uy * w; break;
                                    case (int)swTextPosition_e.swUPPER_CENTER: x0 -= ux * w / 2 + vx * h; y0 -= uy * w / 2 + vy * h; break;
                                }
                                outside.Add(new DimensionText
                                {
                                    View = viewName,
                                    Name = SafeName(dd),
                                    Text = t.Trim(),
                                    Angles = new[]
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
            catch { return null; }   // N47: a list cut short is not the list of the drawing
            return outside;
        }

        private static string SafeName(IDisplayDimension dd)
        {
            try { return dd.GetNameForSelection() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// Chi si accavalla con chi: testo su testo (tutta la tavola), e testo su
        /// uno spigolo della propria vista. Le righe dicono i due nomi e i testi.
        /// Un margine di 0,3 mm: due testi che si sfiorano non si leggono male.
        /// </summary>
        private List<string> TextsThatOverlap(IDrawingDoc drawing, IView view, List<DimensionText> texts)
        {
            // Texts not read (N47) give no overlaps to report, and the caller
            // says «not known» instead of a count.
            if (texts == null) return null;
            var outside = new List<string>();
            const double margin = 0.0003;
            // I PEZZI DI UNA STESSA QUOTA NON SI CONFRONTANO FRA LORO (visto al
            // primo banco, 26/9/2026): il richiamo «Ø8 ↧5» sono quattro testi
            // attaccati — <MOD-DIAM>, 8, <HOLE-DEPTH>, 5 — e il controllo ne
            // contava quattro sovrapposizioni che sul foglio sono una scritta sola.
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                    if (!(texts[i].View == texts[j].View && texts[i].Name == texts[j].Name && texts[i].Name.Length > 0) &&
                        TheyTouch(texts[i].Angles, texts[j].Angles, margin))
                        outside.Add("«" + texts[i].Text + "» (" + texts[i].View + ") sopra «" + texts[j].Text + "» (" + texts[j].View + ")");

            // Gli spigoli della vista, portati sul foglio come segmenti (le curve a tratti).
            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }
            var segments = ViewSegments(view);
            foreach (var t in texts)
            {
                if (t.View != viewName) continue;
                foreach (var s in segments)
                    if (SegmentInRectangle(s, t.Angles, -margin))
                    {
                        outside.Add("«" + t.Text + "» (" + t.View + ") sopra una linea del pezzo");
                        break;
                    }
            }
            return outside;
        }

        private List<double[]> ViewSegments(IView view)
        {
            var outside = new List<double[]>();
            IMathUtility mu = null; MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) return outside;
            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            foreach (var o in edges ?? new object[0])
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
                        if (prec != null) outside.Add(new[] { prec[0], prec[1], q[0], q[1] });
                        prec = q;
                    }
                }
                catch { }
            }
            return outside;
        }

        // ── Geometria piana: rettangoli orientati come poligoni convessi ─────

        private static bool TheyTouch(double[][] a, double[][] b, double margin)
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
                    if (amax + margin < bmin || bmax + margin < amin) return false;
                }
            return true;
        }

        /// <summary>
        /// Il segmento passa DENTRO il rettangolo (ristretto di `-margine` se
        /// negativo: una linea che sfiora il bordo del testo non lo copre).
        /// </summary>
        private static bool SegmentInRectangle(double[] s, double[][] r, double margin)
        {
            // Sistema del rettangolo: origine r[0], assi u (r0->r1), v (r0->r3).
            double ux = r[1][0] - r[0][0], uy = r[1][1] - r[0][1], vx = r[3][0] - r[0][0], vy = r[3][1] - r[0][1];
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-12 || lv < 1e-12) return false;
            ux /= lu; uy /= lu; vx /= lv; vy /= lv;
            double m = -margin;   // positivo: quanto si restringe
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
