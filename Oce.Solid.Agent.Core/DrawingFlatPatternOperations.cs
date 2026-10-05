using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
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
    public partial class SwOperations
    {
        private Outcome FlatPatternView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("Il documento attivo non e' una tavola: la vista dello sviluppo si mette in una " +
                               "tavola. Creala con «create_drawing».");

            string modelPath = Str(a, "model", null);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                string why;
                modelPath = ModelToPutInView(out why);
                if (modelPath == null) return Failed(why);
            }
            var swPath = CanonicalPath(modelPath);
            if (swPath == null)
                return Failed("«" + modelPath + "» non risulta aperto: la vista nascerebbe vuota.");
            // A part loaded hidden (no window of its own) answers here as well:
            // the C4-4 probe of 28/9/2026 read, on the object GetOpenDocumentByName
            // gave for a hidden part, the same IPartDoc, configuration, features,
            // volume and bodies as on the visible one (the guide's «opened in its
            // own window» did not matter).
            var part = _sw.GetOpenDocumentByName(swPath) as IModelDoc2;
            if (!(part is IPartDoc))
                return Failed("«" + swPath + "» non e' una parte: lo sviluppo e' di una parte di lamiera.");
            if (SheetMetalData(part) == null)
                return Failed("«" + System.IO.Path.GetFileName(swPath) + "» non e' di lamiera (non ha la " +
                               "lavorazione «SheetMetal»): non c'e' uno sviluppo da mettere in tavola.");

            string configuration = Str(a, "configuration", null);
            if (string.IsNullOrWhiteSpace(configuration))
                try { configuration = (part.GetActiveConfiguration() as IConfiguration)?.Name ?? ""; } catch { configuration = ""; }
            double x = Units.M(Num(a, "x_mm", 300));
            double y = Units.M(Num(a, "y_mm", 90));
            bool hideBends = Bool(a, "hide_bends", false);

            // ── LO SVILUPPO DEL PEZZO, MISURATO PRIMA ────────────────────────
            // Z-C21.8, finding M24: a flat pattern that did not unsuppress, or
            // did not suppress again, stops the step here, before the drawing is
            // touched; the part may be left flattened, and the refusal says so.
            double alongPart = FlattenedLength(part, out double _, out string flatPatternProblem);
            if (flatPatternProblem != null)
                return Failed("Before placing the view, I measured the flat length of the part and " +
                               flatPatternProblem + ". I did not place the view.");

            int before = CountViews(drawing);
            object raw = null;
            try
            {
                raw = drawing.CreateFlatPatternViewFromModelView3(
                    swPath, configuration ?? "", x, y, 0, hideBends, false);
            }
            catch (Exception ex) { return Failed("La vista dello sviluppo non e' nata: " + ex.Message); }
            var view = raw as IView;
            if (view == null)
                return Failed("SOLIDWORKS non ha creato la vista dello sviluppo e non ha detto perche' " +
                               "(configurazione «" + configuration + "»).");
            string itemName = "";
            try { itemName = view.GetName2() ?? ""; } catch { }

            bool flat = false;
            try { flat = view.IsFlatPatternView(); } catch { }
            // THE ANSWER OF RemoveNewView IS READ at every refusal below (Z-C21.8,
            // finding M40): «I removed it» only when the view is no longer in the
            // drawing, which RemoveNewView checks by reading the views again. Until
            // library 0.144.0 four of these five refusals said removed whatever
            // the removal did.
            if (!flat)
            {
                bool removedNotFlat = RemoveNewView(model, itemName);
                return Failed("The view «" + itemName + "» was born but is not a flat pattern (IsFlatPatternView false)" +
                               ViewRemovedOrNot(removedNotFlat));
            }
            int bends = -1;
            try { bends = view.GetBendLineCount(); } catch { }

            // ── UN POSTO LIBERO (visto nel PNG, 26/9/2026) ───────────────────
            //
            // Messa a 300/90 mm a scala 2:1, la L distesa scendeva dentro il
            // cartiglio, col banco verde. Si misura il contorno della vista (con
            // la nota di piega: e' quello che occupa il foglio) e la si sposta in
            // un posto libero, se «x_mm»/«y_mm» non l'hanno fissata.
            string location, placeReason;
            if (!FlatPatternInItsPlace(drawing, view, a.ContainsKey("x_mm") || a.ContainsKey("y_mm"), out location, out placeReason))
            {
                bool removedNoPlace = RemoveNewView(model, itemName);
                return Failed(placeReason + (removedNoPlace
                    ? " I removed the view."
                    : " Remove the view — " + Sentences.HowToWithdrawIt + "."));
            }

            // ── L'INGOMBRO DAGLI SPIGOLI VISIBILI ────────────────────────────
            double[] boundingBox = BoundingBoxFromEdges(view);
            var measurements = new Dictionary<string, object>
            {
                ["view"] = itemName,
                ["model"] = swPath,
                ["configuration"] = configuration,
                ["views_before"] = before,
                ["views_after"] = CountViews(drawing),
                ["flat"] = true,
                ["bend_lines"] = NumberOrNull(bends),
                ["bounding_box_mm"] = boundingBox == null ? null : new object[] { boundingBox[0], boundingBox[1] },
                ["flat_pattern_length_mm"] = double.IsNaN(alongPart) ? null : (object)Math.Round(alongPart, 3),
            };
            try { measurements["scale"] = view.ScaleDecimal; } catch { }
            measurements["view_location"] = location;
            try
            {
                var o = view.GetOutline() as double[];
                if (o != null) measurements["contour_view_mm"] = new object[] { Math.Round(o[0] * 1000, 1), Math.Round(o[1] * 1000, 1), Math.Round(o[2] * 1000, 1), Math.Round(o[3] * 1000, 1) };
            }
            catch { }
            measurements["inside_sheet"] = true;
            if (boundingBox == null)
            {
                bool removedUnmeasured = RemoveNewView(model, itemName);
                return Failed("The flat-pattern view was born but I cannot measure its edges: without a measure " +
                               "I cannot tell whether it is the right flat pattern" + ViewRemovedOrNot(removedUnmeasured));
            }
            if (!double.IsNaN(alongPart) && Math.Abs(boundingBox[0] - alongPart) > 0.1)
            {
                bool removedOther = RemoveNewView(model, itemName);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The flat-pattern view measures {0:0.###} mm on its long side and the flat pattern of the part " +
                    "{1:0.###} mm: it is not the flat pattern of this part (or the part is not flattened)",
                    boundingBox[0], alongPart) + ViewRemovedOrNot(removedOther));
            }
            if (double.IsNaN(alongPart))
            {
                // SENZA LO SVILUPPO DEL PEZZO NON C'E' RISCONTRO (revisione del
                // 26/9/2026): la vista potrebbe essere di un'altra cosa.
                bool removed = RemoveNewView(model, itemName);
                return Failed("Non riesco a misurare lo sviluppo del pezzo (la lavorazione «FlatPattern» non si lascia " +
                               "misurare), e senza non so dire se la vista mostra lo sviluppo giusto. " +
                               (removed ? "Ho tolto la vista." : "Togli la vista: " + Sentences.HowToWithdrawIt + "."));
            }
            measurements["matches_with_part"] = true;
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// L'ingombro della vista in mm, dal suo disegno vero: gli spigoli
        /// visibili, campionati, portati sul foglio e divisi per la scala.
        /// [lato lungo, lato corto], o null se non si legge niente.
        /// </summary>
        private double[] BoundingBoxFromEdges(IView view)
        {
            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) return null;
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;
            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            if (edges == null || edges.Length == 0) return null;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            int points = 0;
            foreach (var o in edges)
            {
                var e = o as IEdge;
                if (e == null) continue;
                var samples = new List<double[]>();
                try
                {
                    var cp = ApiHelpers.EdgeParameters(e, out ICurve c);
                    if (cp != null && c != null)
                    {
                        var tess = c.GetTessPts(1e-5, 1e-3, cp.StartPoint, cp.EndPoint) as double[];
                        if (tess != null)
                            for (int i = 0; i + 2 < tess.Length; i += 3)
                                samples.Add(new[] { tess[i], tess[i + 1], tess[i + 2] });
                        var s = cp.StartPoint as double[];
                        var f = cp.EndPoint as double[];
                        if (s != null) samples.Add(s);
                        if (f != null) samples.Add(f);
                    }
                }
                catch { }
                foreach (var p in samples)
                {
                    double[] q;
                    try { q = ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[]; }
                    catch { continue; }
                    if (q == null) continue;
                    x0 = Math.Min(x0, q[0]); x1 = Math.Max(x1, q[0]);
                    y0 = Math.Min(y0, q[1]); y1 = Math.Max(y1, q[1]);
                    points++;
                }
            }
            if (points < 2) return null;
            double w = (x1 - x0) / scale * 1000.0, h = (y1 - y0) / scale * 1000.0;
            return new[] { Math.Round(Math.Max(w, h), 3), Math.Round(Math.Min(w, h), 3) };
        }

        /// <summary>
        /// Porta la vista in un posto libero del foglio (OperazioniPostoSulFoglio),
        /// o controlla che quello chiesto lo sia. False col motivo.
        /// </summary>
        private static bool FlatPatternInItsPlace(IDrawingDoc drawing, IView view, bool requested, out string location, out string reason)
        {
            location = null; reason = null;
            var o = view.GetOutline() as double[];
            var p = view.Position as double[];
            if (o == null || o.Length < 4 || p == null || p.Length < 2)
            {
                reason = "La vista dello sviluppo e' nata ma non riesco a leggere dove sta sul foglio.";
                return false;
            }
            double w = o[2] - o[0], h = o[3] - o[1];
            var sheet = ReadSheet(drawing, view);
            if (requested)
            {
                string why = sheet.Touches(o[0], o[1], o[2], o[3]);
                if (why != null)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "La vista dello sviluppo ({0:0} x {1:0} mm sul foglio) dove l'hai chiesta {2}. Togli «x_mm» e " +
                        "«y_mm» e la metto io in un posto libero.", w * 1000, h * 1000, why);
                    return false;
                }
                location = "dove chiesto";
                return true;
            }
            var down = FreePlace(sheet, w, h, out location);
            if (down == null)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "Sul foglio non c'e' un posto libero per la vista dello sviluppo ({0:0} x {1:0} mm) fuori dalle altre " +
                    "viste e dal cartiglio: usa un foglio piu' grande o un'altra scala.", w * 1000, h * 1000);
                return false;
            }
            // La posizione della vista non e' per forza il centro del contorno:
            // si sposta del tanto che serve all'angolo in basso a sinistra.
            try { view.Position = new[] { p[0] + down[0] - o[0], p[1] + down[1] - o[1] }; } catch { }
            // GetOutline IS READ RIGHT AFTER Position, with no rebuild, and that is
            // measured (Z-C21.3, finding L14-1, NOT REAL). IView~Position.html
            // asks for IModelDoc2::EditRebuild3 «to see these changes», which is
            // about the graphics. The Z-C21.3 probe of L14-1 (28/9/2026), a free
            // named view moved by (20, 10) mm: the outline read at once had moved
            // by exactly (20, 10) at both corners, and EditRebuild3 or
            // GraphicsRedraw2 in between changed nothing. The flat-pattern view is
            // a free view like that one. A projected view ALIGNED to its parent is
            // not: asked (20, 20), it moved (0, 20) (same probe). The check below
            // judges the outline where the view actually is, so even then it
            // would judge the place it landed in, not the one asked.
            var o2 = view.GetOutline() as double[];
            string anchor = o2 == null ? "non si legge" : sheet.Touches(o2[0], o2[1], o2[2], o2[3]);
            if (anchor != null)
            {
                reason = "Ho spostato la vista dello sviluppo " + location + " ma " + anchor + ".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The end of a refusal that removed the new view, or tried to (M40):
        /// «removed» only when RemoveNewView found it gone on a reread.
        /// </summary>
        private static string ViewRemovedOrNot(bool removed)
        {
            return removed ? ": I removed it." : ": I could not remove it, remove it — " + Sentences.HowToWithdrawIt + ".";
        }

        /// <summary>
        /// Toglie una vista appena messa, per nome. `DeleteSelection2` e non
        /// `EditDelete`: la seconda puo' chiedere conferma con una finestra, e
        /// una chiamata COM che aspetta una finestra non torna (vedi
        /// SwOperations, «DeleteSelection2 E NON EditDelete»).
        /// </summary>
        private static bool RemoveNewView(IModelDoc2 drawing, string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            try
            {
                drawing.ClearSelection2(true);
                if (drawing.Extension.SelectByID2(itemName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                    drawing.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                drawing.ClearSelection2(true);
                // TOLTA SI RILEGGE: la vista con quel nome non deve esserci piu'.
                var v = (drawing as IDrawingDoc)?.GetFirstView() as IView;
                while (v != null)
                {
                    if (string.Equals(v.GetName2(), itemName, StringComparison.Ordinal)) return false;
                    v = v.GetNextView() as IView;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
