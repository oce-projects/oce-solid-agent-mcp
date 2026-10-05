using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// IL PEZZO TONDO VISTO DI FIANCO (voce GT14): le lunghezze fra le facce e i
    /// diametri, nella vista longitudinale di una boccola, di un perno, di un
    /// albero a gradini.
    ///
    /// ── COSA SI E' MISURATO PRIMA (27/9/2026, `prova0089 tornito`..`tornito8`) ──
    ///
    /// Sull'albero a gradini dell'esempio di «revolve» (Ø30x40, Ø20x30,
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
    public partial class SwOperations
    {
        private sealed class RoundOutcome
        {
            public readonly List<double> Lengths = new List<double>();
            public readonly List<double> Diameters = new List<double>();
            public readonly List<string> NotReached = new List<string>();
            public readonly List<string> Note = new List<string>();
            public int FacelessGeneratrices;
            public int DiscardedPairs;
            public string Axis;
        }

        /// <summary>
        /// Quota il tondo visto di fianco, se i cerchi di taglio della vista sono
        /// coassiali (almeno due). Null se la vista non e' quella di un tondo.
        /// </summary>
        private RoundOutcome DimensionTheRoundFromTheSide(
            IModelDoc2 model, IDrawingDoc drawing, IView view, IMathUtility mu, MathTransform tr,
            List<Circle> cutting, HashSet<string> already, double[] contour, double dimensionOffset, double planStep,
            ref int below, ref int sideways, List<Dictionary<string, object>> born,
            List<string> elsewhere, List<string> failed)
        {
            if (cutting.Count < 2 || cutting[0].ModelAxis == null) return null;
            var axis = cutting[0].ModelAxis;
            var o = cutting[0].ModelCenter;
            // COASSIALI: stesso asse, centri sulla stessa retta.
            var onAxis = new List<Tuple<double, Circle>>();
            foreach (var c in cutting)
            {
                if (c.ModelAxis == null || c.ModelCenter == null) return null;
                double par = Math.Abs(Dot(c.ModelAxis, axis));
                if (par < 0.9999) return null;
                var d = Sub(c.ModelCenter, o);
                double t = Dot(d, axis);
                var outside = Sub(d, Mul(axis, t));
                if (Standard(outside) > 1e-6) return null;
                onAxis.Add(Tuple.Create(t, c));
            }
            var outcome = new RoundOutcome();
            var sheetAxis = VectorOnSheet(axis, mu, tr);
            if (sheetAxis == null) return null;
            bool horizontal = Math.Abs(sheetAxis[0]) >= Math.Abs(sheetAxis[1]);
            outcome.Axis = horizontal ? "horizontal" : "vertical";
            // Il verso del parametro t sul foglio: da sinistra a destra (o dal
            // basso in alto), per partire dalla faccia giusta.
            double sheetFacing = horizontal ? sheetAxis[0] : sheetAxis[1];
            onAxis.Sort((a, b) => (a.Item1 * Math.Sign(sheetFacing)).CompareTo(b.Item1 * Math.Sign(sheetFacing)));
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
            var faces = new List<Tuple<double, Circle>>();
            var chamfers = new List<string>();
            foreach (var p in onAxis)
            {
                string border = WhatBordersTheCircle(p.Item2.Edge, axis);
                if (border != "piano")
                {
                    if (border == "cono" || border == "toro")
                    {
                        string said = (border == "cono" ? "smusso" : "raccordo") + " sul Ø" + p.Item2.DiameterMm.ToString("0.##", Ci);
                        if (!chamfers.Contains(said)) chamfers.Add(said);
                    }
                    continue;
                }
                if (faces.Count == 0 || Math.Abs(p.Item1 - faces[faces.Count - 1].Item1) > 1e-6) faces.Add(p);
            }
            foreach (var sm in chamfers)
                outcome.Note.Add(sm + " (non e' una faccia del tondo: la sua misura va con la quota da smusso o " +
                                       "da raccordo, che questa vista non mette)");
            if (faces.Count < 2)
                outcome.Note.Add("lunghezze (meno di due facce piane di testa o di spallamento viste di taglio)");

            var selMgr = model.SelectionManager as ISelectionMgr;
            // ── LE LUNGHEZZE, IN PARALLELO DALLA PRIMA FACCIA ──────────────────
            var zero = faces.Count > 0 ? faces[0] : null;
            for (int i = 1; i < faces.Count; i++)
            {
                var f = faces[i];
                double expectedValueMm = Math.Round(Math.Abs(f.Item1 - zero.Item1) * 1000.0, 3);
                string key = RoundKey(zero.Item2.ModelCenter, f.Item2.ModelCenter);
                string what = "lunghezza " + expectedValueMm.ToString("0.##", Ci);
                if (already.Contains(key)) { elsewhere.Add(what + " (gia' quotata in questa tavola)"); continue; }
                double x, y;
                if (horizontal)
                {
                    x = (zero.Item2.SheetCenter[0] + f.Item2.SheetCenter[0]) / 2;
                    y = contour[1] - dimensionOffset - below * planStep;
                }
                else
                {
                    x = contour[2] + dimensionOffset + sideways * planStep;
                    y = (zero.Item2.SheetCenter[1] + f.Item2.SheetCenter[1]) / 2;
                }
                bool bornWithoutObject;
                var q = DimensionBetweenTwo(model, drawing, view, selMgr,
                                    e => (zero.Item2.Edge as IEntity).Select4(false, e) &&
                                         (f.Item2.Edge as IEntity).Select4(true, e), x, y, horizontal,
                                    out bornWithoutObject);
                if (q == null && !bornWithoutObject) { failed.Add(what + " (la quota non e' nata)"); continue; }
                // BORN WITHOUT ITS OBJECT (finding N80, the site Z-C21.8/9 left
                // out): the dimension is on the sheet and its value cannot be read
                // back. It keeps its place (the next length goes one step
                // further out), its row with the value unknown, and its key in
                // `gia`: another view of the sheet must not dimension it again.
                if (q != null && !CorrectReread(model, q, expectedValueMm, false, what, failed)) continue;
                if (horizontal) below++; else sideways++;
                var described = DescribeDimension(q, f.Item2.Index);
                described["orientation"] = "round length";
                described["position_mm"] = WhereItEnded(q);
                if (q != null)
                {
                    born.Add(described);
                    outcome.Lengths.Add(expectedValueMm);
                }
                else RecordBornWithoutObject(born, described, failed,
                    "turned length " + expectedValueMm.ToString("0.##", Ci) + " (the dimension was born but SOLIDWORKS did not " +
                    "hand it back: its value was not read back, check it on the sheet)");
                already.Add(key);
            }

            // I CILINDRI COASSIALI DEL PEZZO (diametro, e il punto a meta' lungo
            // l'asse): dicono quali diametri mancano, e riconoscono una quota nata
            // da due generatrici di cui SOLIDWORKS non dice la faccia.
            var cylinders = CoaxialCylinders(view, axis, o);

            // ── I DIAMETRI, DALLE DUE GENERATRICI DI OGNI FACCIA ────────────────
            //
            // LE GENERATRICI SI RILEGGONO PRIMA DI OGNI QUOTA: sono oggetti della
            // vista, non del modello, e dopo una quota nuova quelle lette prima
            // possono non selezionarsi piu' (misurato il 27/9/2026, `orvert`: la
            // seconda coppia «presa True/False»). La quota e' ORIZZONTALE o
            // VERTICALE dichiarata (AddVerticalDimension2 per un asse orizzontale):
            // su due generatrici della stessa faccia SOLIDWORKS ci mette da se' il
            // Ø. `Extension.AddDimension` con due entita' non fa nascere niente.
            bool horizontalDiameterDimension = !horizontal;
            var done = new HashSet<string>(StringComparer.Ordinal);
            for (int benchRun = 0; benchRun < 12; benchRun++)
            {
                var readOnes = ReadGeneratrices(model, view);
                outcome.FacelessGeneratrices = readOnes.WithoutFace.Count;
                string choice = null;
                foreach (var kv in readOnes.Groups)
                    if (kv.Value.Count == 2 && !done.Contains(kv.Key)) { choice = kv.Key; break; }
                if (choice == null) break;
                done.Add(choice);
                var m = readOnes.Meta[choice];
                double expectedValueMm = Math.Round(m[0] * 2000.0, 3);
                string what = "diametro Ø" + expectedValueMm.ToString("0.##", Ci);
                string key = "foro|" + Math.Round(expectedValueMm, 2).ToString("0.00", Ci);
                // A META' DELLA FACCIA, sull'asse: la linea di misura attraversa il
                // suo cilindro, come si quota un albero. Lo stesso punto fa la
                // CHIAVE DEL CILINDRO (revisione del 27/9/2026): due perni Ø30
                // separati da un collare sono due quote, non una.
                double tm = (m[1] + m[2]) / 2;
                string cylinder = CylinderKey(m[0] * 2000.0, new[] { m[3] + tm * m[6], m[4] + tm * m[7], m[5] + tm * m[8] });
                if (already.Contains(cylinder) || already.Contains("cil?|" + key.Substring(5)))
                { elsewhere.Add(what + " (gia' quotato in questa tavola)"); continue; }
                var half = OnSheet(new[] { m[3] + tm * m[6], m[4] + tm * m[7], m[5] + tm * m[8] }, mu, tr);
                if (half == null) { failed.Add(what + " (non so dove sta sul foglio)"); continue; }
                var a = readOnes.Groups[choice][0]; var b = readOnes.Groups[choice][1];
                bool bornWithoutObject;
                var q = DimensionBetweenTwo(model, drawing, view, selMgr, e => a.Select2(false, e) && b.Select2(true, e),
                                    half[0], half[1], horizontalDiameterDimension, out bornWithoutObject);
                if (q == null && !bornWithoutObject) { failed.Add(what + " (la quota non e' nata)"); continue; }
                // BORN WITHOUT ITS OBJECT (N80), as the lengths above: the row
                // with the value unknown, not in `round_diameters_mm` (which lists
                // the diameters read back with their Ø), and the keys of this
                // cylinder in `gia`, so that it is neither dimensioned again nor
                // reported as not reached.
                if (q != null && !CorrectReread(model, q, expectedValueMm, true, what, failed)) continue;
                var described = DescribeDimension(q, -1);
                described["orientation"] = "round diameter";
                described["position_mm"] = WhereItEnded(q);
                if (q != null)
                {
                    born.Add(described);
                    outcome.Diameters.Add(expectedValueMm);
                }
                else RecordBornWithoutObject(born, described, failed,
                    "turned diameter Ø" + expectedValueMm.ToString("0.##", Ci) + " (the dimension was born but SOLIDWORKS did not " +
                    "hand it back: its value and its Ø symbol were not read back, check it on the sheet)");
                already.Add(key);
                already.Add(cylinder);
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
            var tested = new HashSet<string>(StringComparer.Ordinal);
            double cx = (contour[0] + contour[2]) / 2, cy = (contour[1] + contour[3]) / 2;
            for (int benchRun = 0; benchRun < 30; benchRun++)
            {
                var readOnes = ReadGeneratrices(model, view);
                var candidate = new List<int>(readOnes.WithoutFace);
                foreach (var kv in readOnes.Groups) if (kv.Value.Count == 1) candidate.Add(readOnes.Indices[kv.Key][0]);
                candidate.Sort();
                int ia = -1, ib = -1;
                for (int x = 0; x < candidate.Count && ia < 0; x++)
                    for (int y = x + 1; y < candidate.Count; y++)
                        if (tested.Add(candidate[x] + "|" + candidate[y])) { ia = candidate[x]; ib = candidate[y]; break; }
                if (ia < 0) break;
                var a = readOnes[ia]; var b = readOnes[ib];
                if (a == null || b == null) continue;
                bool bornWithoutObject;
                var q = DimensionBetweenTwo(model, drawing, view, selMgr, e => a.Select2(false, e) && b.Select2(true, e),
                                    cx, cy, horizontalDiameterDimension, out bornWithoutObject);
                if (bornWithoutObject)
                {
                    // BORN WITHOUT ITS OBJECT (N80), and here it cannot be judged:
                    // without the object there is no value to match to a
                    // cylinder, no Ø to look for, and nothing to remove
                    // (RemoveDimension selects the annotation of the object). Until
                    // library 0.145.0 this was «not born» and the next pair was
                    // tried: the dimension stayed on the sheet unreported, and a
                    // later pair of the same cylinder could dimension it a second
                    // time. Now it is said, and no further pair is tried.
                    RecordBornWithoutObject(born, DescribeDimension(null, -1), failed,
                        "a dimension between two silhouette edges without a face (it was born but SOLIDWORKS did not " +
                        "hand it back: it could not be read, matched to a cylinder or removed, and the remaining pairs " +
                        "were not tried, so as not to dimension the same cylinder twice; it may be one of the diameters " +
                        "reported as not reached. Check it on the sheet)");
                    break;
                }
                if (q == null) continue;
                double read = double.NaN; string prefix = "";
                try { read = Math.Round(ApiHelpers.ReadDimensionInConfiguration(q.GetDimension2(0) as IDimension, "") * 1000.0, 3); } catch { }
                try { prefix = q.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
                RoundCylinder its = null;
                foreach (var c in cylinders)
                    if (!double.IsNaN(read) && Math.Abs(c.DiameterMm - read) <= 0.01 && !DimensionedCylinder(already, c)) { its = c; break; }
                if (prefix.Contains("<MOD-DIAM>") && its == null && !double.IsNaN(read) &&
                    cylinders.Any(c => Math.Abs(c.DiameterMm - read) <= 0.01))
                {
                    // UN DIAMETRO VERO, ma gia' quotato altrove: si toglie e si dice.
                    RemoveDimension(model, q);
                    string said = "diametro Ø" + read.ToString("0.##", Ci) + " (gia' quotato in questa tavola)";
                    if (!elsewhere.Contains(said)) elsewhere.Add(said);
                    continue;
                }
                if (!prefix.Contains("<MOD-DIAM>") || its == null) { RemoveDimension(model, q); outcome.DiscardedPairs++; continue; }
                var half = OnSheet(its.Meta, mu, tr);
                try { if (half != null) (q.GetAnnotation() as IAnnotation)?.SetPosition2(half[0], half[1], 0); } catch { }
                var described = DescribeDimension(q, -1);
                described["orientation"] = "diametro del tondo (generatrici senza faccia, riconosciuto dal valore)";
                described["position_mm"] = WhereItEnded(q);
                born.Add(described);
                outcome.Diameters.Add(Math.Round(its.DiameterMm, 3));
                already.Add("foro|" + Math.Round(its.DiameterMm, 2).ToString("0.00", Ci));
                already.Add(its.Key);
            }

            // ── E QUELLI CHE LA VISTA NON ESPONE ────────────────────────────────
            // I cilindri coassiali del pezzo il cui diametro, a questo punto, non
            // e' quotato in nessuna vista della tavola.
            var called = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cylinders)
            {
                string itemName = "Ø" + Math.Round(c.DiameterMm, 2).ToString("0.##", Ci);
                if (DimensionedCylinder(already, c) || !called.Add(c.Key)) continue;
                outcome.NotReached.Add(itemName + " (non raggiunto da questa vista: generatrici nascoste, o che SOLIDWORKS " +
                                       "non da'. Se e' un foro, quotalo dalla vista in cui e' un cerchio)");
            }
            return outcome;
        }

        /// <summary>Un cilindro del tondo: il diametro, il punto a meta' lungo l'asse (m, nel modello), la chiave.</summary>
        private sealed class RoundCylinder
        {
            public double DiameterMm;
            public double[] Meta;
            public string Key;
        }

        /// <summary>Quel cilindro e' gia' quotato: la sua chiave, o un Ø uguale di cui non si sa il cilindro.</summary>
        private static bool DimensionedCylinder(HashSet<string> already, RoundCylinder c)
        {
            return already.Contains(c.Key) || already.Contains("cil?|" + Math.Round(c.DiameterMm, 2).ToString("0.00", Ci));
        }

        /// <summary>
        /// I cilindri del pezzo coassiali col tondo, ciascuno col punto a meta' lungo
        /// l'asse e la sua chiave. Vuoto se la vista non e' di una parte.
        /// </summary>
        private static List<RoundCylinder> CoaxialCylinders(IView view, double[] axis, double[] o)
        {
            var outside = new List<RoundCylinder>();
            try
            {
                var partDoc = view.ReferencedDocument as IPartDoc;
                if (partDoc == null) return outside;
                foreach (var ob in (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[]) ?? new object[0])
                    foreach (var fo in ((ob as IBody2)?.GetFaces() as object[]) ?? new object[0])
                    {
                        var face = fo as IFace2;
                        var su = face?.GetSurface() as ISurface;
                        if (su == null || !su.IsCylinder()) continue;
                        var cp = su.CylinderParams as double[];
                        if (Math.Abs(Dot(new[] { cp[3], cp[4], cp[5] }, axis)) < 0.9999) continue;
                        var d = Sub(new[] { cp[0], cp[1], cp[2] }, o);
                        if (Standard(Sub(d, Mul(axis, Dot(d, axis)))) > 1e-6) continue;
                        double tmin, tmax;
                        if (!FaceEnds(face, cp, out tmin, out tmax)) continue;
                        double tm = (tmin + tmax) / 2;
                        var meta = new[] { cp[0] + tm * cp[3], cp[1] + tm * cp[4], cp[2] + tm * cp[5] };
                        outside.Add(new RoundCylinder { DiameterMm = Math.Round(cp[6] * 2000.0, 3), Meta = meta, Key = CylinderKey(cp[6] * 2000.0, meta) });
                    }
            }
            catch { }
            return outside;
        }

        /// <summary>Toglie una quota appena nata. DeleteSelection2, non EditDelete (puo' aprire una finestra).</summary>
        private static bool RemoveDimension(IModelDoc2 model, IDisplayDimension q)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                var an = q.GetAnnotation() as IAnnotation;
                if (an != null && an.Select3(false, null))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            return removed;
        }

        private sealed class Generatrices
        {
            public object[] All = new object[0];
            public readonly Dictionary<string, List<ISilhouetteEdge>> Groups = new Dictionary<string, List<ISilhouetteEdge>>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<int>> Indices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            public readonly Dictionary<string, double[]> Meta = new Dictionary<string, double[]>(StringComparer.Ordinal);
            public readonly List<int> WithoutFace = new List<int>();
            public ISilhouetteEdge this[int i] { get { return All[i] as ISilhouetteEdge; } }
        }

        /// <summary>
        /// Le generatrici della vista, raggruppate per faccia (raggio ed estremi
        /// lungo l'asse). PRIMA SI RIDISEGNA E SI CHIEDE LA FACCIA PIU' VOLTE:
        /// misurato il 27/9/2026 (`prova0089 gendiag`), `GetFace` risponde null
        /// su tutte finche' la tavola non e' ridisegnata e la domanda ripetuta.
        /// </summary>
        private static Generatrices ReadGeneratrices(IModelDoc2 model, IView view)
        {
            var g = new Generatrices();
            // OBSOLETE-MEASURED: stepped_shaft_dimensioned_from_the_side — IModelDoc2~GraphicsRedraw2.html:
            // "Obsolete. Superseded by IModelView::GraphicsRedraw". The redraw is what makes
            // ISilhouetteEdge.GetFace answer (null on every edge until then: probe prova0089
            // gendiag, 27/9/2026). The replacement was not measured here: remeasure in Z-C21.7 (N20).
            try { model.GraphicsRedraw2(); } catch { }
            try { g.All = (view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge) as object[]) ?? new object[0]; } catch { }
            var faces = new IFace2[g.All.Length];
            for (int benchRun = 0; benchRun < 3; benchRun++)
            {
                bool isMissing = false;
                for (int i = 0; i < g.All.Length; i++)
                {
                    if (faces[i] != null) continue;
                    try { faces[i] = (g.All[i] as ISilhouetteEdge)?.GetFace() as IFace2; } catch { }
                    if (faces[i] == null) isMissing = true;
                }
                if (!isMissing) break;
                // OBSOLETE-MEASURED: stepped_shaft_dimensioned_from_the_side — same measure as above.
                try { model.GraphicsRedraw2(); } catch { }
            }
            for (int i = 0; i < g.All.Length; i++)
            {
                var se = g.All[i] as ISilhouetteEdge;
                if (se == null) continue;
                var su = faces[i]?.GetSurface() as ISurface;
                double tmin, tmax;
                double[] cp = su != null && su.IsCylinder() ? su.CylinderParams as double[] : null;
                if (cp == null || !FaceEnds(faces[i], cp, out tmin, out tmax)) { g.WithoutFace.Add(i); continue; }
                string k = Math.Round(cp[6] * 1000, 3).ToString("0.###", Ci) + "|" +
                           Math.Round(tmin * 1000, 2).ToString("0.##", Ci) + "|" + Math.Round(tmax * 1000, 2).ToString("0.##", Ci);
                if (!g.Groups.ContainsKey(k))
                {
                    g.Groups[k] = new List<ISilhouetteEdge>();
                    g.Indices[k] = new List<int>();
                    g.Meta[k] = new[] { cp[6], tmin, tmax, cp[0], cp[1], cp[2], cp[3], cp[4], cp[5] };
                }
                g.Groups[k].Add(se);
                g.Indices[k].Add(i);
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
        private IDisplayDimension DimensionBetweenTwo(IModelDoc2 model, IDrawingDoc drawing, IView view, ISelectionMgr selMgr,
                                              Func<SelectData, bool> doSelect, double x, double y, bool horizontal,
                                              out bool bornWithoutObject)
        {
            bornWithoutObject = false;
            int before = CountDimensions(drawing);
            IDisplayDimension q = null;
            try
            {
                model.ClearSelection2(true);
                var sd = selMgr?.CreateSelectData() as SelectData;
                if (sd == null) return null;
                sd.View = (View)view;
                // ORIZZONTALE O VERTICALE DICHIARATA: IModelDoc2.AddDimension2 e'
                // vietata (niente direzione, voce GP6) e Extension.AddDimension con
                // due entita' selezionate non fa nascere niente (misurato).
                if (doSelect(sd))
                    q = (horizontal ? model.AddHorizontalDimension2(x, y, 0)
                                     : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
            }
            catch { }
            try { model.ClearSelection2(true); } catch { }
            if (NotGrown(drawing, before, q)) return null;
            bornWithoutObject = q == null;
            return q;
        }

        /// <summary>
        /// La quota riletta vale quanto la geometria (e, se e' un diametro, porta
        /// il simbolo)? Se no si toglie: un numero sbagliato e plausibile non
        /// resta in tavola.
        /// </summary>
        private static bool CorrectReread(IModelDoc2 model, IDisplayDimension q, double expectedValueMm, bool diameter,
                                          string what, List<string> failed)
        {
            double read = double.NaN;
            string prefix = "";
            try { read = ApiHelpers.ReadDimensionInConfiguration(q.GetDimension2(0) as IDimension, "") * 1000.0; } catch { }
            try { prefix = q.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
            bool correct = !double.IsNaN(read) && Math.Abs(read - expectedValueMm) <= 0.01 &&
                          (!diameter || prefix.Contains("<MOD-DIAM>"));
            if (correct) return true;
            bool removed = RemoveDimension(model, q);
            failed.Add(what + " (SOLIDWORKS ha scritto " +
                        (double.IsNaN(read) ? "?" : Math.Round(read, 3).ToString("0.###", Ci)) +
                        (diameter && !prefix.Contains("<MOD-DIAM>") ? " senza Ø" : "") +
                        (removed ? ": l'ho tolta)" : ": toglila)"));
            return false;
        }

        /// <summary>Gli estremi della faccia cilindrica lungo il suo asse, dai suoi cerchi.</summary>
        private static bool FaceEnds(IFace2 face, double[] cp, out double tmin, out double tmax)
        {
            tmin = double.MaxValue; tmax = double.MinValue;
            try
            {
                foreach (var oe in (face.GetEdges() as object[]) ?? new object[0])
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
        private static string CylinderKey(double diameterMm, double[] meta)
        {
            return "cil|" + Math.Round(diameterMm, 2).ToString("0.00", Ci) + "|" +
                   string.Join(",", meta.Take(3).Select(v => (Math.Round(v * 1000, 1) + 0.0).ToString("0.0", Ci)));
        }

        /// <summary>La chiave del cilindro di una faccia, o null se la faccia non e' un cilindro che si legge.</summary>
        private static string CylinderKey(IFace2 face)
        {
            try
            {
                var su = face?.GetSurface() as ISurface;
                if (su == null || !su.IsCylinder()) return null;
                var cp = su.CylinderParams as double[];
                double tmin, tmax;
                if (cp == null || cp.Length < 7 || !FaceEnds(face, cp, out tmin, out tmax)) return null;
                double tm = (tmin + tmax) / 2;
                return CylinderKey(cp[6] * 2000.0, new[] { cp[0] + tm * cp[3], cp[1] + tm * cp[4], cp[2] + tm * cp[5] });
            }
            catch { return null; }
        }

        /// <summary>
        /// La chiave del cilindro di cui il cerchio e' un bordo (stesso raggio), o
        /// null. Per co-spigoli e anelli, come ThroughAllFromGeometry: e' la strada
        /// che da fuori processo risponde.
        /// </summary>
        private static string CylinderCircleKey(IEdge e)
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
                    return CylinderKey(f);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Che faccia borda un cerchio del tondo: «plane» se una delle sue facce e' un
        /// piano perpendicolare all'asse (una testa, uno spallamento), altrimenti
        /// «cono» (uno smusso), «toro» (un raccordo) o «altro».
        /// </summary>
        private static string WhatBordersTheCircle(IEdge edge, double[] axis)
        {
            string found = "altro";
            try
            {
                foreach (var oc in (edge?.GetCoEdges() as object[]) ?? new object[0])
                {
                    var su = (((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2)?.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (su.IsPlane())
                    {
                        var pp = su.PlaneParams as double[];
                        if (pp != null && pp.Length >= 3 && Math.Abs(Dot(new[] { pp[0], pp[1], pp[2] }, axis)) > 0.9999) return "piano";
                    }
                    else if (su.IsCone()) found = "cono";
                    else if (su.IsTorus() && found != "cono") found = "toro";
                }
            }
            catch { }
            return found;
        }

        /// <summary>La chiave di una lunghezza del tondo: i centri delle due facce, nel modello.</summary>
        private static string RoundKey(double[] a, double[] b)
        {
            Func<double[], string> s = p => string.Join(",", p.Take(3).Select(v => Math.Round(v * 1000, 2).ToString("0.##", Ci)));
            var x = s(a); var y = s(b);
            return "tondo|" + (string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x);
        }

        /// <summary>Due cerchi (CircleParams) sullo stesso asse, con centri diversi?</summary>
        private static bool Coaxial(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 6 || b.Length < 6) return false;
            var axis = new[] { a[3], a[4], a[5] };
            if (Math.Abs(Dot(axis, new[] { b[3], b[4], b[5] })) < 0.9999) return false;
            var d = new[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
            double t = Dot(d, axis);
            return Math.Abs(t) > 1e-7 && Standard(Sub(d, Mul(axis, t))) < 1e-6;
        }

        private static double[] VectorOnSheet(double[] v, IMathUtility mu, MathTransform tr)
        {
            try { return ((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(tr)).ArrayData as double[]; }
            catch { return null; }
        }

        private static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        private static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        private static double[] Mul(double[] a, double k) { return new[] { a[0] * k, a[1] * k, a[2] * k }; }
        private static double Standard(double[] a) { return Math.Sqrt(Dot(a, a)); }
    }
}
