using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// SURFACE FINISH, DATUM AND GEOMETRIC TOLERANCES IN THE DRAWING (entries GT6 and GT8).
    ///
    /// ── MEASURED AND LOOKED AT BEFORE WRITING THEM, 26/9/2026 ────────────────
    ///
    /// `prova0089 annotazioni` and `annotazioni2`, drawing of the 120x80x10 plate:
    ///
    ///  - `IModelDocExtension.InsertSurfaceFinishSymbol3` returns the object and the Ra
    ///    can be read back (`GetText(swSFSymbolMaximumRoughness)` = «3.2»). BUT THE
    ///    COORDINATES PASSED ARE IGNORED: with nothing else the symbol ended up
    ///    in the corner of the sheet, outside the frame — seen in the PNG, no check
    ///    said so.
    ///  - `IModelDoc2.InsertDatumTag2()` (zero arguments) works on the edge
    ///    SELECTED in the view, and does not open windows. Left where it was, the
    ///    little flag was on the opposite side of the edge and the line
    ///    crossed the part.
    ///  - `IModelDoc2.InsertGtol()` + `SetFrameSymbols` (codes of
    ///    `swGtolGeomCharSymbol_e`) + `SetFrameValues`: the frame is born and can be read
    ///    back (`GetFrameSymbols` returns `Int16[]`, `GetFrameValues` the strings).
    ///    Without a selection it is free; with the edge selected it has the arrow.
    ///
    /// The cure, seen in the PNG: the border edge of the side asked for is SELECTED
    /// in the view, it is inserted, and then the annotation is MOVED outside the part
    /// with `IAnnotation.SetPosition2`, on the side of that side. The datum has the
    /// triangle on the edge and the flag outside; the surface finish and the frame
    /// have the leader line on the edge.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class ViewSide
        {
            public IEdge Edge;
            public double[] Midpoint;      // on the sheet, metres
            public double[] Outside;      // direction outward from the part
            public double[] Along;      // direction along the edge
        }

        /// <summary>
        /// The border edge of the view on the side asked for (top, bottom, left,
        /// right): among the straight horizontal or vertical edges, the one furthest
        /// out on that side. Null with the reason.
        /// </summary>
        private ViewSide SideEdge(IView view, string side, out string reason)
        {
            reason = null;
            double fx = 0, fy = 0;
            switch (side)
            {
                case "top": fy = 1; break;
                case "bottom": fy = -1; break;
                case "left": fx = -1; break;
                case "right": fx = 1; break;
                default:
                    reason = "«side» e' uno fra alto, basso, sinistra, destra, non «" + side + "».";
                    return null;
            }
            IMathUtility mu = null; MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) { reason = "Non riesco a leggere come la vista porta il modello sul foglio."; return null; }
            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            ViewSide better = null; double score = double.MinValue;
            foreach (var o in edges ?? new object[0])
            {
                var e = o as IEdge;
                try
                {
                    var p = ApiHelpers.EdgeParameters(e, out ICurve curva);
                    if (curva == null || !curva.IsLine()) continue;
                    var a = ((IMathPoint)((IMathPoint)mu.CreatePoint(p.StartPoint)).MultiplyTransform(tr)).ArrayData as double[];
                    var b = ((IMathPoint)((IMathPoint)mu.CreatePoint(p.EndPoint)).MultiplyTransform(tr)).ArrayData as double[];
                    bool horizontal = Math.Abs(a[1] - b[1]) < 1e-6, vertical = Math.Abs(a[0] - b[0]) < 1e-6;
                    if (fy != 0 && !horizontal || fx != 0 && !vertical) continue;
                    if (Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) < 1e-4) continue;   // too short to attach anything to
                    double s = (a[0] + b[0]) / 2 * fx + (a[1] + b[1]) / 2 * fy;
                    if (s > score + 1e-9)
                    {
                        score = s;
                        better = new ViewSide
                        {
                            Edge = e,
                            Midpoint = new[] { (a[0] + b[0]) / 2, (a[1] + b[1]) / 2 },
                            Outside = new[] { fx, fy },
                            Along = new[] { Math.Abs(fy), Math.Abs(fx) },
                        };
                    }
                }
                catch { }
            }
            if (better == null)
                reason = "La vista non mostra nessuno spigolo dritto sul lato «" + side + "» a cui attaccare l'annotazione.";
            return better;
        }

        /// <summary>
        /// How much is already occupied, in mm from the edge outward, by the
        /// annotations of the view that are on that side and at the height of the
        /// edge (the dimensions put by `drawing_dimension`, the datums, the frames). Zero
        /// if there is nothing.
        ///
        /// BORN LOOKING AT THE PNG (26/9/2026): the datum flag 12 mm below
        /// the edge fell ON the line of the dimension of the 120, and the frame 15 mm to
        /// the right on the text of the dimension 10. An annotation goes beyond the band
        /// that is there already.
        /// </summary>
        private static double OccupiedBand(IView view, ViewSide side)
        {
            double maxValue = 0;   // ZERO-E-ZERO: no annotation on that side
            try
            {
                double half = 0;
                try
                {
                    var p = ApiHelpers.EdgeParameters(side.Edge);
                    var s = p.StartPoint as double[]; var e = p.EndPoint as double[];
                    double scale = 1; try { scale = view.ScaleDecimal; } catch { }
                    half = Math.Sqrt(Math.Pow(e[0] - s[0], 2) + Math.Pow(e[1] - s[1], 2) + Math.Pow(e[2] - s[2], 2)) * (scale > 0 ? scale : 1) / 2;
                }
                catch { }
                var an = view.GetFirstAnnotation3() as IAnnotation;
                while (an != null)
                {
                    var q = an.GetPosition() as double[];
                    if (q != null && q.Length >= 2)
                    {
                        double dx = q[0] - side.Midpoint[0], dy = q[1] - side.Midpoint[1];
                        double outside = dx * side.Outside[0] + dy * side.Outside[1];
                        double along = Math.Abs(dx * side.Along[0] + dy * side.Along[1]);
                        if (outside > 0 && along <= half + 0.03) maxValue = Math.Max(maxValue, outside);
                    }
                    an = an.GetNext3() as IAnnotation;
                }
            }
            catch { }
            return maxValue * 1000.0;
        }

        /// <summary>The view, resolved like the other drawing operations; null with the reason.</summary>
        private IView ViewToAnnotate(IModelDoc2 model, Dictionary<string, object> a, out IDrawingDoc drawing, out string reason)
        {
            reason = null;
            drawing = model as IDrawingDoc;
            if (drawing == null)
            {
                reason = "Il documento attivo non e' una tavola: le annotazioni stanno su una vista. Crea prima la " +
                         "tavola con «create_drawing».";
                return null;
            }
            var names = ViewNames(drawing);
            if (names.Count == 0)
            {
                reason = "La tavola non ha viste: metti prima le viste con «standard_views».";
                return null;
            }
            var view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason == null && view == null) reason = "Non riesco a prendere la vista.";
            return view;
        }

        /// <summary>Selects the edge in the view (the selection that the three APIs use).</summary>
        private static bool SelectInView(IModelDoc2 model, IView view, IEdge edge)
        {
            try
            {
                model.ClearSelection2(true);
                var sel = model.SelectionManager as ISelectionMgr;
                var sd = sel?.CreateSelectData() as SelectData;
                if (sd == null) return false;
                sd.View = (View)view;
                return (edge as IEntity).Select4(false, sd);
            }
            catch { return false; }
        }

        /// <summary>How many annotations of that type the drawing has, view by view; -1 if it cannot be read.</summary>
        private static int DrawingAnnotations(IDrawingDoc drawing, string typ)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    n += typ == "surface_finish" ? v.GetSFSymbolCount() : typ == "datum" ? v.GetDatumTagCount() : v.GetGTolCount();
                    v = v.GetNextView() as IView;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Removes an annotation this operation has just placed, and says it is
        /// gone only when a recount agrees (Z-C21.8, finding M38). The deletion
        /// is DeleteSelection2, not EditDelete (the second can open a
        /// confirmation window, and a COM call waiting on a window does not
        /// come back). Its answer alone is not enough: true is claimed only
        /// when <paramref name="count"/>, read again, is back to
        /// <paramref name="countBefore"/>, the count taken before the
        /// annotation was inserted. With either count unreadable the answer is
        /// false, and the caller asks the user to remove it.
        /// </summary>
        private static bool RemoveNewAnnotation(IModelDoc2 model, IAnnotation annotation, Func<int> count, int countBefore)
        {
            if (annotation == null || countBefore < 0) return false;
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (annotation.Select3(false, null))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            if (!deleted) return false;
            int now = count();
            return now >= 0 && now == countBefore;
        }

        /// <summary>
        /// What the diameter flag read back from a tolerance frame says is
        /// wrong, or null (Z-C21.8, finding M38). <paramref name="read"/> is
        /// the first value of IGtol.GetFrameDiameterSymbols: 1 for a Ø in front
        /// of the first tolerance value, 0 for none, -1 when it could not be
        /// read. An unreadable flag is wrong only when the Ø was asked: that is
        /// the claim the answer would make (a cylindrical zone) without having
        /// seen it.
        /// </summary>
        internal static string GtolDiameterWrong(bool asked, int read)
        {
            if (read < 0)
                return asked ? "the Ø in front of the value cannot be read back (IGtol.GetFrameDiameterSymbols)" : null;
            if ((read == 1) != asked)
                return asked ? "the value has no Ø in front, and it was asked" : "the value has a Ø in front, and it was not asked";
            return null;
        }

        /// <summary>The datum letters already in the drawing.</summary>
        private static List<string> DatumLetters(IDrawingDoc drawing)
        {
            var outside = new List<string>();
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    var d = v.GetFirstDatumTag() as IDatumTag;
                    while (d != null)
                    {
                        string l = null;
                        try { l = d.GetLabel(); } catch { }
                        if (!string.IsNullOrWhiteSpace(l)) outside.Add(l.Trim().ToUpperInvariant());
                        d = d.GetNext() as IDatumTag;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// WHERE TO PUT AN ANNOTATION, among a few places in order: outside the
        /// edge at its height; then beyond the corners of the view (above and
        /// below for a vertical edge, right and left for a
        /// horizontal one). The first is taken whose estimated extent does not fall
        /// inside ANOTHER view of the sheet (with 5 mm of margin) and lies within the sheet.
        ///
        /// BORN LOOKING AT THE PNG (26/9/2026): the frame put beyond the dimension 10,
        /// to the right of the front view, fell over the border of the
        /// side view — there is little room between the two. The extent is estimated
        /// (width and height given by the caller), not read: the annotation does not
        /// say how big it is.
        /// </summary>
        private static double[] ChoosePosition(IDrawingDoc drawing, IView view, ViewSide side,
                                            double outsideMm, double alongMm, double widthMm, double heightMm)
        {
            var other = new List<double[]>();
            double[] ownOne = null;
            try
            {
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    var o = v.GetOutline() as double[];
                    if (o != null && o.Length >= 4)
                    {
                        if (ReferenceEquals(v, view) || v.GetName2() == view.GetName2()) ownOne = o;
                        else other.Add(o);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            double f = outsideMm / 1000.0, l = alongMm / 1000.0, w = widthMm / 1000.0, h = heightMm / 1000.0;
            var candidates = new List<double[]>
            {
                new[] { side.Midpoint[0] + side.Outside[0] * f + side.Along[0] * l, side.Midpoint[1] + side.Outside[1] * f + side.Along[1] * l },
            };
            if (ownOne != null)
            {
                double margin = 0.012 + h;
                if (side.Outside[0] != 0)   // vertical edge: above and below the view, on the side of the edge
                {
                    double x = side.Midpoint[0] + side.Outside[0] * 0.008;
                    candidates.Add(new[] { x, ownOne[3] + margin });
                    candidates.Add(new[] { x, ownOne[1] - margin });
                }
                else                      // horizontal edge: beyond the corners, on the side of the edge
                {
                    double y = side.Midpoint[1] + side.Outside[1] * f;
                    candidates.Add(new[] { ownOne[2] + 0.012, y });
                    candidates.Add(new[] { ownOne[0] - 0.012 - w, y });
                }
            }
            foreach (var c in candidates)
            {
                double x0 = c[0], x1 = c[0] + w, y0 = c[1] - h / 2, y1 = c[1] + h / 2;
                bool free = true;
                foreach (var o in other)
                    if (x1 > o[0] - 0.005 && x0 < o[2] + 0.005 && y1 > o[1] - 0.005 && y0 < o[3] + 0.005) { free = false; break; }
                if (free) return c;
            }
            return candidates[0];
        }

        /// <summary>
        /// Moves an annotation to the place chosen by `ChoosePosition`. Returns the
        /// position read back in mm, or null.
        /// </summary>
        private static double[] Move(IAnnotation an, IDrawingDoc drawing, IView view, ViewSide side,
                                       double outsideMm, double alongMm, double widthMm, double heightMm)
        {
            if (an == null) return null;
            try
            {
                var place = ChoosePosition(drawing, view, side, outsideMm, alongMm, widthMm, heightMm);
                double x = place[0], y = place[1];
                an.SetPosition2(x, y, 0);
                var p = an.GetPosition() as double[];
                return p == null ? null : new[] { Math.Round(p[0] * 1000, 2), Math.Round(p[1] * 1000, 2) };
            }
            catch { return null; }
        }

        // ──────────────────────────────────────────────────────────────────
        // surface_finish
        // ──────────────────────────────────────────────────────────────────
        private Outcome SurfaceFinish(IModelDoc2 model, Dictionary<string, object> a)
        {
            string reason;
            IDrawingDoc drawing;
            var view = ViewToAnnotate(model, a, out drawing, out reason);
            if (view == null) return Failed(reason);
            double ra;
            if (!RequiredNum(a, "ra_um", out ra, out reason)) return Failed(reason);
            if (ra <= 0 || ra > 100)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«ra_um» e' la rugosita' Ra in micrometri, fra 0 e 100: {0:0.###} non lo e'.", ra));
            string featureItem = Str(a, "process", "material_removal");
            int typ = featureItem == "material_removal" ? (int)swSFSymType_e.swSFMachining_Req
                     : featureItem == "any" ? (int)swSFSymType_e.swSFBasic
                     : featureItem == "no_removal" ? (int)swSFSymType_e.swSFDont_Machine : -1;
            if (typ < 0)
                return Failed("«process» e' asportazione, qualsiasi o no_removal, non «" + featureItem + "».");
            var side = SideEdge(view, Str(a, "side", "top"), out reason);
            if (side == null) return Failed(reason);

            string textRa = ra.ToString("0.###", CultureInfo.InvariantCulture);
            double occupied = OccupiedBand(view, side);
            int before = DrawingAnnotations(drawing, "surface_finish");
            if (!SelectInView(model, view, side.Edge))
                return Failed("Non riesco a selezionare nella vista lo spigolo del lato «" + Str(a, "side", "top") + "».");
            object raw;
            try
            {
                raw = model.Extension.InsertSurfaceFinishSymbol3(typ, (int)swLeaderStyle_e.swSTRAIGHT,
                    side.Midpoint[0], side.Midpoint[1], 0, (int)swSFLaySym_e.swSFNone, (int)swArrowStyle_e.swCLOSED_ARROWHEAD,
                    "", "", "", "", textRa, "", "");
            }
            catch (Exception ex) { return Failed("Il simbolo di rugosita' non e' nato: " + ex.Message); }
            model.ClearSelection2(true);
            var symbol = raw as ISFSymbol;
            if (symbol == null) return Failed("SOLIDWORKS non ha creato il simbolo di rugosita' e non ha detto perche'.");
            int after = DrawingAnnotations(drawing, "surface_finish");
            string read = null;
            try { read = symbol.GetText((int)swSurfaceFinishSymbolText_e.swSFSymbolMaximumRoughness); } catch { }
            var an = symbol.GetAnnotation() as IAnnotation;
            // OUTSIDE THE PART: the symbol, left where SOLIDWORKS puts it, is not
            // where it is asked for (measured). 12 mm outward, 20 along the edge.
            var pos = Move(an, drawing, view, side, Math.Max(12, occupied + 10), 20, 16, 12);
            int attached = -1;
            try { attached = an.GetAttachedEntityCount3(); } catch { }

            if (!string.Equals((read ?? "").Trim(), textRa, StringComparison.Ordinal))
                return Failed("Il simbolo e' nato ma dice Ra «" + read + "» invece di «" + textRa + "»: toglilo — " +
                               Sentences.HowToWithdrawIt + ".");
            var measurements = new Dictionary<string, object>
            {
                ["ra_um"] = textRa,
                ["ra_reread"] = read,
                ["feature"] = featureItem,
                ["side"] = Str(a, "side", "top"),
                ["symbols_before"] = NumberOrNull(before),
                ["symbols_after"] = NumberOrNull(after),
                ["position_mm"] = pos,
                ["attached_to_edge"] = attached > 0,
            };
            if (before >= 0 && after >= 0 && after != before + 1)
                measurements["warning"] = "I simboli di rugosita' della tavola sono passati da " + before + " a " + after +
                                       ": ne aspettavo uno in piu'.";
            return WithoutFeature(measurements);
        }

        // ──────────────────────────────────────────────────────────────────
        // datum
        // ──────────────────────────────────────────────────────────────────
        private Outcome Datum(IModelDoc2 model, Dictionary<string, object> a)
        {
            string reason;
            IDrawingDoc drawing;
            var view = ViewToAnnotate(model, a, out drawing, out reason);
            if (view == null) return Failed(reason);
            string letter = (Str(a, "letter", "A") ?? "").Trim().ToUpperInvariant();
            if (letter.Length != 1 || letter[0] < 'A' || letter[0] > 'Z')
                return Failed("«letter» e' una lettera sola, dalla A alla Z, non «" + Str(a, "letter", "") + "».");
            // TWO DATUMS WITH THE SAME LETTER give no error, and every tolerance that
            // cites that letter becomes ambiguous (trap written in Z-E6).
            var already = DatumLetters(drawing);
            if (already.Contains(letter))
                return Failed("In tavola c'e' gia' un datum «" + letter + "»: due riferimenti con la stessa lettera " +
                               "rendono ambigua ogni tolleranza che lo cita. Usa un'altra lettera (ci sono gia': " +
                               string.Join(", ", already) + ").");
            var side = SideEdge(view, Str(a, "side", "bottom"), out reason);
            if (side == null) return Failed(reason);

            double occupied = OccupiedBand(view, side);
            int before = DrawingAnnotations(drawing, "datum");
            if (!SelectInView(model, view, side.Edge))
                return Failed("Non riesco a selezionare nella vista lo spigolo del lato «" + Str(a, "side", "bottom") + "».");
            object raw;
            try { raw = model.InsertDatumTag2(); }
            catch (Exception ex) { return Failed("Il datum non e' nato: " + ex.Message); }
            model.ClearSelection2(true);
            var datum = raw as IDatumTag;
            if (datum == null) return Failed("SOLIDWORKS non ha creato il datum e non ha detto perche'.");
            bool labeled = false;
            try { labeled = datum.SetLabel(letter); } catch { }
            string read = null;
            try { read = datum.GetLabel(); } catch { }
            var an = datum.GetAnnotation() as IAnnotation;
            // The flag OUTSIDE, on the side of the edge: left where it is born it was
            // on the other side and the line crossed the part (seen in the PNG).
            var pos = Move(an, drawing, view, side, Math.Max(12, occupied + 10), -30, 8, 8);
            int attached = -1;
            try { attached = an.GetAttachedEntityCount3(); } catch { }
            int after = DrawingAnnotations(drawing, "datum");
            if (!string.Equals((read ?? "").Trim(), letter, StringComparison.OrdinalIgnoreCase))
                return Failed("Il datum e' nato ma dice «" + read + "» invece di «" + letter + "»" +
                               (labeled ? "" : " (SetLabel ha risposto no)") + ": toglilo — " + Sentences.HowToWithdrawIt + ".");
            var measurements = new Dictionary<string, object>
            {
                ["letter"] = letter,
                ["letter_reread"] = read,
                ["side"] = Str(a, "side", "bottom"),
                ["datum_before"] = NumberOrNull(before),
                ["datum_after"] = NumberOrNull(after),
                ["position_mm"] = pos,
                ["attached_to_edge"] = attached > 0,
            };
            return WithoutFeature(measurements);
        }

        // ──────────────────────────────────────────────────────────────────
        // geometric_tolerance
        // ──────────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, int> Characteristics = new Dictionary<string, int>
        {
            ["straightness"] = (int)swGtolGeomCharSymbol_e.swGcsSTRAIGHT,
            ["flatness"] = (int)swGtolGeomCharSymbol_e.swGcsFLAT,
            ["circularity"] = (int)swGtolGeomCharSymbol_e.swGcsROUND,
            ["cylindricity"] = (int)swGtolGeomCharSymbol_e.swGcsCYL,
            ["profile_of_line"] = (int)swGtolGeomCharSymbol_e.swGcsLINEPROF,
            ["profile_of_surface"] = (int)swGtolGeomCharSymbol_e.swGcsSURFPROF,
            ["angularity"] = (int)swGtolGeomCharSymbol_e.swGcsANG,
            ["perpendicularity"] = (int)swGtolGeomCharSymbol_e.swGcsPERP,
            ["parallelism"] = (int)swGtolGeomCharSymbol_e.swGcsPARALLEL,
            ["position"] = (int)swGtolGeomCharSymbol_e.swGcsPOSITION,
            ["concentricity"] = (int)swGtolGeomCharSymbol_e.swGcsCONC,
            ["symmetry"] = (int)swGtolGeomCharSymbol_e.swGcsSYMMETRY,
            ["circular_runout"] = (int)swGtolGeomCharSymbol_e.swGcsCIRCRUNOUT,
            ["total_runout"] = (int)swGtolGeomCharSymbol_e.swGcsTOTALRUNOUT,
        };

        /// <summary>FORM tolerances have no references (ISO 1101); the others want them.</summary>
        private static bool IsOfShape(string c)
        {
            return c == "straightness" || c == "flatness" || c == "circularity" || c == "cylindricity";
        }

        private Outcome GeometricTolerance(IModelDoc2 model, Dictionary<string, object> a)
        {
            string reason;
            IDrawingDoc drawing;
            var view = ViewToAnnotate(model, a, out drawing, out reason);
            if (view == null) return Failed(reason);
            string characteristic = Str(a, "characteristic", "");
            int code;
            if (!Characteristics.TryGetValue(characteristic, out code))
                return Failed("«characteristic» e' una fra " + string.Join(", ", Characteristics.Keys) +
                               ", non «" + characteristic + "».");
            double val;
            if (!RequiredNum(a, "value_mm", out val, out reason)) return Failed(reason);
            if (val <= 0) return Failed("«value_mm» e' l'ampiezza della zona di tolleranza: deve essere maggiore di zero.");
            string valueText = val.ToString("0.###", CultureInfo.InvariantCulture);

            var references = new List<string>();
            foreach (var r in StringList(a, "references")) if (!string.IsNullOrWhiteSpace(r)) references.Add(r.Trim().ToUpperInvariant());
            if (references.Count > 3)
                return Failed("Una cornice cita al massimo tre riferimenti (primario, secondario, terziario).");
            if (IsOfShape(characteristic) && references.Count > 0)
                return Failed("La tolleranza di " + characteristic + " e' di FORMA: non si riferisce a nessun datum " +
                               "(ISO 1101). Togli «references», oppure scegli una tolleranza di orientamento o di " +
                               "posizione.");
            if (!IsOfShape(characteristic) && references.Count == 0)
                return Failed("La tolleranza di " + characteristic + " si misura rispetto a un riferimento: dai almeno una lettera " +
                               "in «references» (un datum messo con «datum»).");
            var already = DatumLetters(drawing);
            foreach (var r in references)
                if (!already.Contains(r))
                    return Failed("La tolleranza cita il datum «" + r + "» e in tavola non c'e'" +
                                   (already.Count == 0 ? " nessun datum" : " (ci sono: " + string.Join(", ", already) + ")") +
                                   ". Mettilo prima con «datum».");
            bool diameter = a != null && a.ContainsKey("diameter") && Convert.ToBoolean(a["diameter"], CultureInfo.InvariantCulture);
            var side = SideEdge(view, Str(a, "side", "right"), out reason);
            if (side == null) return Failed(reason);

            double occupied = OccupiedBand(view, side);
            int before = DrawingAnnotations(drawing, "gtol");
            if (!SelectInView(model, view, side.Edge))
                return Failed("Non riesco a selezionare nella vista lo spigolo del lato «" + Str(a, "side", "right") + "».");
            object raw;
            try { raw = model.InsertGtol(); }
            catch (Exception ex) { return Failed("La cornice di tolleranza non e' nata: " + ex.Message); }
            model.ClearSelection2(true);
            var gtol = raw as IGtol;
            if (gtol == null) return Failed("SOLIDWORKS non ha creato la cornice di tolleranza e non ha detto perche'.");
            string r1 = references.Count > 0 ? references[0] : "", r2 = references.Count > 1 ? references[1] : "",
                   r3 = references.Count > 2 ? references[2] : "";
            try
            {
                gtol.SetFrameSymbols(1, (short)code, diameter, (short)swGtolMatCondition_e.swMcNONE, false,
                    (short)swGtolMatCondition_e.swMcNONE, (short)swGtolMatCondition_e.swMcNONE,
                    (short)swGtolMatCondition_e.swMcNONE, (short)swGtolMatCondition_e.swMcNONE);
                gtol.SetFrameValues(1, valueText, "", r1, r2, r3);
            }
            catch (Exception ex)
            {
                // A FRAME THAT CANNOT BE FILLED IS NOT LEFT ON THE SHEET (M38),
                // as a wrong one below.
                IAnnotation emptyFrame = null;
                try { emptyFrame = gtol.GetAnnotation() as IAnnotation; } catch { }
                bool removedEmpty = RemoveNewAnnotation(model, emptyFrame,
                                                        () => DrawingAnnotations(drawing, "gtol"), before);
                return Failed("The tolerance frame was born but does not let itself be filled (" + ex.Message + ")" +
                               (removedEmpty ? ": I removed it." : ": remove it — " + Sentences.HowToWithdrawIt + "."));
            }
            // THE RE-READING: the symbol as a code, the values as strings.
            int readSymbol = -1;
            var valuesRead = new List<string>();
            try
            {
                var fs = gtol.GetFrameSymbols(1) as System.Collections.IEnumerable;
                if (fs != null) foreach (var x in fs) { readSymbol = Convert.ToInt32(x, CultureInfo.InvariantCulture); break; }
                var fv = gtol.GetFrameValues(1) as System.Collections.IEnumerable;
                if (fv != null) foreach (var x in fv) valuesRead.Add(Convert.ToString(x, CultureInfo.InvariantCulture) ?? "");
            }
            catch { }
            // THE Ø IS READ BACK TOO (Z-C21.8, finding M38). IGtol~GetFrameSymbols2.html:
            // «Use this method with IGtol::GetFrameDiameterSymbols, which determines
            // whether diameter symbols are displayed»; GetFrameDiameterSymbols.html:
            // «an array of two Booleans ... retval [0] = True if the first tolerance
            // value has a diameter symbol». Until library 0.144.0 the answer
            // echoed the argument there, not the frame.
            int frameDiameterFlag = -1;
            try
            {
                var fd = gtol.GetFrameDiameterSymbols(1) as System.Collections.IEnumerable;
                if (fd != null) foreach (var x in fd) { frameDiameterFlag = Convert.ToBoolean(x, CultureInfo.InvariantCulture) ? 1 : 0; break; }
            }
            catch { }
            var an = gtol.GetAnnotation() as IAnnotation;
            var pos = Move(an, drawing, view, side, Math.Max(15, occupied + 12), 0, 10 * (2 + references.Count), 8);
            int attached = -1;
            try { attached = an.GetAttachedEntityCount3(); } catch { }
            int after = DrawingAnnotations(drawing, "gtol");

            string valueRead = valuesRead.Count > 0 ? valuesRead[0] : "";
            var readReferences = new List<string>();
            for (int i = 2; i < valuesRead.Count && i < 5; i++) if (!string.IsNullOrWhiteSpace(valuesRead[i])) readReferences.Add(valuesRead[i].Trim());
            string diameterWrong = GtolDiameterWrong(diameter, frameDiameterFlag);
            if (readSymbol != code || valueRead.Trim() != valueText ||
                string.Join(",", readReferences) != string.Join(",", references) || diameterWrong != null)
            {
                // A WRONG FRAME IS NOT LEFT ON THE SHEET (Z-C21.8, finding M38): it
                // would be printed and read in the shop. Removed as drawing_weld_symbol
                // removes a wrong weld symbol, and said removed only after a recount.
                bool removed = RemoveNewAnnotation(model, an, () => DrawingAnnotations(drawing, "gtol"), before);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The tolerance frame was born but does not say what I asked: symbol {0} (asked {1}), value «{2}» " +
                    "(asked «{3}»), datums «{4}» (asked «{5}»){6}. {7}",
                    readSymbol, code, valueRead, valueText, string.Join(",", readReferences),
                    string.Join(",", references), diameterWrong == null ? "" : "; " + diameterWrong,
                    removed ? "I removed it." : "Remove it — " + Sentences.HowToWithdrawIt + "."));
            }
            var measurements = new Dictionary<string, object>
            {
                ["characteristic"] = characteristic,
                ["symbol_reread"] = readSymbol,
                ["value_mm"] = valueText,
                ["value_reread"] = valueRead,
                ["references"] = references.ToArray(),
                ["references_reread"] = readReferences.ToArray(),
                ["diameter"] = diameter,
                // The Ø as read back from the frame (M38): null when it could not be
                // read and was not asked, which GtolDiameterWrong lets through.
                ["diameter_read"] = frameDiameterFlag < 0 ? null : (object)(frameDiameterFlag == 1),
                ["side"] = Str(a, "side", "right"),
                ["frames_before"] = NumberOrNull(before),
                ["frames_after"] = NumberOrNull(after),
                ["position_mm"] = pos,
                ["attached_to_edge"] = attached > 0,
            };
            return WithoutFeature(measurements);
        }
    }
}
