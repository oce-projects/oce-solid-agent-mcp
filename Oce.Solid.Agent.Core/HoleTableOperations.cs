using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA TABELLA FORI IN TAVOLA (voce GT5).
    ///
    /// ── MISURATA PRIMA DI SCRIVERLA, 26/9/2026 (`prova0089 tabellafori`) ────
    ///
    /// `IView.InsertHoleTable2(UseAnchorPoint, X, Y, AnchorType, StartValue,
    /// TableTemplate)`, and `InsertHoleTable3` that replaces it since Z-C21.7,
    /// have no argument for the origin or the face: they are SELECTED first, in
    /// the view (`ISelectData.View`). Measured with InsertHoleTable2 on a
    /// 120 x 80 plate with four D8 holes 15 from the edges, top view:
    ///
    ///   corner vertex with mark 1, drilled face with mark 2
    ///       -> 5 rows: A1 (15,15), A2 (15,65), A3 (105,15), A4 (105,65),
    ///          all «Ø8 PASSANTE»: the positions from the corner, right
    ///   marks 2/1, or 0/0
    ///       -> the table is BORN EMPTY (the header only), without an error
    ///
    /// The Z-C21.3 probe probe_hole_table3 found the same with InsertHoleTable3
    /// (swapped marks, H3: the header only). Hence the operation wants
    /// rows = holes + 1, and removes and refuses a table born empty. And it
    /// reads every row back against the model: the position of each hole from
    /// the origin, measured on the view, must be in the table.
    ///
    /// Il 17/9 nessuna tabella nasceva su questa installazione (GT2): il 26/9
    /// nascono tutte tranne la distinta di taglio. Vedi la voce GT2 del TODO.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class HoleInView
        {
            public double X, Y;          // centro sul foglio, metri
            public double DiameterMm;
        }

        private Outcome HoleTable(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("Il documento attivo non e' una tavola: la tabella fori sta su una vista. " +
                               "Crea prima la tavola con «create_drawing».");
            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed("La tavola non ha viste: la tabella fori si aggancia a una vista del pezzo. " +
                               "Metti prima le viste con «standard_views».");

            string origin = Str(a, "origin", "bottom_left");
            int sx, sy;
            switch (origin)
            {
                case "bottom_left": sx = -1; sy = -1; break;
                case "bottom_right": sx = 1; sy = -1; break;
                case "top_left": sx = -1; sy = 1; break;
                case "top_right": sx = 1; sy = 1; break;
                default:
                    return Failed("«origin» e' uno fra bottom_left, bottom_right, top_left, " +
                                   "top_right, non «" + origin + "».");
            }

            string reason;
            IView view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason != null) return Failed(reason);
            if (view == null) return Failed("Non riesco a prendere la vista.");
            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }

            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null)
                return Failed("Non riesco a leggere come la vista porta il modello sul foglio.");
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;

            // ── LA FACCIA FORATA CHE GUARDA CHI LEGGE ───────────────────────
            //
            // Fra le facce piane del pezzo dietro la vista, quelle la cui normale,
            // portata sul foglio, esce verso chi guarda (z > 0,99); di queste, quella
            // con piu' anelli interni fatti di cerchi — i fori.
            var part = view.ReferencedDocument as IModelDoc2;
            var partDoc = part as IPartDoc;
            if (partDoc == null)
                return Failed("Dietro la vista «" + viewName + "» non c'e' una parte: la tabella fori si fa " +
                               "sui fori di un pezzo.");
            Func<double[], double[]> onSheet = p =>
                ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[];
            Func<double[], double[]> directionOnSheet = v =>
                ((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(tr)).ArrayData as double[];

            IFace2 face = null;
            var holes = new List<HoleInView>();
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                foreach (var oc in bodies ?? new object[0])
                {
                    foreach (var of in ((oc as IBody2)?.GetFaces() as object[]) ?? new object[0])
                    {
                        var f = of as IFace2;
                        var n = ApiHelpers.NormalIfPlanar(f);
                        if (n == null) continue;
                        var nv = directionOnSheet(n);
                        double len = Math.Sqrt(nv[0] * nv[0] + nv[1] * nv[1] + nv[2] * nv[2]);
                        if (len < 1e-12 || nv[2] / len < 0.99) continue;
                        var here = FaceHoles(f, onSheet);
                        if (here.Count > holes.Count) { holes = here; face = f; }
                    }
                }
            }
            catch (Exception ex) { return Failed("Non riesco a leggere le facce del pezzo: " + ex.Message); }
            if (face == null || holes.Count == 0)
                return Failed("Nella vista «" + viewName + "» nessuna faccia con dei fori guarda verso chi legge: " +
                               "la tabella fori si fa sulla vista in cui i fori si vedono come cerchi (di solito " +
                               "quella dall'alto, V2).");

            // ── L'ORIGINE: il vertice del contorno esterno piu' nell'angolo chiesto ──
            IVertex vertex = null;
            double vx = 0, vy = 0, better = double.MinValue;
            try
            {
                foreach (var oe in (face.GetEdges() as object[]) ?? new object[0])
                {
                    var e = oe as IEdge;
                    foreach (var ov in new[] { e?.GetStartVertex(), e?.GetEndVertex() })
                    {
                        var v = ov as IVertex;
                        var p = v?.GetPoint() as double[];
                        if (p == null) continue;
                        var s = onSheet(p);
                        double score = s[0] * sx + s[1] * sy;
                        if (score > better + 1e-9) { better = score; vertex = v; vx = s[0]; vy = s[1]; }
                    }
                }
            }
            catch { }
            if (vertex == null) return Failed("Non trovo un vertice d'angolo sulla faccia forata.");

            // ── DOVE VA LA TABELLA: in alto a sinistra del foglio, se non si dice ──
            double sheetWidth = 0.420, sheetHeight = 0.297;
            try
            {
                var sheet = drawing.GetCurrentSheet() as ISheet;
                var pr = sheet?.GetProperties2() as double[];
                if (pr != null && pr.Length >= 7 && pr[5] > 0 && pr[6] > 0) { sheetWidth = pr[5]; sheetHeight = pr[6]; }
            }
            catch { }
            double x = Units.M(Num(a, "x_mm", 15)), y = Units.M(Num(a, "y_mm", Units.Mm(sheetHeight) - 15));
            string firstTag = Str(a, "tag_start", "A");

            int calloutsBefore = HoleCalloutsInView(view);
            int tablesBefore = DrawingTables(drawing);
            model.ClearSelection2(true);
            var sel = model.SelectionManager as ISelectionMgr;
            var sdV = sel?.CreateSelectData() as SelectData;
            var sdF = sel?.CreateSelectData() as SelectData;
            if (sdV == null || sdF == null) return Failed("Non riesco a preparare la selezione nella vista.");
            sdV.Mark = SelectionMarks.HoleTableDatumOrigin; sdV.View = (View)view;
            sdF.Mark = SelectionMarks.HoleTableHoles; sdF.View = (View)view;
            bool okV = (vertex as IEntity).Select4(false, sdV);
            bool okF = (face as IEntity).Select4(true, sdF);
            if (!okV || !okF)
                return Failed("Non riesco a selezionare nella vista l'origine e la faccia forata (vertice " + okV +
                               ", faccia " + okF + ").");

            string tableModel = FirstTableModel((int)swUserPreferenceStringValue_e.swFileLocationsHoleTableTemplates, ".sldholtbt");
            // Z-C21.7 (L15-1): InsertHoleTable3, not the obsolete InsertHoleTable2
            // (IView~InsertHoleTable2.html: «Obsolete. Superseded by
            // IView::InsertHoleTable3»). The two new arguments as case H1 of the
            // Z-C21.3 probe probe_hole_table3: TagOrder swHoleTableTagOrder_XY (1),
            // TagType swHoleTable_AlphaNumericTags (1), no manual tags
            // (IView~InsertHoleTable3.html: «ManualTags Array of custom tags;
            // valid only if TagType is swHoleTableTagStyle_e.swHoleTable_ManualTags»).
            // On the bench plate (120 x 80, four D8 and one D20) H1 gave the same
            // six rows as InsertHoleTable2 (H0): A1 (15,15), A2 (15,65),
            // A3 (105,15), A4 (105,65), B1 (60,40), every one on a hole. TagType
            // is honoured (H2, numeric: 1 to 5), so the start value this
            // operation takes, the LETTER of the tags, goes with the alphanumeric
            // style.
            object raw;
            try
            {
                raw = view.InsertHoleTable3(false, x, y,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft, firstTag, tableModel,
                    (int)swHoleTableTagOrder_e.swHoleTableTagOrder_XY,
                    (int)swHoleTableTagStyle_e.swHoleTable_AlphaNumericTags, null);
            }
            catch (Exception ex) { return Failed("La tabella fori non e' nata: " + ex.Message); }
            model.ClearSelection2(true);
            var table = raw as ITableAnnotation;
            if (table == null)
                return Failed("SOLIDWORKS non ha creato la tabella fori e non ha detto perche'.");

            int lines = -1;
            try { lines = table.RowCount; } catch { }
            // NATA VUOTA: e' cio' che succede coi marchi sbagliati, misurato. Una
            // tabella di sola intestazione si stampa e sembra che il pezzo non abbia fori.
            if (lines != holes.Count + 1)
            {
                string removed = RemoveTable(model, drawing, table, tablesBefore);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "La tabella fori e' nata con {0} righe e i fori della faccia sono {1}: ne servivano {2} " +
                    "(l'intestazione piu' un foro per riga).{3}", lines, holes.Count, holes.Count + 1, removed));
            }

            // ── LA RILETTURA: ogni riga contro il modello ───────────────────
            var entries = new List<object>();
            int match = 0;
            for (int r = 1; r < lines; r++)
            {
                string tag = CleanCell(TableCell(table, r, 0));
                string sx0 = CleanCell(TableCell(table, r, 1));
                string sy0 = CleanCell(TableCell(table, r, 2));
                string measure = CleanCell(TableCell(table, r, 3));
                double lx, ly;
                bool numbers = double.TryParse(sx0.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out lx) &
                              double.TryParse(sy0.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out ly);
                bool found = false;
                if (numbers)
                    foreach (var f in holes)
                    {
                        double dx = Math.Abs(f.X - vx) / scale * 1000.0, dy = Math.Abs(f.Y - vy) / scale * 1000.0;
                        if (Math.Abs(dx - Math.Abs(lx)) <= 0.05 && Math.Abs(dy - Math.Abs(ly)) <= 0.05) { found = true; break; }
                    }
                if (found) match++;
                entries.Add(new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["x_mm"] = numbers ? (object)lx : sx0,
                    ["y_mm"] = numbers ? (object)ly : sy0,
                    ["measure"] = measure,
                    ["in_model"] = found,
                });
            }
            if (match != holes.Count)
            {
                string removed = RemoveTable(model, drawing, table, tablesBefore);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "La tabella fori dice {0} posizioni e solo {1} corrispondono a un foro del pezzo misurato " +
                    "dall'origine sulla vista: non e' la tabella di questi fori.{2}", holes.Count, match, removed));
            }

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["origin"] = origin,
                ["holes"] = holes.Count,
                ["rows"] = lines,
                ["entries"] = entries.ToArray(),
                ["positions_checked"] = true,
                ["tables_in_drawing"] = DrawingTables(drawing),
            };
            if (!string.IsNullOrEmpty(tableModel)) measurements["table_template"] = tableModel;
            // I FORI DUE VOLTE (26/9/2026, la prima tavola guardata con la tabella):
            // se la vista aveva gia' i richiami dei fori, la tabella li ripete.
            // «Ciascun elemento si quota una volta sola»: lo si dice. Nell'ordine
            // giusto — tabella prima, poi drawing_dimension — non succede, perche'
            // drawing_dimension lascia i fori alla tabella.
            if (calloutsBefore > 0)
                measurements["warning"] = "Nella vista c'erano gia' " + calloutsBefore + " richiami di foro (e di solito le " +
                                       "quote di posizione dei fori): con la tabella i fori sono quotati due volte. " +
                                       "Metti la tabella PRIMA di «drawing_dimension», che allora lascia i fori alla tabella.";
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// I fori di una faccia: gli anelli interni fatti di un cerchio intero,
        /// col centro portato sul foglio.
        /// </summary>
        private static List<HoleInView> FaceHoles(IFace2 f, Func<double[], double[]> onSheet)
        {
            var outside = new List<HoleInView>();
            try
            {
                foreach (var ol in (f.GetLoops() as object[]) ?? new object[0])
                {
                    var ring = ol as ILoop2;
                    if (ring == null || ring.IsOuter()) continue;
                    var edges = ring.GetEdges() as object[];
                    if (edges == null || edges.Length == 0) continue;
                    var curva = (edges[0] as IEdge)?.GetCurve() as ICurve;
                    if (curva == null || !curva.IsCircle()) continue;
                    var cp = curva.CircleParams as double[];   // centro x,y,z, asse, raggio
                    if (cp == null || cp.Length < 7) continue;
                    var c = onSheet(new[] { cp[0], cp[1], cp[2] });
                    outside.Add(new HoleInView { X = c[0], Y = c[1], DiameterMm = cp[6] * 2000.0 });
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// Quanti fori elencano le tabelle fori del foglio corrente (righe meno
        /// l'intestazione); 0 se non ce ne sono, -1 se non si legge.
        ///
        /// SI GUARDA TUTTO IL FOGLIO, NON LA VISTA. Misurato il 26/9/2026: la
        /// tabella fori messa sulla vista dall'alto appartiene al FOGLIO (la
        /// «view» zero di `GetFirstView`), non alla vista — cercandola nella
        /// vista, `drawing_dimension` non la vedeva e quotava i fori una seconda
        /// volta. Una tavola e' di un pezzo: una tabella fori sul foglio copre i
        /// fori delle sue viste.
        /// </summary>
        private static int HolesInHoleTable(IDrawingDoc drawing)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
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
        private static int HoleCalloutsInView(IView view)
        {
            try
            {
                int n = 0;
                var d = view.GetFirstDisplayDimension5() as IDisplayDimension;
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
        private static int DrawingTables(IDrawingDoc drawing)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
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
        private static string RemoveTable(IModelDoc2 model, IDrawingDoc drawing, ITableAnnotation table, int before)
        {
            try
            {
                var ann = table.GetAnnotation() as IAnnotation;
                model.ClearSelection2(true);
                // DeleteSelection2 E NON EditDelete: EditDelete chiede conferma con
                // una finestra, e la chiamata COM non torna (vedi SwOperations,
                // WithoutSketch). Era l'unica rimasta, trovata dal workflow W-S il
                // 27/9/2026; adesso il divieto e' in Tests/ForbiddenSignaturesTests.
                if (ann != null && ann.Select3(false, null))
                    model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            int after = DrawingTables(drawing);
            if (before >= 0 && after == before) return " L'ho tolta: la tavola e' com'era.";
            return " Non sono riuscito a toglierla (tabelle " + before + " -> " + after + "): toglila a mano.";
        }

        private static string TableCell(ITableAnnotation t, int r, int c)
        {
            try { return t.get_Text(r, c) ?? ""; } catch { return ""; }
        }

        /// <summary>Il testo di una cella senza i codici di formato: «&lt;MOD-DIAM&gt;» diventa «Ø».</summary>
        private static string CleanCell(string s)
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
        private string FirstTableModel(int preference, string ext)
        {
            try
            {
                string folders = _sw.GetUserPreferenceStringValue(preference) ?? "";
                foreach (var c in folders.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(c) || !System.IO.Directory.Exists(c)) continue;
                    var f = System.IO.Directory.GetFiles(c, "*" + ext, System.IO.SearchOption.AllDirectories);
                    if (f.Length > 0) { Array.Sort(f, StringComparer.OrdinalIgnoreCase); return f[0]; }
                }
            }
            catch { }
            return "";
        }
    }
}
