using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA NERVATURA. (voce W1.2, che era GP10)
    ///
    /// Una parete sottile senza irrigidimento si imbarca: l'angolo di una staffa
    /// a L, il fondo di una scatola stampata. La nervatura riempie, NEL PIANO
    /// dello schizzo, lo spazio fra una linea e le pareti del pezzo che quella
    /// linea (prolungata) incontra, e lo ispessisce di traverso.
    ///
    /// ── LA GUIDA API, E IL BOOLEANO CHE NESSUNO AVEVA GIRATO ─────────────
    ///
    /// Pagine della guida API (sldworksapi, estratta in locale), per nome:
    ///
    ///   * `SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~InsertRib.html`
    ///     — `void InsertRib(Is2Sided, ReverseThicknessDir, Thickness,
    ///     ReferenceEdgeIndex, ReverseMaterialDir, IsDrafted, DraftOutward,
    ///     DraftAngle, IsNormToSketch, IsDraftedFromWall)`. «IsNormToSketch:
    ///     True if extrusion direction is normal to sketch, false if parallel
    ///     to sketch»; «ReverseMaterialDir: True to flip the direction of the
    ///     extrusion»; «Is2Sided: ... a double-sided rib thickens on both sides
    ///     of the sketch». **Torna void**: la riuscita si misura sul pezzo.
    ///   * `Get_Rib_Feature_Data_Example_CSharp.htm` — l'unico esempio. Chiude
    ///     lo schizzo, lo seleziona per nome e tipo («Sketch2», "SKETCH", marca
    ///     0) e chiama `InsertRib(true, false, 0.00254, 0, false, false, true,
    ///     0.0174532925199433, false, false)`: il nono argomento,
    ///     IsNormToSketch, e' **false**. La nervatura cresce nel piano dello
    ///     schizzo, dalla linea verso le pareti. Poi legge la feature con
    ///     `GetSelectedObject6(1, -1)` e `GetDefinition` → `RibFeatureData2`.
    ///   * `...IRibFeatureData2~FlipSide.html` («True if the material is added
    ///     to the reverse side of the rib»), `~Thickness.html`,
    ///     `~IsTwoSided.html`, `~ExtrusionDirection.html`
    ///     (swRibExtrusionDirection_e): si rileggono e si riportano come
    ///     DICHIARATI dalla feature, accanto al volume che e' la misura.
    ///
    /// Le sonde storiche (NervaturaProva, SbloccoProva 1-3, 16/9/2026)
    /// passavano TUTTE IsNormToSketch = true — anche quelle che avevano gia'
    /// la L giusta con la diagonale nel vuoto — e non hanno mai girato il
    /// verso del materiale. Nota RAG 82zz-quinquies.
    ///
    /// ── LA SONDA DEL 28/9/2026 (`Checks\Rib\Program.cs`) ──
    ///
    /// Un pezzo nuovo per ogni riga; verita' = una feature NUOVA di tipo «Rib»
    /// piu' il delta di volume:
    ///
    ///   L 60x60x10 profonda 50, piano a z=25, linea (10,40)->(40,10), spessore 6
    ///     normale allo schizzo                  -> niente     (55000 -> 55000)
    ///     parallela, materiale dritto           -> «Nervatura1», +2700 = 450 mm2 x 6
    ///     parallela, materiale girato           -> niente
    ///     parallela, schizzo lasciato aperto    -> «Nervatura1», +2700
    ///   scatola 40x40x20 svuotata (2), piano destro, linea a y=12 da z=-15 a 15, spessore 2
    ///     parallela, materiale dritto           -> niente     (8672 -> 8672)
    ///     parallela, materiale girato           -> «Nervatura1», +720 = 36 x 10 x 2
    ///   piastra 40x40x2, stessa linea           -> niente in nessuno dei due versi
    ///
    /// **Il verso del materiale dipende dalla forma** (L: dritto; scatola:
    /// girato), e dal di fuori non c'e' una regola per saperlo prima. Quindi
    /// qui si prova il verso dritto e, se la nervatura non nasce, il girato;
    /// si tiene quello in cui nasce e il volume cresce. Nel verso sbagliato la
    /// sonda non ha mai visto nascere niente (nessuna feature, volume uguale):
    /// il tentativo fallito non lascia nulla da togliere se non lo schizzo, che
    /// serve al secondo tentativo.
    ///
    /// **Il secondo tentativo sullo STESSO pezzo non e' stato misurato dalla
    /// sonda** (ogni sua riga era un pezzo nuovo): lo misura il caso
    /// `rib_in_the_hollowed_box`, dove il primo verso fallisce e il
    /// secondo deve fare +720.
    ///
    /// La piastra senza pareti non fa nascere niente, e va bene cosi': dalla
    /// linea non c'e' niente da incontrare. Si rifiuta dicendolo, e nominando
    /// `extrude_on_face` per un rilievo su una faccia piana.
    ///
    /// ── COSA RESTA NEL PEZZO SE MI RIFIUTO ───────────────────────────────
    ///
    /// Niente. Il piano sfalsato (se l'ho fatto io), lo schizzo e l'eventuale
    /// nervatura nata storta si tolgono con `DeleteSelection2` (non con
    /// EditDelete, che apre una finestra di conferma e la chiamata COM non
    /// torna: vedi `DeleteFeature`), dal piu' giovane al piu' vecchio, e
    /// si rilegge il volume (`IsBackAsItWas` in OperazioniGuardie).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// Quanto puo' stare fuori dal piano un estremo della linea, in mm. Lo
        /// stesso margine di `sketched_bend`: cinque centesimi coprono gli
        /// arrotondamenti dello stato e restano lontani da qualunque errore vero.
        /// </summary>
        private const double RibOffPlaneMm = 0.05;

        /// <summary>
        /// Sotto questo volume «cresciuto» non e' una misura, e' rumore della
        /// lettura in metri cubi (`VolumeMm3`).
        /// </summary>
        private const double RibMinimumGrowthMm3 = 0.01;

        private Outcome Rib(IModelDoc2 model, Dictionary<string, object> a)
        {
            if (!(model is IPartDoc))
                return Failed("La nervatura si fa su un pezzo. Aprimi il pezzo.");

            // ── GLI ARGOMENTI, PRIMA DI TOCCARE IL PEZZO ─────────────────
            string plane = (Str(a, "plane", "") ?? "").Trim().ToLowerInvariant();
            if (plane != "front" && plane != "top" && plane != "right")
                return Failed(
                    "Dimmi «plane»: frontale, superiore o destro. E' il piano in cui sta la linea " +
                    "e in cui la nervatura cresce; con «distance_mm» lo sposto parallelo a se stesso " +
                    "(per esempio a meta' profondita' di una staffa).");
            // FACOLTATIVA, MA SE C'E' DEV'ESSERE UN NUMERO: `Num` inghiottirebbe
            // «25mm» e restituirebbe zero, cioe' la nervatura sul piano base.
            double distance = 0;
            if (a != null && a.TryGetValue("distance_mm", out var raw) && raw != null &&
                !RequiredNum(a, "distance_mm", out distance, out string distanceError))
                return Failed(distanceError + " Scrivila in mm, per esempio 25; senza, la nervatura sta " +
                               "sul piano base.");

            if (!RequiredNum(a, "thickness_mm", out double thickness, out string error))
                return Failed(error + " E' lo spessore della nervatura: scrivilo in mm (si divide a " +
                               "meta' dalle due parti del piano).");
            if (thickness <= 0)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«thickness_mm» dev'essere piu' di zero: {0:0.###} non e' una nervatura. Per una " +
                    "staffa di lamiera da 10 si usa di solito 4-6 mm.", thickness));

            var da = Triplet(a, "start_mm");
            var upTo = Triplet(a, "end_mm");
            if (da == null || upTo == null)
                return Failed(
                    "«start_mm» e «end_mm» sono i due estremi della linea della nervatura: punti [x, y, z] " +
                    "in mm nel sistema del modello, tutti e due sul piano indicato. Per esempio, " +
                    "nell'angolo di una L: da [10, 40, 25] a [40, 10, 25].");

            // I DUE PUNTI STANNO SUL PIANO? Il piano base «front» e' XY
            // (normale +Z), «top» XZ (+Y), «right» YZ (+X): la stessa
            // tabella di `References.PlaneNormal`, e il segno della
            // distanza e' quello di `create_plane` (frontale -250 -> z = -250,
            // misurato dal caso front_plane_minus_250).
            var normal = References.PlaneNormal(plane);
            Func<double[], double> dimension = p => p[0] * normal[0] + p[1] * normal[1] + p[2] * normal[2];
            double deviation = Math.Max(Math.Abs(dimension(da) - distance), Math.Abs(dimension(upTo) - distance));
            if (deviation > RibOffPlaneMm)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "La linea non sta sul piano {0} a distanza {1:0.###} mm: un estremo ne dista " +
                    "{2:0.###} mm. I due punti vanno presi sul piano della nervatura (sul {0} " +
                    "sfalsato di {1:0.###}, la coordinata {3} di tutti e due e' {1:0.###}); oppure " +
                    "cambia «distance_mm».",
                    plane, distance, deviation, plane == "front" ? "z" : plane == "top" ? "y" : "x"));
            double span = Math.Sqrt(Math.Pow(upTo[0] - da[0], 2) + Math.Pow(upTo[1] - da[1], 2) + Math.Pow(upTo[2] - da[2], 2));
            if (span < 1)
                return Failed("La linea della nervatura e' lunga meno di un millimetro: i due punti " +
                               "coincidono. Indica due estremi distinti.");

            // ── IL PEZZO DI PARTENZA ─────────────────────────────────────
            var photo = SnapshotPart(model);
            if (photo.Volume == null) return Failed(VolumeNotReadBefore("fare la nervatura"));
            if (photo.Bodies == 0)
                return Failed(
                    "Nel pezzo non c'e' un corpo solido: la nervatura riempie lo spazio fra una linea e " +
                    "pareti che ci sono gia'. Fai prima il pezzo (per esempio con «extrude_profile»).");
            var ribBefore = RibNamesOfType(model, "Rib");
            var sketchesBefore = RibNamesOfType(model, "ProfileFeature");

            // Cio' che creo io, dal piu' vecchio al piu' giovane: se mi
            // rifiuto si toglie al contrario.
            var mie = new List<IFeature>();

            // ── IL PIANO DELLO SCHIZZO ───────────────────────────────────
            IFeature sketchPlane;
            bool planeMadeHere = Math.Abs(distance) > 1e-9;
            if (!planeMadeHere)
            {
                sketchPlane = FindBasePlane(model, plane);
                if (sketchPlane == null)
                    return Failed("Non ho trovato il piano «" + plane + "» nell'albero.");
            }
            else
            {
                sketchPlane = RibPlaneOffset(model, plane, distance, out string planeReason);
                if (sketchPlane == null) return Failed(planeReason);
                mie.Add(sketchPlane);
            }

            // ── LO SCHIZZO: UNA LINEA, PASSATA PER ModelToSketchTransform ──
            //
            // Sul piano destro la x dello schizzo va in -Z del modello (RAG 81r):
            // non ci si ragiona, si trasforma. E' la ricetta della sonda e di
            // `sketched_bend`.
            model.ClearSelection2(true);
            if (!sketchPlane.Select2(false, 0))
                return Failed("Non sono riuscito a selezionare il piano dello schizzo." +
                               RibWithdraw(model, mie, photo));
            model.SketchManager.InsertSketch(true);
            var sk = model.SketchManager.ActiveSketch as ISketch;
            var math = _sw.GetMathUtility() as IMathUtility;
            if (sk == null || math == null)
            {
                try { if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(true); } catch { }
                // Everything born of that type goes back out, not just one.
                mie.AddRange(FeaturesBornOfType(model, "ProfileFeature", sketchesBefore));
                return Failed("Lo schizzo della nervatura non si e' aperto." + RibWithdraw(model, mie, photo));
            }
            var tr = sk.ModelToSketchTransform;
            Func<double[], double[]> inSketch = p =>
                ((IMathPoint)((IMathPoint)math.CreatePoint(new[] { Units.M(p[0]), Units.M(p[1]), Units.M(p[2]) }))
                    .MultiplyTransform(tr)).ArrayData as double[];
            var s0 = inSketch(da);
            var s1 = inSketch(upTo);
            object line = null;
            bool addBefore = model.SketchManager.AddToDB;
            try
            {
                model.SketchManager.AddToDB = true;
                line = model.SketchManager.CreateLine(s0[0], s0[1], 0, s1[0], s1[1], 0);
            }
            finally { model.SketchManager.AddToDB = addBefore; }
            model.SketchManager.InsertSketch(true);
            int sketchesBorn;
            var sketch = RibNewOfType(model, "ProfileFeature", sketchesBefore, out sketchesBorn);
            if (sketch != null) mie.Add(sketch);
            else mie.AddRange(FeaturesBornOfType(model, "ProfileFeature", sketchesBefore));
            if (line == null || sketch == null)
                return Failed(SketchNotIdentified(sketchesBorn, "La linea della nervatura non e' nata nello schizzo.") +
                               RibWithdraw(model, mie, photo));

            // L'ALBERO DOPO LO SCHIZZO: e' come deve essere prima di OGNI
            // tentativo. Un tentativo fallito che lascia una feature di un altro
            // tipo (non una «Rib») non la vede nessuno, e il secondo tentativo
            // le costruirebbe sopra. La sonda non l'ha mai visto succedere; se
            // succede non si prova l'altro verso, si ritira e si dice.
            var namesAfterTheSketch = RibNamesOfAll(model);

            // ── I DUE VERSI DEL MATERIALE ────────────────────────────────
            IFeature rib = null;
            bool hasRun = false;
            int tentativi = 0;
            var outcomes = new List<string>();
            foreach (bool flip in new[] { false, true })
            {
                tentativi++;
                string alzato = RibInsert(model, sketch, thickness, flip);
                try { model.EditRebuild3(); } catch { }
                int ribsBorn;
                var born = RibNewOfType(model, "Rib", ribBefore, out ribsBorn);
                string facing = flip ? "materiale girato" : "materiale dritto";
                if (born == null && ribsBorn > 1)
                {
                    // Two ribs from one call: which one is ours is not known, so
                    // none is kept and every one goes back out.
                    mie.AddRange(FeaturesBornOfType(model, "Rib", ribBefore));
                    return Failed(ribsBorn + " rib features were born from one InsertRib call (" + facing +
                                   "), and nothing tells which one is the rib asked for: none is kept." +
                                   RibWithdraw(model, mie, photo));
                }
                if (born == null)
                {
                    outcomes.Add(facing + ": nessuna nervatura" + (alzato == null ? "" : " (" + alzato + ")"));
                    var intruse = RibNewOfAll(model, namesAfterTheSketch);
                    if (intruse.Count > 0)
                    {
                        var intruderNames = new List<string>();
                        foreach (var f in intruse) { intruderNames.Add(RibName(f)); mie.Add(f); }
                        return Failed(
                            "Col " + facing + " la nervatura non e' nata, ma l'albero e' cresciuto lo stesso («" +
                            string.Join("», «", intruderNames) + "»): non provo l'altro verso sopra una " +
                            "lavorazione che non ho chiesto. Di solito la linea non sta fra due pareti: " +
                            "spostala nel vuoto, con gli estremi (o il loro prolungamento) sulle pareti da " +
                            "unire; per un rilievo su una faccia piana usa «extrude_on_face»." +
                            RibWithdraw(model, mie, photo));
                    }
                    continue;
                }
                // UNA NERVATURA NATA CHE NON AGGIUNGE MATERIALE. La sonda non
                // l'ha mai vista: nel verso sbagliato non nasceva niente. Se
                // succede non si prova l'altro verso (togliere la sola
                // nervatura lasciando lo schizzo non e' misurato): si ritira
                // tutto e si dice.
                mie.Add(born);
                double? after = VolumeMm3(model);
                if (after == null || after.Value - photo.Volume.Value < RibMinimumGrowthMm3)
                    return Failed(string.Format(CultureInfo.InvariantCulture,
                        "Con il {0} SOLIDWORKS ha fatto nascere «{1}», ma il volume del pezzo {2}: una " +
                        "nervatura che non aggiunge materiale non irrigidisce niente. Di solito la linea " +
                        "sta dentro il materiale invece che nel vuoto fra le pareti: prova a spostarla nel vuoto, " +
                        "con gli estremi (o il loro prolungamento) sulle pareti da unire.{3}",
                        facing, RibName(born),
                        after == null ? "non si rilegge" : "e' rimasto " + after.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3",
                        RibWithdraw(model, mie, photo)));
                rib = born;
                hasRun = flip;
                outcomes.Add(facing + ": nata «" + RibName(born) + "»");
                break;
            }

            if (rib == null)
                return Failed(
                    "La nervatura non e' nata in nessuno dei due versi del materiale (" +
                    string.Join("; ", outcomes) + "). Una nervatura riempie, nel piano dello schizzo, lo " +
                    "spazio fra la linea e le pareti del pezzo che la linea prolungata incontra: " +
                    "l'angolo di una L, il fondo e i fianchi di una scatola svuotata. Quando non nasce " +
                    "in nessun verso, di solito dalla linea non c'e' niente da incontrare: una piastra " +
                    "senza pareti (misurato), o una linea fuori dal pezzo. Per un rilievo su una faccia " +
                    "piana usa «extrude_on_face»; oppure sposta la linea fra due pareti." +
                    RibWithdraw(model, mie, photo));

            // ── LE GUARDIE, SUL PEZZO E NON SULLA FEATURE ────────────────
            bool rebuilt = true;
            try { rebuilt = model.EditRebuild3(); } catch { }
            string broken = WhatBroke(model, photo, rebuilt, aperture: false);
            if (broken != null)
                return Failed("La nervatura «" + RibName(rib) + "» e' nata, ma " + broken + "." +
                               RibWithdraw(model, mie, photo));

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            double? added = volumeAfter - photo.Volume;

            // COSA DICHIARA LA FEATURE (IRibFeatureData2), accanto a cio' che
            // si e' misurato. Non decide niente: lo spessore e il verso
            // dichiarati sono quelli chiesti anche quando il pezzo e' un altro
            // (la lezione dello sformo, `angle_declared_by_feature`).
            object declaredThickness = null, declaredFacing = null, declaredParallel = null;
            try
            {
                var d = rib.GetDefinition() as IRibFeatureData2;
                if (d != null)
                {
                    declaredThickness = Math.Round(Units.Mm(d.Thickness), 3);
                    declaredFacing = d.FlipSide;
                    declaredParallel = d.ExtrusionDirection == (int)swRibExtrusionDirection_e.swRibParallelToSketch;
                }
            }
            catch { }

            var measurements = new Dictionary<string, object>
            {
                // CHIESTI, E DETTI COSI': sono gli argomenti (e la lunghezza
                // della linea fatta con quelli), non misure. Le misure sono il
                // volume, i corpi, i tentativi e i «declared_*» riletti.
                ["requested_thickness_mm"] = Math.Round(thickness, 3),
                ["requested_sketch_plane"] = plane,
                ["requested_plane_offset_mm"] = Math.Round(distance, 3),
                ["requested_line_length_mm"] = Math.Round(span, 3),
                ["plane_created"] = planeMadeHere,
                ["material_side"] = hasRun ? "flipped" : "straight",
                ["attempts"] = tentativi,
                ["added_material_mm3"] = NumberOrNull(added, 2),
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["declared_thickness_mm"] = declaredThickness,
                ["declared_flip_side"] = declaredFacing,
                ["declared_parallel_to_sketch"] = declaredParallel,
                ["not_checked"] =
                    "Ho misurato che la nervatura e' nata e che il pezzo e' cresciuto, e che non ha rotto " +
                    "niente; non ho guardato QUALI pareti ha unito. Il verso del materiale l'ho provato io " +
                    "(prima dritto, poi girato) e ho tenuto il primo in cui nasce: se la linea sta fra " +
                    "pareti da tutte e due le parti, il verso dritto vince anche se tu pensavi l'altro. " +
                    "Nessuno sformo: la nervatura ha le facce dritte.",
            };
            if (volumeAfter == null) measurements["volume_not_reread"] = NoteVolumeNotReread;

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] nervatura {0} su {1}{2}: {3}, volume {4} -> {5}",
                RibName(rib), plane, planeMadeHere ? " a " + distance.ToString("0.###", CultureInfo.InvariantCulture) : "",
                string.Join("; ", outcomes), ReadValue(photo.Volume), ReadValue(volumeAfter)));

            // Nessuna direzione nell'esito: lo spessore va lungo la normale del
            // piano, ma le facce della nervatura non sono il «fondo» di niente.
            return Succeeded(rib, measurements);
        }

        // =================================================================

        /// <summary>
        /// La chiamata della guida, con i suoi dieci argomenti per nome.
        /// Null se non ha alzato, altrimenti il messaggio (`InsertRib` e' void:
        /// quello che conta e' l'albero dopo).
        ///
        /// Lo schizzo si riseleziona OGNI volta, per nome e tipo e con marca 0
        /// come nell'esempio della guida: un tentativo fallito puo' aver
        /// consumato la selezione.
        /// </summary>
        private static string RibInsert(IModelDoc2 model, IFeature sketch, double thicknessMm, bool flipMaterial)
        {
            try
            {
                model.ClearSelection2(true);
                string itemName = RibName(sketch);
                bool taken = false;
                try { taken = model.Extension.SelectByID2(itemName, "SKETCH", 0, 0, 0, false, 0, null, 0); } catch { }
                if (!taken) taken = sketch.Select2(false, 0);
                if (!taken) return "lo schizzo non si seleziona";
                model.FeatureManager.InsertRib(
                    Is2Sided: true,                 // spessore diviso a meta' dalle due parti del piano
                    ReverseThicknessDir: false,     // vale solo per una nervatura da un lato
                    Thickness: Units.M(thicknessMm),
                    ReferenceEdgeIndex: 0,
                    ReverseMaterialDir: flipMaterial,
                    IsDrafted: false,
                    DraftOutward: false,
                    DraftAngle: 0,
                    IsNormToSketch: false,          // PARALLELA allo schizzo: il nono argomento dell'esempio
                    IsDraftedFromWall: false);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { try { model.ClearSelection2(true); } catch { } }
        }

        /// <summary>
        /// The plane parallel to the base plane, offset by `distanceMm`: the
        /// same call as `create_plane` since Z-C21.7 (L8-1), IFeatureManager.
        /// InsertRefPlane with the base plane selected by name with mark 0
        /// (ApiHelpers.SelectOffsetPlaneBase / InsertOffsetPlane, where the
        /// guide pages and the numbers of the Z-C21.3 probe are: G1 top +100,
        /// G4 top -100, G6 front -250 all where they belong). Until then it was
        /// the obsolete CreatePlaneAtOffset3, the call of the rib probe
        /// (Checks/Rib, `CreatePlaneAtOffset3(0.025, false, true)`).
        /// The tree decides whether the plane was born, not the return value.
        /// </summary>
        private static IFeature RibPlaneOffset(IModelDoc2 model, string plane, double distanceMm, out string reason)
        {
            reason = null;
            var basePlane = FindBasePlane(model, plane);
            if (basePlane == null) { reason = "Non ho trovato il piano «" + plane + "» nell'albero."; return null; }
            if (!ApiHelpers.SelectOffsetPlaneBase(model, basePlane))
            {
                reason = "Non sono riuscito a selezionare il piano «" + plane + "»."; return null;
            }

            int planesBefore = CountPlanes(model);
            var namesBefore = FeatureNamesNow(model);
            IFeature returned = null;
            try
            {
                returned = ApiHelpers.InsertOffsetPlane(model, distanceMm) as IFeature;
            }
            catch (Exception ex) { reason = "Il piano della nervatura non e' nato: " + ex.Message; return null; }
            finally { try { model.ClearSelection2(true); } catch { } }

            // THE PLANE IS WHAT InsertRefPlane RETURNED, or else the ONE RefPlane
            // born since the call (FeatureBorn, scheme S7, Z-C21.5). Until then
            // it was the last feature of the tree, checked to be a RefPlane: on a
            // sheet-metal part that is the flat pattern, and a plane that was
            // born was refused and left in the tree.
            int planesAfter = CountPlanes(model);
            IFeature last = returned ?? FeatureBorn(model, namesBefore, "RefPlane", out _);
            string typ = "";
            try { typ = last?.GetTypeName2() ?? ""; } catch { }
            if (planesBefore < 0 || planesAfter != planesBefore + 1 || typ != "RefPlane")
            {
                reason = "Il piano della nervatura, parallelo al " + plane + " a " +
                         distanceMm.ToString("0.###", CultureInfo.InvariantCulture) +
                         " mm, non e' nato (piani " + planesBefore + " -> " + planesAfter + "). Puoi farlo prima " +
                         "con «create_plane» e guardare dove finisce.";
                return null;
            }
            return last;
        }

        /// <summary>
        /// Ritira cio' che questa operazione ha messo nel pezzo (dal piu'
        /// giovane al piu' vecchio) e restituisce la coda della frase di
        /// rifiuto, col volume riletto.
        ///
        /// `DeleteSelection2(swDelete_Absorbed)`: la nervatura si porta via lo
        /// schizzo che ha assorbito, lo schizzo le sue relazioni. Prima di
        /// toccare una feature si guarda che non abbia figli: se ne ha, qualcosa
        /// ci si e' appoggiato e non e' roba mia.
        /// </summary>
        private string RibWithdraw(IModelDoc2 model, List<IFeature> mie, PartPhoto before)
        {
            if (mie.Count == 0) return " Non ho toccato il pezzo: e' com'era.";
            bool everything = true;
            var names = new List<string>();
            // A SKETCH THAT A FAILED InsertRib HAS TOUCHED CANNOT BE DELETED, ONLY
            // SUPPRESSED. Measured on 28/9/2026 (probe cinque/probe_nervatura_ritira,
            // SOLIDWORKS 29.5.1): after two failed InsertRib on a plate with no walls
            // the rib sketch has no children and selects fine, yet
            // DeleteSelection2 answers false with swDelete_Absorbed, swDelete_Children
            // and 0, EditDelete leaves it and three EditUndo2 do not take it away.
            // A new sketch made after the failures deletes normally, so the lock is
            // on that sketch only. EditSuppress2 works (IsSuppressed read back true).
            // Such a sketch is suppressed and named in the answer, instead of
            // declaring a restore that did not happen (full bench on 0.141.0,
            // limit_rib_on_a_plate_without_walls).
            var suppressedOnes = new List<string>();
            for (int i = mie.Count - 1; i >= 0; i--)
            {
                string itemName = RibName(mie[i]);
                names.Add(itemName);
                if (string.IsNullOrEmpty(itemName) || FindFeature(model, itemName) == null) continue;   // assorbita da chi l'ha tolta prima
                try
                {
                    if (mie[i].IGetChildCount() > 0) { everything = false; continue; }
                    model.ClearSelection2(true);
                    if (!mie[i].Select2(false, 0)) { everything = false; continue; }
                    if (!model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed))
                    {
                        string typ = "";
                        try { typ = mie[i].GetTypeName2(); } catch { }
                        bool suppressed = false;
                        if (typ == "ProfileFeature")
                        {
                            model.ClearSelection2(true);
                            if (mie[i].Select2(false, 0))
                            {
                                try { model.EditSuppress2(); } catch { }
                                // IsSuppressed2 in the active configuration since Z-C21.7 (S3).
                                try { suppressed = ApiHelpers.IsSuppressedHere(mie[i]); } catch { }
                            }
                        }
                        if (suppressed) suppressedOnes.Add(itemName);
                        else everything = false;
                    }
                }
                catch (Exception ex)
                {
                    _log("[agente] nervatura: non riesco a togliere «" + itemName + "»: " + ex.Message);
                    everything = false;
                }
                finally { try { model.ClearSelection2(true); } catch { } }
            }
            try { model.EditRebuild3(); } catch { }
            // SI RILEGGE L'ALBERO, NON IL VALORE DI RITORNO.
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && !suppressedOnes.Contains(n) && FindFeature(model, n) != null) everything = false;
            names.Reverse();
            var removed = names.FindAll(n => !suppressedOnes.Contains(n));
            string queue = IsBackAsItWas(model, before, everything,
                "il pezzo come l'avevo trovato" + (removed.Count > 0 ? " (tolti «" + string.Join("», «", removed) + "»)" : ""));
            if (suppressedOnes.Count > 0)
                queue += " Lo schizzo «" + string.Join("», «", suppressedOnes) + "» resta nell'albero, SOPPRESSO: dopo una " +
                        "nervatura fallita SOLIDWORKS non lo lascia cancellare (misurato), solo sopprimere.";
            return queue;
        }

        /// <summary>I nomi delle feature di quel tipo, adesso.</summary>
        private static HashSet<string> RibNamesOfType(IModelDoc2 model, string typ)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == typ) names.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return names;
        }

        /// <summary>I nomi di tutte le feature dell'albero, adesso.</summary>
        private static HashSet<string> RibNamesOfAll(IModelDoc2 model)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    names.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return names;
        }

        /// <summary>Le feature che prima non c'erano, dalla piu' vecchia alla piu' giovane.</summary>
        private static List<IFeature> RibNewOfAll(IModelDoc2 model, HashSet<string> before)
        {
            var newOnes = new List<IFeature>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (!before.Contains(f.Name)) newOnes.Add(f);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return newOnes;
        }

        /// <summary>
        /// The ONE feature of that type whose name was not in <paramref name="before"/>,
        /// or null; <paramref name="born"/> says how many were born (-1: the tree
        /// was not read). Z-C21.5 (S7, review of the leftovers, 30/9/2026): this
        /// kept the LAST new one, so two born would have given one of them without
        /// a word; now it is <see cref="FeatureBorn"/>, which answers null for two.
        /// </summary>
        private static IFeature RibNewOfType(IModelDoc2 model, string typ, HashSet<string> before, out int born)
        {
            return FeatureBorn(model, before, typ, out born);
        }

        /// <summary>
        /// Every feature of that type whose name was not in <paramref name="before"/>,
        /// in tree order: what goes back out when a step is taken away. Empty when
        /// the tree cannot be read.
        /// </summary>
        private static List<IFeature> FeaturesBornOfType(IModelDoc2 model, string typ, HashSet<string> before)
        {
            var newOnes = new List<IFeature>();
            List<IFeature> features;
            List<string> names, types;
            if (before == null || !ReadFeatureTree(model, out features, out names, out types)) return newOnes;
            foreach (int i in BornFeatureIndices(names, before))
                if (i < types.Count && types[i] == typ) newOnes.Add(features[i]);
            return newOnes;
        }

        private static string RibName(IFeature f)
        {
            try { return f?.Name ?? ""; } catch { return ""; }
        }
    }
}
