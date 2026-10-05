using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE SOVRASPORGENZE: cosa non si stampa senza supporti. (voce GP16)
    ///
    /// Una parete che pende nel vuoto oltre un certo angolo si stampa a
    /// filamenti persi. SOLIDWORKS non dice niente — il pezzo e' sano, il
    /// volume e' giusto, la geometria e' chiusa — e il difetto si vede solo
    /// sulla macchina, a stampa finita.
    ///
    /// ── LA NORMALE: MISURATA, NON CORRETTA A CASO ─────────────────────
    ///
    /// La voce avvertiva che la normale di `ISurface` e' quella della
    /// superficie sottostante e va girata quando `FaceInSurfaceSense()` e'
    /// falso, e che sbagliarlo da' *«un'analisi che dichiara supportata proprio
    /// la faccia che pende nel vuoto»*.
    ///
    /// Misurato il 16/9/2026 su un blocco di cui si sa tutto (100x100x20, sei
    /// facce con normali uscenti note):
    ///
    ///   IFace2.Normal esce dal materiale   6 volte su 6
    ///   Normal concorde con PlaneParams    1 volta su 6
    ///   FaceInSurfaceSense() = true        sulle 5 in cui sono OPPOSTE
    ///   FaceInSurfaceSense() = false       sulla 1 in cui sono concordi
    ///
    /// Due cose, e tutt'e due contano:
    ///
    ///   1. **`IFace2.Normal` e' gia' giusta.** Punta sempre fuori dal
    ///      materiale, e non va corretta con niente. E' quella che questo
    ///      progetto usa dappertutto, e adesso e' misurato invece che sperato.
    ///   2. **La regola «se il senso e' falso, gira» sarebbe stata sbagliata
    ///      al contrario**, e non su meta' delle facce ma su cinque sesti.
    ///      Qui il senso non si usa: si usa `Normal`.
    ///
    /// ── LE FACCE CURVE ────────────────────────────────────────────────
    ///
    /// Una faccia curva non ha UNA normale: il sotto di un cilindro
    /// orizzontale pende nel vuoto nella sua parte bassa e non nella parte
    /// alta. Percio' le facce si tassellano e si guarda triangolo per
    /// triangolo, pesando per area.
    ///
    /// Il verso dei triangoli lo decide l'avvolgimento, che e' una convenzione
    /// e non un fatto: **si controlla contro `IFace2.Normal` su tutte le facce
    /// PIANE**, dove la risposta giusta si conosce. Se non combacia, ci si
    /// rifiuta invece di rispondere con un'analisi rovesciata. E' un controllo
    /// che si porta dietro la sua stessa taratura.
    ///
    /// `GetTessTriangles` risponde **`float[]`, non `double[]`**: il cast
    /// sbagliato torna null in silenzio (nota 80zf del RAG).
    ///
    /// ── E QUELLO CHE STA SUL PIANO NON E' UNA SPORGENZA ───────────────
    ///
    /// La faccia di sotto guarda dritta in giu' — 90 gradi di sporgenza, il
    /// caso peggiore possibile — ma appoggia sul piano della macchina e si
    /// stampa benissimo. Contarla sarebbe dichiarare non stampabile ogni pezzo
    /// esistente.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Quanto si tollera sul piano di appoggio, in mm.</summary>
        private const double OnPlaneMm = 0.05;

        private Outcome CheckOverhangs(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double threshold = Num(a, "threshold_deg", 45);
            if (threshold <= 0 || threshold >= 90)
                return Failed(
                    "«threshold_deg» va fra 0 e 90 esclusi. E' l'angolo oltre il quale una " +
                    "parete non si regge: 45 per una stampante a filamento normale, 30-35 " +
                    "se vuoi stare largo, 60 se la macchina raffredda bene.");

            double[] su = Direction(a, "print_direction");
            if (su == null)
                return Failed(
                    "«print_direction» dev'essere tre numeri, per esempio [0, 0, 1]. " +
                    "E' il verso in cui cresce la stampa: il piano della macchina sta sotto, " +
                    "perpendicolare a questo.");

            var bodies = BodiesToCheck(model, a, docState, out string reason);
            if (bodies == null) return Failed(reason);
            int hiddenBodies = 0;
            foreach (var c in bodies)
            {
                try { if (!c.Visible) hiddenBodies++; } catch { }
            }

            // La quota del piano di appoggio: il punto piu' basso del pezzo
            // nella direzione di stampa.
            double bedLevel = double.MaxValue;
            foreach (var c in bodies)
            {
                var end = new double[3];
                if (c.GetExtremePoint(-su[0], -su[1], -su[2],
                                      out end[0], out end[1], out end[2]))
                {
                    double q = end[0] * su[0] + end[1] * su[1] + end[2] * su[2];
                    if (q < bedLevel) bedLevel = q;
                }
            }
            if (bedLevel == double.MaxValue)
                return Failed("Non riesco a trovare il punto piu' basso del pezzo.");

            double totalArea = 0, overhangingArea = 0, areaOnPlane = 0;
            double worse = 0;
            int checkedFaces = 0, overhangingFaces = 0, curvedFaces = 0;
            int unanalyzedFaces = 0;
            double unanalyzedArea = 0;
            // Planar faces that hang over the threshold but whose true box did
            // not answer: whether they rest on the plate is unknown, so they
            // count as not analysed (part of unanalyzedFaces), never as
            // overhangs and never as resting.
            int planarWithoutBox = 0;
            double planarWithoutBoxArea = 0;
            int consistent = 0, conflicting = 0;
            var list = new List<Dictionary<string, object>>();

            foreach (var body in bodies)
            {
                var f = body.GetFirstFace() as IFace2;
                while (f != null)
                {
                    var face = f;
                    f = f.GetNextFace() as IFace2;
                    checkedFaces++;

                    double[] normal = null;
                    try { normal = ApiHelpers.NormalIfPlanar(face); } catch { }
                    bool planar = false;
                    try { var s = face.GetSurface() as ISurface; planar = s != null && s.IsPlane(); }
                    catch { }
                    if (!planar) curvedFaces++;

                    double faceArea = 0;
                    try { faceArea = face.GetArea() * 1e6; } catch { }
                    totalArea += faceArea;

                    // ── i triangoli, e il controllo dell'avvolgimento ────
                    var tri = Triangles(face);
                    if (tri == null || tri.Count == 0)
                    {
                        // Niente tassellazione: si ripiega sulla normale, che
                        // per una faccia piana e' esatta. PER UNA CURVA NON C'E'
                        // (voce Z-C21.2, rilievo L12-2).
                        //
                        // IFace2~Normal.html, Remarks: «If the face is not a
                        // planar face, then this property returns 0,0,0.» Il
                        // commento che stava qui prometteva «e si dice», e il
                        // codice guardava solo `normale == null`: con (0,0,0)
                        // Prodotto dava 0, la faccia risultava «in su» e non
                        // veniva mai segnalata, qualunque fosse la sua
                        // pendenza. Se nessuna faccia tassellava, la risposta
                        // era «si stampa senza supporti» senza aver guardato
                        // niente. Adesso la faccia curva senza triangoli si
                        // CONTA come non analizzata, e il giudizio diventa non
                        // conclusivo (qui sotto).
                        if (!planar)
                        {
                            unanalyzedFaces++;
                            unanalyzedArea += faceArea;
                            continue;
                        }
                        if (normal == null) continue;
                        double degP = Overhang(normal, su);
                        bool downP = Product(normal, su) < 0;
                        bool? resting = downP && degP > threshold ? OnPlane(face, su, bedLevel) : true;
                        if (resting == null)
                        {
                            // S6: the true box replaced IFace2.GetBox, which always
                            // answered. A box that does not answer is not "not on
                            // the plate": it is a face this check did not look at.
                            unanalyzedFaces++;
                            unanalyzedArea += faceArea;
                            planarWithoutBox++;
                            planarWithoutBoxArea += faceArea;
                            continue;
                        }
                        if (resting == false)
                        {
                            overhangingFaces++;
                            overhangingArea += faceArea;
                            if (degP > worse) worse = degP;
                            Add(list, degP, faceArea, planar, face, su);
                        }
                        continue;
                    }

                    double downArea = 0;
                    double worseHere = 0;
                    foreach (var t in tri)
                    {
                        // Il verso dell'avvolgimento contro quello della
                        // faccia: si controlla dove la risposta si conosce.
                        if (planar && normal != null)
                        {
                            if (Product(t.Normal, normal) > 0) consistent++; else conflicting++;
                        }
                        if (Product(t.Normal, su) >= 0) continue;   // guarda in su
                        double deg = Overhang(t.Normal, su);
                        if (deg <= threshold) continue;
                        if (PointOnPlane(t.Center, su, bedLevel)) { areaOnPlane += t.Area; continue; }
                        downArea += t.Area;
                        if (deg > worseHere) worseHere = deg;
                    }

                    if (downArea > 0)
                    {
                        overhangingFaces++;
                        overhangingArea += downArea;
                        if (worseHere > worse) worse = worseHere;
                        Add(list, worseHere, downArea, planar, face, su);
                    }
                }
            }

            // ── IL CONTROLLO CHE SI PORTA DIETRO LA SUA TARATURA ────────
            //
            // Se l'avvolgimento dei triangoli non concorda con la normale
            // delle facce PIANE -- dove la risposta si conosce -- allora
            // l'analisi delle facce CURVE e' rovesciata, e non c'e' modo di
            // accorgersene guardando il risultato.
            if (consistent + conflicting > 0 && conflicting > 0)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "Non mi fido della mia stessa analisi: su {0} triangoli di facce piane, " +
                    "{1} hanno il verso opposto a quello della faccia. Vuol dire che " +
                    "l'avvolgimento della tassellazione qui non segue la regola che ho " +
                    "verificato, e allora sulle facce curve direi il contrario del vero " +
                    "senza che si veda. Non ti do un'analisi rovesciata.",
                    consistent + conflicting, conflicting));

            list = list.OrderByDescending(v => Convert.ToDouble(v["overhang_deg"])).ToList();
            const int howManyISay = 12;
            bool truncated = list.Count > howManyISay;

            var measurements = new Dictionary<string, object>
            {
                ["threshold_deg"] = threshold,
                ["print_direction"] = su.Select(x => Math.Round(x, 4)).ToArray(),
                ["faces_checked"] = checkedFaces,
                ["faces_curved"] = curvedFaces,
                ["faces_overhanging"] = overhangingFaces,
                ["overhang_worst_deg"] = Math.Round(worse, 2),
                ["area_overhanging_mm2"] = Math.Round(overhangingArea, 2),
                ["area_total_mm2"] = Math.Round(totalArea, 2),
                ["area_resting_on_plane_mm2"] = Math.Round(areaOnPlane, 2),
                // NON CONCLUSIVO E' UN TERZO VALORE, non un «si'» (rilievo
                // L12-2): con una faccia curva che non si e' potuta guardare,
                // «nessuna sporgenza trovata» non vuol dire «nessuna sporgenza».
                // Una sporgenza trovata resta un «no» anche cosi'.
                ["prints_without_supports"] = overhangingFaces > 0
                    ? (object)false
                    : unanalyzedFaces > 0 ? null : (object)true,
                ["overhangs"] = list.Take(howManyISay).ToArray(),
                ["triangles_checked"] = consistent + conflicting,
                // Nomi nuovi, in inglese come i prossimi del ponte.
                ["faces_not_analyzed"] = unanalyzedFaces,
                ["hidden_bodies"] = hiddenBodies,
            };
            if (unanalyzedFaces > 0)
                measurements["area_not_analyzed_mm2"] = Math.Round(unanalyzedArea, 2);
            if (planarWithoutBox > 0)
            {
                measurements["planar_faces_without_true_box"] = planarWithoutBox;
                measurements["planar_area_without_true_box_mm2"] = Math.Round(planarWithoutBoxArea, 2);
            }
            if (truncated)
            {
                measurements["overhangs_not_listed"] = list.Count - howManyISay;
                measurements["truncated"] = true;
            }

            int curvedNotAnalysed = unanalyzedFaces - planarWithoutBox;
            string notChecked = curvedNotAnalysed == 0 ? "" : string.Format(
                CultureInfo.InvariantCulture,
                " ATTENZIONE: {0} facce curve ({1:0.##} mm2) non si sono lasciate tassellare " +
                "e non le ho potute guardare: una faccia curva non ha una normale sola.",
                curvedNotAnalysed, unanalyzedArea - planarWithoutBoxArea);
            if (planarWithoutBox > 0)
                notChecked += string.Format(
                    CultureInfo.InvariantCulture,
                    " WARNING: {0} planar faces ({1:0.##} mm2) hang over the threshold, but their true " +
                    "bounding box could not be measured, so I cannot tell whether they rest on the plate.",
                    planarWithoutBox, planarWithoutBoxArea);
            measurements["verdict"] = overhangingFaces > 0
                ? string.Format(CultureInfo.InvariantCulture,
                    "NON si stampa senza supporti: {0} facce pendono oltre {1:0.#} gradi, " +
                    "la peggiore a {2:0.#}, per {3:0.##} mm2 su {4:0.##} di superficie. " +
                    "Gira il pezzo, o mettici i supporti, o smussa quelle facce.",
                    overhangingFaces, threshold, worse, overhangingArea, totalArea) + notChecked
                : unanalyzedFaces > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "NON POSSO DIRE se si stampa senza supporti con la soglia di {0:0.#} " +
                        "gradi: sulle facce che ho guardato non pende niente oltre.", threshold) +
                      notChecked +
                      " Riprova dopo una ricostruzione, o guarda quelle facce a occhio."
                    : string.Format(CultureInfo.InvariantCulture,
                        "Si stampa senza supporti con la soglia di {0:0.#} gradi: non c'e' " +
                        "nessuna superficie che pende oltre.", threshold);

            measurements["what_it_does_not_check"] =
                "Guardo l'angolo di ogni superficie rispetto alla direzione di stampa, e " +
                "basta. NON guardo: i ponti fra due appoggi (una traversa corta si stampa " +
                "anche se e' orizzontale), lo spessore delle pareti, le isole staccate che " +
                "nascono a mezz'aria, e se il pezzo sta nel volume della macchina. E quello " +
                "che appoggia sul piano non lo conto come sporgenza, perche' il piano lo " +
                "regge.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] sporgenze oltre {0:0.#} gradi: {1} facce, peggiore {2:0.#}, " +
                "{3:0.##} mm2", threshold, overhangingFaces, worse, overhangingArea));

            return SucceededWithoutCreating(measurements);
        }

        // =================================================================

        private sealed class Triangle
        {
            public double[] Normal;
            public double[] Center;
            public double Area;
        }

        /// <summary>
        /// I triangoli di una faccia, con normale, centro e area.
        ///
        /// `GetTessTriangles` risponde **float[]**: nove numeri per triangolo,
        /// tre vertici. Il cast a `double[]` torna null in silenzio — e' la
        /// trappola gia' documentata nel RAG, e qui e' scritta perche' chi
        /// tocca questo codice non la ripaghi.
        /// </summary>
        private static List<Triangle> Triangles(IFace2 face)
        {
            float[] raw;
            try { raw = face.GetTessTriangles(true) as float[]; }
            catch { return null; }
            if (raw == null || raw.Length < 9) return null;

            var outside = new List<Triangle>(raw.Length / 9);
            for (int i = 0; i + 8 < raw.Length; i += 9)
            {
                var ax = raw[i]; var ay = raw[i + 1]; var az = raw[i + 2];
                var bx = raw[i + 3]; var by = raw[i + 4]; var bz = raw[i + 5];
                var cx = raw[i + 6]; var cy = raw[i + 7]; var cz = raw[i + 8];

                double ux = bx - ax, uy = by - ay, uz = bz - az;
                double vx = cx - ax, vy = cy - ay, vz = cz - az;
                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-15) continue;   // triangolo degenere
                outside.Add(new Triangle
                {
                    Normal = new[] { nx / len, ny / len, nz / len },
                    Center = new[] { (ax + bx + cx) / 3.0, (ay + by + cy) / 3.0, (az + bz + cz) / 3.0 },
                    // meta' del modulo del prodotto vettoriale, in mm2
                    Area = len / 2.0 * 1e6,
                });
            }
            return outside;
        }

        /// <summary>
        /// Di quanto una superficie pende, in gradi, contati DALLA VERTICALE:
        /// una parete dritta e' 0, un soffitto orizzontale e' 90.
        ///
        /// E' la convenzione degli slicer -- «soglia 45 gradi» vuol dire quello
        /// -- e non quella dell'angolo col piano, che e' il complementare. Due
        /// numeri che si somigliano e dicono il contrario: qui si scrive quale
        /// dei due e', e il campo si chiama `overhang_deg`.
        /// </summary>
        private static double Overhang(double[] normal, double[] su)
        {
            double c = Math.Abs(Product(normal, su));
            if (c > 1) c = 1;
            return 90.0 - Math.Acos(c) * 180.0 / Math.PI;
        }

        private static double Product(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        /// <summary>
        /// Whether the face rests on the plate; null when its true box cannot
        /// be measured (the caller counts it as not analysed).
        /// </summary>
        private static bool? OnPlane(IFace2 face, double[] su, double bedLevel)
        {
            try
            {
                var b = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (b == null || b.Length < 6) return null;
                // l'angolo piu' alto della scatola, proiettato sulla direzione
                double up = double.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    double x = (i & 1) == 0 ? b[0] : b[3];
                    double y = (i & 2) == 0 ? b[1] : b[4];
                    double z = (i & 4) == 0 ? b[2] : b[5];
                    double q = x * su[0] + y * su[1] + z * su[2];
                    if (q > up) up = q;
                }
                return up - bedLevel < Units.M(OnPlaneMm);
            }
            catch { return null; }
        }

        private static bool PointOnPlane(double[] point, double[] su, double bedLevel)
        {
            double q = point[0] * su[0] + point[1] * su[1] + point[2] * su[2];
            return q - bedLevel < Units.M(OnPlaneMm);
        }

        private static void Add(
            List<Dictionary<string, object>> list, double deg, double area,
            bool planar, IFace2 face, double[] su)
        {
            var entry = new Dictionary<string, object>
            {
                ["overhang_deg"] = Math.Round(deg, 2),
                ["area_mm2"] = Math.Round(area, 2),
                ["planar"] = planar,
            };
            try
            {
                var b = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (b != null && b.Length >= 6)
                    entry["is_between_mm"] = new[]
                    {
                        Math.Round(Units.Mm(b[0]), 2), Math.Round(Units.Mm(b[1]), 2),
                        Math.Round(Units.Mm(b[2]), 2), Math.Round(Units.Mm(b[3]), 2),
                        Math.Round(Units.Mm(b[4]), 2), Math.Round(Units.Mm(b[5]), 2),
                    };
            }
            catch { }
            list.Add(entry);
        }

        private static double[] Direction(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null)
                return new double[] { 0, 0, 1 };
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var n = new List<double>();
            foreach (var x in e)
            {
                try { n.Add(Convert.ToDouble(x, CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            if (n.Count != 3) return null;
            double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (len < 1e-9) return null;
            return new[] { n[0] / len, n[1] / len, n[2] / len };
        }

        private List<IBody2> BodiesToCheck(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState,
            out string reason)
        {
            reason = null;
            var partDoc = model as IPartDoc;
            if (partDoc == null) { reason = "Questo non e' un documento di parte."; return null; }
            // TUTTI I CORPI, ANCHE QUELLI NASCOSTI (voce Z-C21.2, rilievo L12-1).
            //
            // IPartDoc~GetBodies2.html, parametro BVisibleOnly: «True gets only
            // the visible bodies, false gets all of the bodies in the part». Qui
            // c'era `true`: un corpo nascosto per guardare dentro il pezzo usciva
            // dall'analisi senza che niente lo dicesse, e «prints_without_supports»
            // poteva dire si' su un corpo che pende. Lo stato usa `false` da tempo
            // (DocumentState.cs, «e il false e' voluto»), e con `true` qui «C2»
            // indicava un corpo diverso da quello dello stato.
            //
            // La guida NON dice se `GetTessTriangles` risponde sulle facce di un
            // corpo nascosto. Per questo il ramo senza triangoli (rilievo L12-2)
            // conta le facce curve come non analizzate invece di saltarle: se il
            // corpo nascosto non tassella, il giudizio diventa «non posso dire»
            // e non torna il «si'» silenzioso. La mini-sonda che lo misura e'
            // ancora da lanciare (voce Z-C21.2, CHANGELOG).
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            var all = (rawItems ?? new object[0]).Select(o => o as IBody2).Where(b => b != null).ToList();
            if (all.Count == 0) { reason = "Il pezzo non ha corpi solidi da guardare."; return null; }

            string uid = Str(a, "body_uid", null);
            if (string.IsNullOrWhiteSpace(uid)) return all;

            // IL CORPO PER FIRMA, come in combine_bodies e move_bodies (schema S5,
            // rilievo L6-1): GetBodies2.html, «May vary the order in which bodies
            // are returned».
            var foundItems = BodiesFromUid(docState, new List<string> { uid.Trim() }, a, out reason);
            if (foundItems == null) return null;
            return new List<IBody2> { foundItems[0] };
        }
    }
}
