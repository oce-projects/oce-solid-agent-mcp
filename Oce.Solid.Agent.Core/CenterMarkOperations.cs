using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// CROCI DI CENTRO SUI FORI IN TAVOLA (voce W2.11).
    ///
    /// ── PERCHE' E' UN'OPERAZIONE E NON UNA RIFINITURA ───────────────────
    ///
    /// Una tavola con dodici fori e nessuna croce di centro esce, si stampa, e
    /// sembra completa. In officina manca la cosa che serve per forare: **dove
    /// sta il centro.** Nessun errore, risultato inutilizzabile.
    ///
    /// ── COSA SI E' MISURATO, DAL 17/9 AL 26/9/2026 ─────────────────────
    ///
    /// Il 17/9 tre sonde: `AutoInsertCenterMarks2` (25 combinazioni),
    /// `InsertCenterMark2/3`: «risponde True e non mette niente», contando con
    /// `GetCenterMarkCount`. Il 26/9 (`prova0089 croci2..croci7`):
    ///
    ///   1. **LE CROCI C'ERANO.** SOLIDWORKS le mette da se' sui fori quando crea
    ///      la vista (opzione del documento `swDetailingAutoInsertCenterMarksForHoles`,
    ///      accesa nel modello di casa). Si vedono nel PNG.
    ///   2. **`GetCenterMarkCount` NON LE CONTA**: 0 su una vista con tre croci.
    ///      `GetFirstCenterMark`/`GetNext` le vede tutte (`CountCenterMarks`).
    ///   3. **DA CODICE NON NE NASCE NESSUNA.** `AddCenterMark` sul cerchio
    ///      selezionato nella vista faceva salire `GetCenterMarkCount` 0->3 —
    ///      ma su fori che la croce l'avevano gia'; tolte le tre croci
    ///      automatiche, `AddCenterMark` non ne rimette nessuna. Sugli archi dei
    ///      raccordi, niente (4 su 4).
    ///
    /// ── QUINDI E' UN CONTROLLO ──────────────────────────────────────────
    ///
    /// Per ogni foro visibile si guarda se ha la croce (una croce entro mezzo
    /// millimetro dal centro). Se ne manca qualcuna si prova `AddCenterMark`
    /// (qui non nasce, altrove chissa'), e se resta senza si RIFIUTA dicendo la
    /// strada che funziona: l'opzione del documento accesa e la vista rifatta.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome CenterMarks(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: le croci di centro stanno " +
                    "su una vista di disegno. Crea prima la tavola con «create_drawing».");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "La tavola non ha viste: le croci non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «standard_views» o «model_view».");

            string wanted = ViewRequestedByName(drawing, Str(a, "view", null));
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
                return Failed(
                    "Non c'e' nessuna vista «" + wanted + "». Ci sono: " + string.Join(", ", names) +
                    " (o V1..V" + names.Count + ").");

            // SOLO I FORI (cerchi interi). Misurato il 26/9/2026 (`prova0089
            // croci6`): `AddCenterMark` su un ARCO di raccordo selezionato nella
            // vista non fa nascere niente, 4 archi su 4. Le asole hanno due archi
            // e una croce sola in mezzo. Tutt'e due si rifiutano, con la misura.
            string su = (Str(a, "applies_to", "holes") ?? "holes").Trim().ToLowerInvariant();
            bool allWhole = true;
            switch (su)
            {
                case "": case "holes": break;
                case "raccordi":
                case "tutti":
                    return Failed("Sui raccordi la croce da codice non nasce: misurato, AddCenterMark su un arco " +
                                   "selezionato nella vista non mette niente (4 archi su 4). Chiedi «holes»; i centri " +
                                   "dei raccordi, se servono, vanno messi a mano in SOLIDWORKS.");
                case "asole":
                    return Failed("Le asole non le so: un'asola ha due archi e una croce sola in mezzo, e una croce " +
                                   "per arco sarebbe sbagliata. Chiedi «holes».");
                default:
                    return Failed("«su» puo' essere solo «holes», non «" + su + "».");
            }

            // SENZA `size_mm` la dimensione del documento: e' una
            // convenzione di studio, non un numero mio.
            bool itsDimension = a != null && a.ContainsKey("size_mm");
            double markSize = Units.M(Num(a, "size_mm", DefaultCentreMarkSizeMm));
            if (!itsDimension) markSize = DocumentCentreMarkSize(model, markSize);
            if (markSize <= 0) return Failed("«size_mm» deve essere maggiore di zero.");
            bool lines = Bool(a, "extended_lines", false);

            IMathUtility mu = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
            var selMgr = model.SelectionManager as ISelectionMgr;

            var perView = new List<object>();
            int totalBefore = 0, totalAfter = 0, already = 0;
            var notCreated = new List<string>();

            var sheet = drawing.GetFirstView() as IView;      // il primo e' IL FOGLIO
            var view = sheet == null ? null : sheet.GetNextView() as IView;
            while (view != null)
            {
                string itemName = null;
                try { itemName = view.Name; } catch { }
                if (!string.IsNullOrWhiteSpace(wanted) && !SameName(itemName, wanted))
                {
                    view = view.GetNextView() as IView;
                    continue;
                }

                // The walk over the holes of the view is shared with the standard
                // views (Z-C21.13): MarkHolesOfView, below. Only whole circles:
                // the arcs and the slots were refused above.
                var marks = MarkHolesOfView(model, drawing, view, markSize, lines, mu, selMgr);
                int before = marks.Before;
                if (before < 0)
                    return Failed(
                        "Non riesco a contare le croci di centro della vista «" + (itemName ?? "?") +
                        "» prima di cominciare: senza quel numero non saprei dirti quali ho " +
                        "messo io. Non ho toccato niente.");
                foreach (bool notSelectable in marks.LeftWithoutMark)
                    notCreated.Add((itemName ?? "?") + ": un cerchio" + (notSelectable ? " (non si e' lasciato selezionare)" : ""));

                int after = marks.After;
                totalBefore += before;
                totalAfter += after < 0 ? before : after;
                already += marks.AlreadyMarked;
                perView.Add(new Dictionary<string, object>
                {
                    ["view"] = itemName ?? "(senza nome)",
                    ["circles"] = marks.Holes,
                    ["circles_not_holes"] = marks.CirclesThatAreNotHoles,
                    ["marks_before"] = before,
                    ["marks_after"] = NumberOrNull(after),
                    ["marks_added"] = marks.Added,
                    ["circles_already_marked"] = marks.AlreadyMarked,
                });
                view = view.GetNextView() as IView;
            }

            try { model.EditRebuild3(); } catch { }

            int additions = Math.Max(0, totalAfter - totalBefore);
            var measurements = new Dictionary<string, object>
            {
                ["applies_to"] = su,
                ["views_checked"] = perView.Count,
                ["marks_before"] = totalBefore,
                ["marks_after"] = totalAfter,
                ["marks_added"] = additions,
                ["circles_already_marked"] = already,
                ["per_view"] = perView.ToArray(),
                ["size_mm"] = Math.Round(markSize * 1000, 3),
                ["size_source"] = itsDimension ? "requested" : "document",
            };
            if (notCreated.Count > 0)
            {
                measurements["circles_without_mark"] = notCreated.ToArray();
                return Failed(notCreated.Count + (notCreated.Count == 1 ? " foro e'" : " fori sono") +
                               " senza croce di centro (" + string.Join("; ", notCreated) + ") e da codice la croce non " +
                               "nasce (misurato: AddCenterMark non la mette). La mette SOLIDWORKS creando la vista, se " +
                               "l'opzione del documento «Croci di centro sui fori» e' accesa: accendila nel modello di " +
                               "tavola e rifai la vista. Una tavola con qualche foro senza centro sembra finita e non lo e'.");
            }

            // ── ZERO CROCI NON E' UN SUCCESSO SILENZIOSO ────────────────
            if (additions == 0)
            {
                measurements["verdict"] =
                    "NESSUNA CROCE AGGIUNTA. " +
                    (already > 0
                        ? already + (already == 1 ? " cerchio aveva" : " cerchi avevano") + " gia' la croce: niente di nuovo da mettere."
                        : "Su " + perView.Count + (perView.Count == 1 ? " vista" : " viste") +
                          " non c'e' nessun " + (allWhole ? "foro" : "arco") + " visibile: e' la vista sbagliata " +
                          "(i fori si vedono come cerchi dall'alto, di solito V2), oppure il pezzo non ne ha.") +
                    " Il numero e' zero e te lo dico.";
                _log("[agente] " + measurements["verdict"]);
                return SucceededWithoutCreating(measurements);
            }

            measurements["verdict"] =
                "Messe " + additions + (additions == 1 ? " croce di centro" : " croci di centro") +
                " su " + perView.Count + (perView.Count == 1 ? " vista" : " viste") +
                " (" + totalBefore + " -> " + totalAfter + "), una per cerchio, contate sulla tavola.";
            _log("[agente] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The centre mark size when nobody asks for one, in mm: the value of
        /// the operation's own default, used only when the document does not
        /// say its own (<see cref="DocumentCentreMarkSize"/>).
        /// </summary>
        private const double DefaultCentreMarkSizeMm = 2.5;

        /// <summary>
        /// The centre mark size of the document (swDetailingCenterMarkSize), in
        /// metres, or <paramref name="fallback"/> when it cannot be read or is
        /// not positive. It is a house convention of the drawing template, not
        /// a number of the bridge.
        /// </summary>
        private static double DocumentCentreMarkSize(IModelDoc2 model, double fallback)
        {
            try
            {
                double size = model.Extension.GetUserPreferenceDouble(
                    (int)swUserPreferenceDoubleValue_e.swDetailingCenterMarkSize,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
                if (size > 0) return size;
            }
            catch { }
            return fallback;
        }

        /// <summary>What <see cref="MarkHolesOfView"/> found and did on one view.</summary>
        private sealed class HoleCentreMarks
        {
            /// <summary>Marks before anything was touched, enumerated; -1 when they cannot be counted, and then nothing was touched.</summary>
            public int Before = -1;
            /// <summary>Marks at the end, enumerated; -1 when they cannot be counted.</summary>
            public int After = -1;
            // ZERO-E-ZERO: the four counters below count what the walk over the
            // visible edges found; they are read only when Before is not -1, that
            // is when the walk ran, and then a zero is a real zero.
            /// <summary>Whole circles that are holes seen end-on in this view.</summary>
            public int Holes;
            /// <summary>Whole circles that are not holes: the rim of a disc, the end of a shaft.</summary>
            public int CirclesThatAreNotHoles;
            /// <summary>Marks added, each one seen in the recount right after its call.</summary>
            public int Added;
            /// <summary>Holes that already had a mark within half a millimetre of their centre.</summary>
            public int AlreadyMarked;
            /// <summary>One entry per hole left without a mark: true when its edge could not even be selected.</summary>
            public List<bool> LeftWithoutMark = new List<bool>();
        }

        /// <summary>
        /// THE CENTRE MARKS ON THE HOLES OF ONE VIEW, shared by center_marks
        /// and standard_views (Z-C21.13; before, this walk lived inside
        /// CenterMarks).
        ///
        /// For every WHOLE circle of the view that is a hole seen end-on
        /// (<see cref="IsAVisibleHole"/>) and has no mark within half a
        /// millimetre of its centre, the edge is selected in the view and
        /// AddCenterMark is called; a mark counts as added only when the
        /// enumerated count went up by exactly one right after the call. What
        /// the file summary measured still holds: once SOLIDWORKS's own marks
        /// were deleted, AddCenterMark put none back, so a hole left without a
        /// mark is reported (<see cref="HoleCentreMarks.LeftWithoutMark"/>) and
        /// each caller says it in its own words.
        ///
        /// When the marks cannot be counted first, nothing is touched and
        /// Before is -1.
        /// </summary>
        private static HoleCentreMarks MarkHolesOfView(
            IModelDoc2 model, IDrawingDoc drawing, IView view,
            double size, bool extendedLines, IMathUtility mu, ISelectionMgr selMgr)
        {
            var marks = new HoleCentreMarks();
            marks.Before = HowManyCenterMarks(view);
            if (marks.Before < 0) return marks;

            MathTransform toSheet = null;
            try { toSheet = view.ModelToViewTransform; } catch { }
            var knownCentres = CenterMarkCenters(view);
            var leftWithoutMark = new List<bool>();
            int count = marks.Before;
            foreach (var o in (view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]) ?? new object[0])
            {
                var e = o as IEdge;
                ICurve c = null;
                try { c = e?.GetCurve() as ICurve; } catch { }
                if (c == null || !c.IsCircle()) continue;
                bool whole = false;
                try { whole = e.GetStartVertex() == null; } catch { }
                // Only whole circles: on the arc of a fillet no mark is born from
                // code (measured on 26/9/2026, 4 arcs of 4).
                if (!whole) continue;
                // A REAL HOLE (review of 26/9/2026): the circle faces the reader
                // (its axis, taken onto the sheet, is normal to the sheet) and is an
                // INNER loop of a planar face. The rim of a disc or the end of a
                // shaft is a whole circle but not a hole: they used to become
                // "holes without a mark" and a right part was refused.
                if (!IsAVisibleHole(e, c, mu, toSheet)) { marks.CirclesThatAreNotHoles++; continue; }
                marks.Holes++;

                // The centre on the sheet, so that one hole never gets two marks.
                double[] centre = null;
                try
                {
                    var cp = c.CircleParams as double[];
                    if (cp != null && mu != null && toSheet != null)
                        centre = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { cp[0], cp[1], cp[2] })).MultiplyTransform(toSheet)).ArrayData as double[];
                }
                catch { }
                if (centre != null && knownCentres.Exists(q => SameCenter(view, q, centre)))
                { marks.AlreadyMarked++; continue; }

                bool selected = false;
                try
                {
                    model.ClearSelection2(true);
                    var sd = selMgr?.CreateSelectData() as SelectData;
                    if (sd != null) { sd.View = (View)view; selected = (e as IEntity).Select4(false, sd); }
                }
                catch { }
                if (selected)
                    // OBSOLETE-MEASURED: center_marks_on_the_holes — IDrawingDoc.AddCenterMark is
                    // obsolete; its replacements InsertCenterMark2/3 answer True and add nothing
                    // (probes of 17/9 and 26/9/2026, see the summary of this file).
                    try { drawing.AddCenterMark(size, extendedLines); } catch { }
                model.ClearSelection2(true);
                int now = HowManyCenterMarks(view);
                if (now == count + 1)
                {
                    marks.Added++;
                    count = now;
                    if (centre != null) knownCentres.Add(RelativeToView(view, centre));
                }
                else
                {
                    leftWithoutMark.Add(!selected);
                    if (now >= 0) count = now;
                }
            }
            marks.LeftWithoutMark = leftWithoutMark;
            marks.After = HowManyCenterMarks(view);
            return marks;
        }

        /// <summary>
        /// Il cerchio e' un foro che in questa vista si vede come cerchio: asse
        /// perpendicolare al foglio, e anello interno di una faccia piana.
        /// </summary>
        private static bool IsAVisibleHole(IEdge e, ICurve c, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var cp = c.CircleParams as double[];
                if (cp == null || cp.Length < 6 || mu == null || tr == null) return false;
                var axis = ((IMathVector)((IMathVector)mu.CreateVector(new[] { cp[3], cp[4], cp[5] })).MultiplyTransform(tr)).ArrayData as double[];
                double l = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
                if (l < 1e-12 || Math.Abs(axis[2]) / l < 0.99) return false;
                // IL CERCHIO E' UN ANELLO INTERNO DI UNA FACCIA PIANA: il bordo di un
                // foro sta dentro la faccia che attraversa; il bordo di un disco o la
                // testa di un albero e' l'anello ESTERNO della sua faccia.
                // Misurato il 26/9/2026 (\`prova0089 croci8\`): FaceInSurfaceSense
                // NON distingue — foro vero, disco falso — perche' il verso della
                // superficie dipende da come e' nata; l'anello no.
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var ring = (oc as ICoEdge)?.GetLoop() as ILoop2;
                    var f = ring?.GetFace() as IFace2;
                    var s = f?.GetSurface() as ISurface;
                    if (s == null || !s.IsPlane()) continue;
                    if (!ring.IsOuter()) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// I centri delle croci che la vista ha gia', come li dice
        /// `ICenterMark.GetPosition(0)`: RISPETTO AL CENTRO DELLA VISTA, non sul
        /// foglio. Misurato il 26/9/2026 (`prova0089 croci4`): un cerchio a
        /// 54,5/110,69 sul foglio, in una vista centrata a 94,5/110,69, ha la
        /// croce a -40/0. Il primo codice li confrontava col foglio, e la seconda
        /// chiamata metteva tre croci doppie.
        /// </summary>
        private static List<double[]> CenterMarkCenters(IView view)
        {
            var outside = new List<double[]>();
            try
            {
                var cm = view.GetFirstCenterMark() as ICenterMark;
                while (cm != null)
                {
                    try
                    {
                        var p = cm.GetPosition(0) as double[];
                        if (p != null && p.Length >= 2) outside.Add(new[] { p[0], p[1] });
                    }
                    catch { }
                    cm = cm.GetNext() as ICenterMark;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>Un punto del foglio portato nel riferimento della vista (come GetPosition).</summary>
        private static double[] RelativeToView(IView view, double[] sheet)
        {
            double[] p = null;
            try { p = view.Position as double[]; } catch { }
            if (p == null || p.Length < 2) return new[] { sheet[0], sheet[1] };
            return new[] { sheet[0] - p[0], sheet[1] - p[1] };
        }

        /// <summary>
        /// La croce `q` (riferimento della vista) sta sul centro `sheet`? Si prova
        /// con e senza la scala: a scala 1 non si e' potuto dire se GetPosition
        /// la applichi, e mezzo millimetro di tolleranza vale nelle due letture.
        /// </summary>
        private static bool SameCenter(IView view, double[] q, double[] sheet)
        {
            var r = RelativeToView(view, sheet);
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;
            if (Math.Abs(q[0] - r[0]) < 0.0005 && Math.Abs(q[1] - r[1]) < 0.0005) return true;
            return Math.Abs(q[0] - r[0] / scale) < 0.0005 && Math.Abs(q[1] - r[1] / scale) < 0.0005;
        }

        /// <summary>
        /// Quante croci di centro ha questa vista, o **-1 se non si e' potuto
        /// leggere** (voce Z-C8).
        ///
        /// Tornava zero, e il commento diceva che era «il verso prudente». Non
        /// lo era: se falliva la lettura di PRIMA, qualunque croce dopo
        /// sembrava nuova e il messaggio diceva «messe N croci» anche su una
        /// vista che le aveva gia'; se falliva quella di DOPO, il ciclo provava
        /// tutti e tre gli stili (croci doppie) e poi diceva «quella vista non
        /// ha fori». Un numero che non si e' letto non si confronta.
        /// </summary>
        private static int HowManyCenterMarks(IView view)
        {
            return CountCenterMarks.OfView(view);
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static bool KnownViewName(List<string> names, string wanted)
        {
            foreach (var n in names) if (SameName(n, wanted)) return true;
            return false;
        }
    }

    /// <summary>
    /// LE CROCI DI CENTRO SI CONTANO ENUMERANDOLE (26/9/2026, `prova0089 croci5`).
    ///
    /// `IView.GetCenterMarkCount()` rispondeva **0** su una vista con tre croci:
    /// quelle che SOLIDWORKS mette da se' sui fori quando crea la vista (opzione
    /// del documento `swDetailingAutoInsertCenterMarksForHoles`, accesa qui) non
    /// le conta; conta solo quelle aggiunte dopo. `GetFirstCenterMark` /
    /// `GetNext` le vede tutte. Il «blocco» di W2.11 del 17/9 — «l'API dice si'
    /// e non mette niente» — era in gran parte questo contatore: le croci
    /// c'erano, e nessuno le vedeva. Lo usano l'operazione e lo stato.
    /// </summary>
    internal static class CountCenterMarks
    {
        /// <summary>Quante croci di centro ha la vista, enumerate; -1 se non si legge.</summary>
        internal static int OfView(IView view)
        {
            try
            {
                int n = 0;
                var cm = view.GetFirstCenterMark() as ICenterMark;
                while (cm != null && n < 10000)
                {
                    n++;
                    cm = cm.GetNext() as ICenterMark;
                }
                return n;
            }
            catch { return -1; }
        }
    }
}
