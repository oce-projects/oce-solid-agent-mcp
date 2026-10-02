using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// UN POSTO LIBERO SUL FOGLIO, per una tabella o una vista messa dopo le altre.
    ///
    /// ── NATO GUARDANDO DUE PNG, 26/9/2026, col banco verde tutte e due le volte ──
    ///
    /// La distinta di taglio a 250/200 mm usciva dal foglio a destra dopo la
    /// quarta delle sue 17 colonne; la vista dello sviluppo a 300/90 mm, a scala
    /// 2:1, scendeva dentro il cartiglio. Le misure erano giuste — righe, quantita',
    /// ingombro dello sviluppo — e il foglio era sbagliato.
    ///
    /// Il posto: dentro la cornice (12 mm dal bordo), fuori dalle altre viste
    /// (`GetOutline`, 4 mm di stacco) e fuori dal cartiglio — letto dal formato con
    /// `ITitleBlock.GetExtents` quando il formato lo dichiara, se no stimato
    /// nell'angolo in basso a destra (meta' foglio x 65 mm), e lo si dice.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class FoglioLibero
        {
            public double Larghezza = 0.42, Altezza = 0.297;
            public double[] Cartiglio;             // x0, y0, x1, y1 in metri
            public bool CartiglioStimato;
            public List<double[]> Ingombri = new List<double[]>();
            public const double Cornice = 0.012, Stacco = 0.004;

            /// <summary>Null se il rettangolo e' libero, se no cosa tocca.</summary>
            public string Tocca(double x0, double y0, double x1, double y1)
            {
                if (x0 < Cornice - 1e-6 || x1 > Larghezza - Cornice + 1e-6 || y0 < Cornice - 1e-6 || y1 > Altezza - Cornice + 1e-6)
                    return "esce dalla cornice del foglio";
                foreach (var r in Ingombri)
                    if (x1 > r[0] - Stacco && x0 < r[2] + Stacco && y1 > r[1] - Stacco && y0 < r[3] + Stacco)
                        return ReferenceEquals(r, Cartiglio) ? "cade sul cartiglio" : "cade su una vista, una tabella o una nota";
                return null;
            }
        }

        /// <summary>
        /// Il foglio corrente con cio' che lo occupa: il cartiglio e le viste,
        /// tranne `esclusa` (la vista che si sta per spostare).
        /// </summary>
        private static FoglioLibero LeggiIlFoglio(IDrawingDoc disegno, IView esclusa, string tabellaEsclusa = null)
        {
            var f = new FoglioLibero();
            ISheet foglio = null;
            try
            {
                foglio = disegno.GetCurrentSheet() as ISheet;
                double w = 0, h = 0;
                foglio?.GetSize(ref w, ref h);
                if (w > 0.05 && h > 0.05) { f.Larghezza = w; f.Altezza = h; }
            }
            catch { }
            try
            {
                var tb = foglio?.TitleBlock;
                if (tb != null)
                {
                    double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                    tb.GetExtents(ref x0, ref y0, ref x1, ref y1);
                    if (Math.Abs(x1 - x0) > 0.01 && Math.Abs(y1 - y0) > 0.01)
                        f.Cartiglio = new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1) };
                }
            }
            catch { }
            f.CartiglioStimato = f.Cartiglio == null;
            if (f.Cartiglio == null) f.Cartiglio = new[] { f.Larghezza / 2, 0, f.Larghezza, 0.065 };
            f.Ingombri.Add(f.Cartiglio);
            string esclusaNome = null;
            try { esclusaNome = esclusa?.GetName2(); } catch { }
            try
            {
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    string n = null;
                    try { n = v.GetName2(); } catch { }
                    if (esclusaNome == null || n != esclusaNome)
                    {
                        var o = v.GetOutline() as double[];
                        if (o != null && o.Length >= 4) f.Ingombri.Add(new[] { o[0], o[1], o[2], o[3] });
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            // ANCHE TABELLE E NOTE GIA' SUL FOGLIO (revisione del 26/9/2026): una
            // tabella fori a 15/(H-15) e una tabella di taglio «in alto a sinistra»
            // finivano una sopra l'altra. Si guardano il foglio e ogni vista.
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var t = o as ITableAnnotation;
                        var r = IngombroDellaTabella(t, tabellaEsclusa);
                        if (r != null) f.Ingombri.Add(r);
                    }
                    var nota = v.GetFirstNote() as INote;
                    while (nota != null)
                    {
                        try
                        {
                            var e = nota.GetExtent() as double[];
                            if (e != null && e.Length >= 6)
                            {
                                double x0 = Math.Min(e[0], e[3]), y0 = Math.Min(e[1], e[4]), x1 = Math.Max(e[0], e[3]), y1 = Math.Max(e[1], e[4]);
                                // una «note» grande mezzo foglio e' una cornice del formato, non un ingombro
                                if ((x1 - x0) * (y1 - y0) < 0.5 * f.Larghezza * f.Altezza && x1 > x0 && y1 > y0)
                                    f.Ingombri.Add(new[] { x0, y0, x1, y1 });
                            }
                        }
                        catch { }
                        nota = nota.GetNext() as INote;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return f;
        }

        /// <summary>
        /// Il rettangolo di una tabella sul foglio (x0, y0, x1, y1), dall'ancora e
        /// dalla somma di colonne e righe; null se e' quella da escludere o non si
        /// legge.
        /// </summary>
        private static double[] IngombroDellaTabella(ITableAnnotation t, string esclusa)
        {
            if (t == null) return null;
            try
            {
                var an = t.GetAnnotation() as IAnnotation;
                if (an == null) return null;
                if (esclusa != null && an.GetName() == esclusa) return null;
                var p = an.GetPosition() as double[];
                if (p == null || p.Length < 2) return null;
                double w = 0, h = 0;
                for (int c = 0; c < t.ColumnCount; c++) w += t.GetColumnWidth(c);
                for (int r = 0; r < t.RowCount; r++) h += t.GetRowHeight(r);
                if (w <= 0 || h <= 0) return null;
                double x0 = p[0], y1 = p[1];
                switch (t.AnchorType)
                {
                    case (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopRight: x0 = p[0] - w; break;
                    case (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomLeft: y1 = p[1] + h; break;
                    case (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomRight: x0 = p[0] - w; y1 = p[1] + h; break;
                }
                return new[] { x0, y1 - h, x0 + w, y1 };
            }
            catch { return null; }
        }

        /// <summary>
        /// Il primo posto libero per un rettangolo w x h (metri), fra: sopra il
        /// cartiglio a destra, in alto a destra, in alto a sinistra, in basso a
        /// sinistra, poi una griglia a passi di 10 mm. Torna l'angolo in basso a
        /// sinistra e dove, o null.
        /// </summary>
        private static double[] PostoLibero(FoglioLibero f, double w, double h, out string dove)
        {
            double c = FoglioLibero.Cornice, s = FoglioLibero.Stacco;
            var candidati = new List<Tuple<double, double, string>>
            {
                Tuple.Create(Math.Min(f.Cartiglio[2], f.Larghezza - c) - w, f.Cartiglio[3] + s, "sopra il cartiglio"),
                Tuple.Create(f.Larghezza - c - w, f.Altezza - c - h, "in alto a destra"),
                Tuple.Create(c, f.Altezza - c - h, "in alto a sinistra"),
                Tuple.Create(c, c, "in basso a sinistra"),
            };
            foreach (var k in candidati)
                if (f.Tocca(k.Item1, k.Item2, k.Item1 + w, k.Item2 + h) == null)
                {
                    dove = k.Item3;
                    return new[] { k.Item1, k.Item2 };
                }
            // La griglia: da destra a sinistra, dall'alto in basso.
            for (double y = f.Altezza - c - h; y >= c - 1e-9; y -= 0.01)
                for (double x = f.Larghezza - c - w; x >= c - 1e-9; x -= 0.01)
                    if (f.Tocca(x, y, x + w, y + h) == null)
                    {
                        dove = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                             "nel primo posto libero ({0:0}/{1:0} mm)", x * 1000, y * 1000);
                        return new[] { x, y };
                    }
            dove = null;
            return null;
        }
    }
}
