using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE DUE DOMANDE CHE SI FANNO PRIMA DI ORDINARE. (voce GP4)
    ///
    /// «Quanto pesa questo pezzo in alluminio» e «entra nella scatola
    /// 300x260x40». Tutte e due si calcolavano gia' dentro `DocumentState` —
    /// la massa con `IMassProperty`, l'ingombro con `GetExtremePoint` sei
    /// volte — ma **nessuna operazione poteva chiederle**: arrivavano
    /// nell'intero stato del documento, o non arrivavano affatto.
    ///
    /// Qui diventano due domande che si possono fare da sole, per esempio
    /// dopo una modifica per verificare solo quella.
    ///
    /// ── THE UNITS ARE STATED, ALWAYS ────────────────────────────────────
    ///
    /// `UseSystemUnits = true` is written EXPLICITLY (ApiHelpers), but not
    /// because the values would otherwise come in the units of the document:
    /// IMassProperty~UseSystemUnits.html, Remarks, «The default value is True.
    /// Thus, system units (meters, radians, and kilograms) are used» (Z-C21.9,
    /// finding N74; the same words on IMassProperty2~UseSystemUnits.html). The
    /// line stays as a statement of the units the numbers are read in, at no
    /// cost. The answer is in metres and kilograms, and the conversion to
    /// millimetres is ours, in one place.
    ///
    /// ── LA TRAPPOLA DELLE DUE GRAFIE ────────────────────────────────────
    ///
    /// `IMassProperty` scrive **«Princip*le*»**:
    ///
    ///   IMassProperty.PrincipleMomentsOfInertia
    ///   IMassProperty.PrincipleAxesOfInertia
    ///
    /// `IMassProperty2` scrive **«Princip*al*»**:
    ///
    ///   IMassProperty2.PrincipalMomentsOfInertia
    ///   IMassProperty2.PrincipalAxesOfInertia
    ///
    /// Due grafie diverse per la stessa cosa, su due interfacce che si
    /// somigliano. Con l'interop tipizzato e' un errore di compilazione — e
    /// meno male; in late binding sarebbe un `MissingMemberException` a
    /// runtime, o peggio un `catch` che lo ingoia e una risposta senza i
    /// momenti che nessuno nota.
    ///
    /// E MISURANDOLA SI E' TROVATO DI PEGGIO: tutte e due le grafie, su un
    /// cubo di acciaio da 200.000 mm³, rispondono **tre zeri**. I momenti veri
    /// stanno in `Extension.GetMassProperties(1)`, posizioni 6, 7, 8. Un campo
    /// che si chiama «momenti principali» e risponde zero non e' un errore: e'
    /// un numero credibile, e nessun controllo lo prenderebbe. Qui non si
    /// riportano affatto.
    ///
    /// ── E LA MISURA CHE HA DECISO IL RIFIUTO ────────────────────────────
    ///
    /// Sullo stesso cubo da 200.000 mm³:
    ///
    ///   senza materiale        densita' 1000   massa 0,2 kg
    ///   con AISI 304           densita' 8000   massa 1,6 kg
    ///
    /// Senza materiale SOLIDWORKS **non fallisce**: risponde con la densita'
    /// predefinita del modello, che e' quella dell'acqua. Il numero e'
    /// credibile e sbagliato di OTTO VOLTE, e il posto in cui si scopre e' il
    /// preventivo. Percio' qui, senza materiale, la massa e' `null` con
    /// scritto perche'.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome MassProperties(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            // L'ASSIEME HA LA SUA STRADA (voce Z-D1): CreateMassProperty sull'assieme
            // intero, controllata contro la somma dei pezzi (OperazioniMisuraAssieme).
            if (model is IAssemblyDoc) return AssemblyMassProperties(model, a);
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("Questo documento non e' un pezzo ne' un assieme: non ha corpi da pesare. " +
                               "Aprimi il pezzo o l'assieme.");

            var bodies = ChosenBodies(partDoc, docState, a, Str(a, "body_uid", null), out string reason);
            if (bodies == null) return Failed(reason);

            // ── UNA MISURA NUOVA PER OGNI NUMERO, E NON E' PIGNOLERIA ────
            //
            // `IMassProperty` **cambia le sue risposte a seconda di che cosa
            // gli si e' chiesto prima**. Misurato il 15/9/2026 su un cubo di
            // AISI 304 da 200.000 mm³, con lo stesso oggetto:
            //
            //   letto subito `Density`                  ->  8000   (giusto)
            //   letto prima `Volume`, poi `Density`     ->     1
            //   letto prima `SurfaceArea`, poi `Density`->     1
            //   letto prima `CenterOfMass`, poi `Density`-> 8000   (indenne)
            //
            // E la massa segue la densita': 0,0002 kg invece di 1,6 — OTTOMILA
            // VOLTE piu' leggera, con volume, area e baricentro tutti giusti
            // accanto. Nessuna eccezione, nessun valore assurdo: un numero
            // credibile, e il posto in cui si scopre e' il preventivo.
            //
            // Si potrebbe scrivere «leggi prima la massa», ma un ordine di
            // righe e' un contratto che si rompe il giorno in cui qualcuno le
            // riordina — e non fallirebbe niente. Un oggetto per numero non si
            // rompe: costa quattro chiamate COM, che non le sente nessuno.
            var take = new Func<Func<IMassProperty2, object>, string, object>((read, what) =>
            {
                try
                {
                    // S2: a new object in system units of the bodies asked for.
                    // Since Z-C21.7 CreateMassProperty2 with the bodies as
                    // SelectedItems, hidden ones included, and Recalculate
                    // (ApiHelpers.NewPartMassProperty, with the probe numbers).
                    // Without the bodies read back the measure is not of the
                    // part one believes. The order trap above belongs to the
                    // obsolete IMassProperty: the probe found IMassProperty2
                    // free of it (O1: density 8000 read after Volume, O0 with
                    // the old object: 1). One object per number stays anyway.
                    var mp = ApiHelpers.NewPartMassProperty(model, bodies, out bool attached);
                    if (mp == null || !attached) return null;
                    return read(mp);
                }
                catch { return null; }
            });

            object m = take(p => p.Mass, "massa");
            object d = take(p => p.Density, "densita");
            object v = take(p => p.Volume, "volume");
            object s = take(p => p.SurfaceArea, "area");
            object c = take(p => p.CenterOfMass, "baricentro");

            if (m == null || d == null || v == null || s == null)
                return Failed(
                    "SOLIDWORKS non ha dato le proprieta' di massa: o non ha agganciato i " +
                    "corpi, o la misura non e' riuscita. Non ti do numeri a meta'.");

            double massKg = Convert.ToDouble(m);
            double density = Convert.ToDouble(d);
            double volumeM3 = Convert.ToDouble(v);
            double areaM2 = Convert.ToDouble(s);
            var center = Scale(c as double[], 1000.0);

            string material = PartMaterial(model);

            var measurements = new Dictionary<string, object>
            {
                ["bodies"] = bodies.Count,
                ["volume_mm3"] = Math.Round(volumeM3 * 1e9, 2),
                ["area_mm2"] = Math.Round(areaM2 * 1e6, 2),
                ["center_of_mass_mm"] = center,
                // LE UNITA' NELLA RISPOSTA, non solo nei nomi dei campi. E' la
                // stessa ragione dello STL: chi legge puo' non aver mai visto
                // la descrizione dell'operazione.
                ["units"] = "millimetri e chilogrammi; la densita' in kg/m3",
            };

            // LA MASSA SENZA MATERIALE NON E' ZERO, E' «NON SI SA».
            //
            // SOLIDWORKS risponde lo stesso, con la densita' predefinita del
            // modello — di solito 1000 kg/m3, cioe' acqua. Un pezzo d'acciaio
            // che pesa come l'acqua e' un numero credibile e sbagliato di otto
            // volte, e il posto in cui si scopre e' il preventivo.
            if (string.IsNullOrEmpty(material))
            {
                measurements["material"] = null;
                measurements["mass_kg"] = null;
                measurements["density_kg_m3"] = null;
                measurements["why_no_mass"] =
                    "Al pezzo non e' stato dato nessun materiale, quindi la massa non si sa. " +
                    "SOLIDWORKS risponderebbe lo stesso usando la densita' predefinita del " +
                    "modello (di solito 1000 kg/m3, cioe' acqua): un numero credibile e " +
                    "sbagliato di otto volte su un pezzo d'acciaio. Usa «apply_material» " +
                    "e richiedimelo.";
            }
            else
            {
                measurements["material"] = material;
                measurements["mass_kg"] = Math.Round(massKg, 4);
                measurements["density_kg_m3"] = Math.Round(density, 2);
                // IL CONTO RIFATTO A MANO, accanto a quello di SOLIDWORKS. Se
                // i due divergono, una delle due misure e' in unita' diverse
                // da quelle che dice — ed e' il modo in cui un fattore 1000 si
                // fa vedere invece di nascondersi.
                double byHand = volumeM3 * density;
                measurements["mass_recomputed_kg"] = Math.Round(byHand, 4);
                measurements["deviation_on_recompute"] = massKg > 0
                    ? (object)Math.Round(Math.Abs(byHand - massKg) / massKg, 6)
                    : null;
            }

            // ── I MOMENTI PRINCIPALI NON CI SONO, ED E' MISURATO ──────────
            //
            // `IMassProperty.PrincipleMomentsOfInertia` risponde **tre zeri**
            // su un cubo di acciaio da 200.000 mm³ — e cosi' fa
            // `IMassProperty2.PrincipalMomentsOfInertia`, con l'altra grafia.
            // I momenti veri ci sono, ma da un'altra parte:
            // `Extension.GetMassProperties(1)` torna tredici numeri e i
            // momenti stanno nelle posizioni 6, 7 e 8 (0,001387, 0,001387,
            // 0,002667 kg·m² sullo stesso cubo).
            //
            // Tre zeri in un campo che si chiama «momenti principali» sono il
            // difetto che non da' errore: un numero credibile — un momento
            // nullo e' un momento — che nessun controllo prenderebbe. Quindi
            // NON si riportano: nessuno li ha chiesti, e riportarli sbagliati
            // e' peggio che non averli. Se un giorno servissero, la via e'
            // `GetMassProperties`, non la proprieta'.

            _log("[agente] proprieta' di massa: volume " +
                 (volumeM3 * 1e9).ToString("0.#") + " mm3" +
                 (string.IsNullOrEmpty(material) ? ", senza materiale" : ", " + material +
                  ", " + massKg.ToString("0.###") + " kg"));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// LA SCATOLA VERA DEL PEZZO, corpo per corpo e tutta insieme.
        ///
        /// `GetExtremePoint` sei volte, NON `GetBodyBox`. `GetBodyBox` e'
        /// dichiaratamente APPROSSIMATA per le superfici non piane — puo'
        /// essere piu' grande del pezzo — e un ingombro dichiarato piu' grande
        /// del vero fa dimensionare male tutto quello che ci sta intorno: la
        /// scatola per spedirlo, il pezzo che gli va accanto, la tasca in cui
        /// deve entrare.
        ///
        /// E' la stessa scelta gia' fatta in `DocumentState`, e sta scritta
        /// in tutti e due i posti perche' e' il genere di cosa che qualcuno
        /// «semplifica» leggendo solo uno dei due.
        /// </summary>
        private Outcome BodyBoundingBox(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (model is IAssemblyDoc) return AssemblyBoundingBox(model, a);   // voce Z-D1
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("Questo documento non e' un pezzo ne' un assieme: non ha corpi da misurare. " +
                               "Aprimi il pezzo o l'assieme.");

            string askedUid = Str(a, "body_uid", null);
            var bodies = ChosenBodies(partDoc, docState, a, askedUid, out string reason);
            if (bodies == null) return Failed(reason);
            if (bodies.Count == 0)
                return Failed("Il pezzo non ha nessun corpo solido da misurare.");

            var list = new List<object>();
            double[] everything = null;
            for (int i = 0; i < bodies.Count; i++)
            {
                var box = BodyBox(bodies[i] as IBody2);
                if (box == null)
                    return Failed(
                        "Non sono riuscito a leggere l'ingombro del corpo " + (i + 1) +
                        ": GetExtremePoint non ha risposto.");
                list.Add(new Dictionary<string, object>
                {
                    // The body asked for keeps its own uid: numbering the list
                    // of one body said «C1» of the body «C2» (Z-C21.5, S5).
                    ["body"] = string.IsNullOrWhiteSpace(askedUid) ? "C" + (i + 1) : askedUid.Trim().ToUpperInvariant(),
                    ["from_mm"] = new[] { Round(box[0]), Round(box[1]), Round(box[2]) },
                    ["to_mm"] = new[] { Round(box[3]), Round(box[4]), Round(box[5]) },
                    ["bounding_box_mm"] = new[]
                    {
                        Round(box[3] - box[0]),
                        Round(box[4] - box[1]),
                        Round(box[5] - box[2]),
                    },
                });
                if (everything == null) everything = (double[])box.Clone();
                else
                {
                    for (int k = 0; k < 3; k++) everything[k] = Math.Min(everything[k], box[k]);
                    for (int k = 3; k < 6; k++) everything[k] = Math.Max(everything[k], box[k]);
                }
            }

            var measurements = new Dictionary<string, object>
            {
                ["bodies"] = list.ToArray(),
                ["from_mm"] = new[] { Round(everything[0]), Round(everything[1]), Round(everything[2]) },
                ["to_mm"] = new[] { Round(everything[3]), Round(everything[4]), Round(everything[5]) },
                ["bounding_box_mm"] = new[]
                {
                    Round(everything[3] - everything[0]),
                    Round(everything[4] - everything[1]),
                    Round(everything[5] - everything[2]),
                },
                ["units"] = "millimetri, nel sistema di coordinate del modello",
                // IL LIMITE DICHIARATO: e' la scatola ALLINEATA AGLI ASSI. Un
                // pezzo lungo messo in diagonale ha una scatola molto piu'
                // grande di quanto serva per imballarlo, e questa misura non
                // dice come girarlo.
                ["what_it_does_not_check"] =
                    "E' la scatola allineata agli assi del modello, non la piu' piccola " +
                    "possibile: un pezzo messo in diagonale ha un ingombro molto piu' " +
                    "grande di quello che gli servirebbe girato bene.",
            };
            _log("[agente] ingombro: " +
                 string.Join(" x ", ((double[])measurements["bounding_box_mm"]).Select(x => x.ToString("0.##"))) +
                 " mm su " + bodies.Count + " corpi");
            return SucceededWithoutCreating(measurements);
        }

        // =================================================================
        // QUANTO MATERIALE RESTA FRA QUESTE DUE FACCE  (voce GP5)
        // =================================================================
        //
        // E' la domanda dello spessore di parete — «quanto resta fra la cava
        // della guarnizione e la faccia esterna?» — fatta in un modo che si
        // puo' controllare: due facce NOMINATE e un numero, invece di tarare
        // un vettore di intersezioni a raggi.
        //
        // ── LE DUE COSE MISURATE PRIMA DI SCRIVERLA ─────────────────────
        //
        // 1. **`ClosestDistance` torna METRI.** Su una piastra 40x40x10, fra
        //    le due facce grandi, risponde `0,01`. Un fattore mille sbagliato
        //    darebbe 10 dove il vero e' 0,01 — e tutti e due si leggono come
        //    millimetri plausibili.
        //
        // 2. **I due punti che restituisce sono veri**, e distano esattamente
        //    quanto dichiara: [0, 0, 10] e [0, 0, 0] mm, scarto zero. E'
        //    l'unico controllo indipendente che si abbia sul numero, e qui si
        //    fa sempre: se il valore dice una cosa e i suoi stessi punti ne
        //    dicono un'altra, la chiamata e' fallita e ha risposto lo stesso.
        // =================================================================

        private Outcome MinimumDistance(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidA = Str(a, "start_uid", null);
            string uidB = Str(a, "end_uid", null);
            if (string.IsNullOrWhiteSpace(uidA) || string.IsNullOrWhiteSpace(uidB))
                return Failed(
                    "Mi servono due cose fra cui misurare: «start_uid» e «end_uid». Gli " +
                    "identificativi di facce e spigoli stanno nello stato del documento.");
            if (string.Equals(uidA, uidB, StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "«start_uid» e «end_uid» sono la stessa cosa (" + uidA + "): la distanza " +
                    "di una faccia da se stessa e' zero, e non e' una misura.");

            string reason;
            object entA = EntityFromState(docState, uidA, out reason);
            if (entA == null) return Failed(reason);
            object entB = EntityFromState(docState, uidB, out reason);
            if (entB == null) return Failed(reason);

            double meters;
            object p1, p2;
            try { meters = model.ClosestDistance(entA, entB, out p1, out p2); }
            catch (Exception ex) { return Failed("La misura non e' riuscita: " + ex.Message); }

            // «NO SOLUTION» IS SAID AS SUCH (Z-C21.8, finding L12-9).
            // IModelDoc2~ClosestDistance.html: «Distance in meters between Point1
            // and Point2; -1.0 if no solution». The check on the two points below
            // refuses it too (a distance between two points is never negative,
            // so the gap is at least a metre), but with a sentence about a
            // distance of «-1000 mm» contradicted by its own points, or about
            // points that are missing, which sends the reader to the wrong place.
            if (meters < 0)
                return Failed(
                    "SOLIDWORKS found no closest distance between «" + uidA + "» and «" + uidB +
                    "»: ClosestDistance answered " + meters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    ", which its guide page gives as «no solution». Check that both are faces, edges or vertices " +
                    "of the model, and ask again with the identifiers read from the state now.");

            var pointA = p1 as double[];
            var pointB = p2 as double[];
            if (pointA == null || pointB == null || pointA.Length < 3 || pointB.Length < 3)
                return Failed(
                    "SOLIDWORKS ha dato la distanza (" + (meters * 1000).ToString("0.###") +
                    " mm) ma non i due punti fra cui l'ha misurata. Senza quelli il numero " +
                    "non si puo' controllare, e non te lo do.");

            // IL CONTROLLO INDIPENDENTE, e non e' uno scrupolo: se il valore
            // dice 0 e i punti distano 4 mm, la chiamata e' fallita e ha
            // risposto lo stesso.
            double betweenThePoints = Math.Sqrt(
                Math.Pow(pointA[0] - pointB[0], 2) +
                Math.Pow(pointA[1] - pointB[1], 2) +
                Math.Pow(pointA[2] - pointB[2], 2));
            double deviationMm = Math.Abs(betweenThePoints - meters) * 1000.0;
            if (deviationMm > 0.001)
                return Failed(
                    "SOLIDWORKS dice che la distanza e' " + (meters * 1000).ToString("0.####") +
                    " mm, ma i due punti che ha dato ne distano " +
                    (betweenThePoints * 1000).ToString("0.####") + ". Il numero e' smentito dai suoi " +
                    "stessi punti: non te lo do.");

            var measurements = new Dictionary<string, object>
            {
                ["source"] = uidA,
                ["target"] = uidB,
                ["distance_mm"] = Round(meters * 1000.0),
                ["point_source_mm"] = Scale(pointA, 1000.0),
                ["point_target_mm"] = Scale(pointB, 1000.0),
                // SI RIPORTA ANCHE IL CONTROLLO, non solo il suo esito: chi
                // legge vede che il numero e' stato verificato e con che
                // scarto, invece di doverci credere.
                ["distance_between_points_mm"] = Round(betweenThePoints * 1000.0),
                ["deviation_of_check_mm"] = Round(deviationMm),
                ["units"] = "millimetri; SOLIDWORKS li da' in metri e la conversione e' qui",
            };
            if (meters * 1000.0 < 0.001)
                measurements["touch"] =
                    "Le due cose si toccano o si intersecano: la distanza e' zero, e i due " +
                    "punti sono lo stesso punto.";

            _log("[agente] distanza minima " + uidA + " -> " + uidB + ": " +
                 (meters * 1000).ToString("0.###") + " mm");
            return SucceededWithoutCreating(measurements);
        }

        // =================================================================
        // LA MISURA COMPLETA FRA DUE ENTITA'  (voce GP5)
        // =================================================================
        //
        // ── `Calculate(entita)` IGNORA L'ARGOMENTO ──────────────────────
        //
        // `IMeasure.Calculate(Object Entities)` **prende un argomento e non lo
        // guarda**: misura quello che e' SELEZIONATO. Misurato il 15/9/2026:
        //
        //   Calculate(new object[] { faceA, faceB })   ->  FALSE
        //   due facce selezionate, poi Calculate(null)     ->  TRUE, 0,01
        //
        // E peggio: chiamata con UNA faccia nell'array, mentre la selezione
        // ne conteneva ancora DUE dalla prova prima, ha risposto TRUE e ha
        // dato l'area di tutte e due (0,0032 m² invece di 0,0016). Un numero
        // credibile, del pezzo giusto, di un'altra selezione.
        //
        // Quindi: si svuota la selezione, si selezionano le entita' a mano, si
        // chiama `Calculate(null)` e **si guarda il booleano**.
        //
        // ── E `-1` NON E' UNA MISURA ────────────────────────────────────
        //
        // `Perimeter` su una faccia piana quadrata risponde **-1**: e' il modo
        // di SOLIDWORKS di dire «questa cosa qui non ce l'ha». Un perimetro
        // negativo di un millesimo di millimetro riportato come numero e' il
        // difetto che non da' errore; qui i valori negativi diventano `null`.
        // =================================================================

        private Outcome MeasureBetween(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var uid = new List<string>();
            foreach (var key in new[] { "uid", "uid2" })
            {
                string u = Str(a, key, null);
                if (!string.IsNullOrWhiteSpace(u)) uid.Add(u);
            }
            if (uid.Count == 0)
                return Failed(
                    "Mi serve almeno una cosa da misurare: «uid» (e «uid2» per misurare " +
                    "FRA due). Gli identificativi di facce e spigoli stanno nello stato " +
                    "del documento.");
            if (uid.Count > 2)
                return Failed(
                    "Ne hai date " + uid.Count + ": ne misuro una sola o due. Con piu' di " +
                    "due, «distanza» e «fillet» non vogliono dire piu' niente.");

            string arcMeasure = (Str(a, "arc_measure", "centre") ?? "centre").Trim().ToLowerInvariant();
            int arcOption;
            if (!ArcMeasureOptions.TryGetValue(arcMeasure, out arcOption))
                return Failed(
                    "«arc_measure» is «centre», «minimum» or «maximum», not «" + arcMeasure + "»: it says how " +
                    "a distance to an arc or a circle is taken (centre to centre, between the nearest points, " +
                    "between the farthest points).");

            var entity = new List<object>();
            foreach (var u in uid)
            {
                string reason;
                var e = EntityFromState(docState, u, out reason);
                if (e == null) return Failed(reason);
                entity.Add(e);
            }

            IMeasure measure;
            try { measure = model.Extension.CreateMeasure() as IMeasure; }
            catch (Exception ex) { return Failed("Non riesco a preparare la misura: " + ex.Message); }
            if (measure == null)
                return Failed("Questa installazione non espone lo strumento di misura.");

            // THE ARC OPTION IS SET, NEVER INHERITED (finding M29). Measured on
            // 28/9/2026 by probe_measure_arc_option (plate 100 x 60 x 10, holes
            // D10 at x = -25 and D20 at x = +25): between the
            // two cylindrical faces Distance is 50 with ArcOption 0, 35 with 1 and
            // 65 with 2, while CenterDistance stays 50; a fresh IMeasure reads 0/0
            // and the option set on one measure does not stick to the next. Up to
            // library 0.141.0 it was never set: the distance was centre to centre
            // and nothing said so. IMeasure~ArcOption.html: "How to measure an arc
            // or circle as defined by swMeasureArcCircleOption_e"; the guide
            // example (Measure_Selected_Entities_Example_CSharp.htm) sets it before
            // Calculate. ProjectionOption goes to 0 (swMeasureProjectOn_None,
            // IMeasure~ProjectionOption.html) for the same reason: the default
            // measured 0, and the same distance, but it is ours to say.
            try
            {
                measure.ArcOption = arcOption;
                measure.ProjectionOption = (int)swMeasureProjectOnOption_e.swMeasureProjectOn_None;
            }
            catch (Exception ex) { return Failed("The measure did not take the arc option: " + ex.Message); }

            bool succeeded;
            try
            {
                // LA SELEZIONE E' L'ARGOMENTO VERO. Vedi il commento qui sopra.
                model.ClearSelection2(true);
                for (int i = 0; i < entity.Count; i++)
                {
                    var ent = entity[i] as IEntity;
                    if (ent == null || !ent.Select4(i > 0, null))
                        return Failed(
                            "Non sono riuscito a selezionare «" + uid[i] + "»: senza la " +
                            "selezione la misura guarderebbe qualcos'altro.");
                }
                succeeded = measure.Calculate(null);
            }
            catch (Exception ex) { return Failed("La misura non e' riuscita: " + ex.Message); }
            finally { try { model.ClearSelection2(true); } catch { } }

            if (!succeeded)
                return Failed(
                    "Lo strumento di misura ha risposto «no» su " +
                    string.Join(" e ", uid.Select(u => "«" + u + "»")) +
                    ". Non ti do i numeri che aveva dentro: sarebbero quelli di prima.");

            var outside = new Dictionary<string, object>
            {
                ["measured"] = uid.ToArray(),
                ["units"] = "millimetri, millimetri quadri e gradi; SOLIDWORKS li da' in " +
                            "metri e radianti, e la conversione e' qui",
            };
            // ── `Area` E `TotalArea` NON SONO LA STESSA COSA, E SI ALTERNANO
            //
            // Misurato il 15/9/2026 su una piastra 40x40x10:
            //
            //   una faccia sola    Area = 1600 mm²    TotalArea = -1
            //   due facce          Area = -1          TotalArea = 3200 mm²
            //
            // Cioe' quella che vale e' sempre l'altra. Chi ne legge una sola
            // ha un campo che sparisce a seconda di quante cose ha chiesto —
            // ed e' quello che e' successo: il caso «area di una faccia sola»
            // e' uscito con «campo assente: misure.area_mm2». Si riportano
            // tutte e due, col nome che dice quale e' quale.
            Put(outside, "area_mm2", measure.Area, 1e6);
            Put(outside, "area_total_mm2", measure.TotalArea, 1e6);
            Put(outside, "perimetro_mm", measure.Perimeter, 1000.0);
            Put(outside, "length_mm", measure.TotalLength, 1000.0);
            Put(outside, "radius_mm", measure.Radius, 1000.0);
            Put(outside, "diameter_mm", measure.Diameter, 1000.0);
            if (uid.Count == 2)
            {
                // Read back after Calculate: the probe read the value it had set.
                // If SOLIDWORKS answered with another option, «distance_mm» would
                // be another kind of distance under the name the caller asked for.
                int arcOptionRead = measure.ArcOption;
                if (arcOptionRead != arcOption)
                    return Failed(
                        "I asked the measure for «arc_measure» " + arcMeasure + " (ArcOption " + arcOption +
                        ") and it reads back ArcOption " + arcOptionRead + ": the distance would be another " +
                        "kind of distance, and I do not give it.");
                outside["arc_measure"] = arcMeasure;
                outside["projection"] = "none";
                Put(outside, "distance_mm", measure.Distance, 1000.0);
                Put(outside, "distanza_normale_mm", measure.NormalDistance, 1000.0);
                Put(outside, "distance_between_centers_mm", measure.CenterDistance, 1000.0);
                // I DELTA POSSONO ESSERE NEGATIVI PER DAVVERO: un delta e' una
                // differenza, e -20 vuol dire «venti dall'altra parte». Qui
                // `-1` non e' un segnale, quindi non si filtra.
                outside["delta_mm"] = new[]
                {
                    Round(measure.DeltaX * 1000.0),
                    Round(measure.DeltaY * 1000.0),
                    Round(measure.DeltaZ * 1000.0),
                };
                outside["parallel"] = measure.IsParallel;
                outside["perpendicular"] = measure.IsPerpendicular;
                outside["intersect"] = measure.IsIntersect;
                double deg = measure.Angle * 180.0 / Math.PI;
                if (deg >= 0) outside["angle_deg"] = Round(deg);
            }

            _log("[agente] misura fra " + string.Join(" e ", uid) +
                 (outside.ContainsKey("distance_mm") ? ": " + outside["distance_mm"] + " mm" : ""));
            return SucceededWithoutCreating(outside);
        }

        /// <summary>
        /// The values of «arc_measure» and the IMeasure.ArcOption each one sets
        /// (swMeasureArcCircleOption_e: CenterToCenter 0, MinimumDistance 1,
        /// MaximumDistance 2). «centre» is the default because it is what the
        /// measure did before the option was set (finding M29).
        /// </summary>
        private static readonly Dictionary<string, int> ArcMeasureOptions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["centre"] = (int)swMeasureArcCircleOption_e.swMeasureArcCircle_CenterToCenter,
            ["minimum"] = (int)swMeasureArcCircleOption_e.swMeasureArcCircle_MinimumDistance,
            ["maximum"] = (int)swMeasureArcCircleOption_e.swMeasureArcCircle_MaximumDistance,
        };

        /// <summary>
        /// Mette un valore nelle misure **solo se e' una misura**.
        ///
        /// `IMeasure` risponde `-1` quando la cosa chiesta non si applica — il
        /// perimetro di una faccia che non ne ha, il raggio di un piano — e un
        /// `-1` riportato come numero e' il difetto che non da' errore:
        /// nessuna di queste grandezze puo' essere negativa, quindi un valore
        /// negativo e' un'assenza travestita.
        /// </summary>
        private static void Put(Dictionary<string, object> location, string itemName,
                                  double val, double factor)
        {
            if (double.IsNaN(val) || val < 0) return;
            location[itemName] = Round(val * factor);
        }

        /// <summary>
        /// Una faccia o uno spigolo, presi dallo stato per identificativo.
        /// Il rifiuto dice di che tipo era quell'uid, se c'era: «E3 e' uno
        /// spigolo» e' un aiuto, «non lo trovo» no.
        /// </summary>
        private static object EntityFromState(DocumentState docState, string uid, out string reason)
        {
            reason = null;
            if (docState == null)
            {
                reason = "Non ho lo stato del documento: senza, gli identificativi non " +
                         "vogliono dire niente.";
                return null;
            }
            DocumentState.EntitySignature signature;
            if (!docState.Signatures.TryGetValue(uid, out signature))
            {
                reason = "«" + uid + "» non c'e' in questo documento. Gli identificativi " +
                         "delle facce e degli spigoli stanno nello stato, e si rinumerano " +
                         "a ogni lettura: rileggi il pezzo prima di nominarli.";
                return null;
            }
            if (signature.Type != "FACE" && signature.Type != "EDGE")
            {
                reason = "«" + uid + "» e' di tipo «" + signature.Type + "»: so misurare " +
                         "facce e spigoli.";
                return null;
            }
            if (signature.Entity == null)
            {
                reason = "«" + uid + "» non e' piu' utilizzabile: rileggi il pezzo.";
                return null;
            }
            return signature.Entity;
        }

        /// <summary>
        /// I corpi da misurare: tutti, oppure quello chiesto per
        /// identificativo. Null (con un motivo) quando l'identificativo non
        /// esiste: un elenco vuoto darebbe una risposta con dei numeri, e dei
        /// numeri su niente sono peggio di un rifiuto.
        ///
        /// A BODY ASKED FOR IS RESOLVED THROUGH THE DOCUMENT STATE AND ITS
        /// SIGNATURE (Z-C21.5, scheme S5), as combine_bodies and the overhang check (OperazioniSporgenze) do:
        /// <see cref="BodyAskedFor"/>. Until then «C2» was the second body of a
        /// GetBodies2 made now (IPartDoc~GetBodies2.html, Remarks: «May vary the
        /// order in which bodies are returned»), and a body named before a cut
        /// that renumbers the bodies was measured in the place of another one,
        /// with plausible numbers. body_bounding_box and mass_properties both
        /// come through here.
        /// </summary>
        private List<object> ChosenBodies(
            IPartDoc partDoc, DocumentState docState, Dictionary<string, object> a, string uid, out string reason)
        {
            reason = null;
            var outside = new List<object>();
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (rawItems == null || rawItems.Length == 0)
            {
                reason = "Il pezzo non ha nessun corpo solido. Fai prima un'estrusione.";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var asked = BodyAskedFor(docState, a, uid.Trim(), out reason);
                if (asked == null) return null;
                outside.Add(asked);
                return outside;
            }
            for (int i = 0; i < rawItems.Length; i++)
            {
                if (rawItems[i] == null) continue;
                // SI TIENE L'OGGETTO COM'E' USCITO da `GetBodies2`, non
                // ricastato: e' quello che `AddBodies` aggancia. Vedi il
                // commento accanto ad `AddBodies`.
                outside.Add(rawItems[i]);
            }
            if (outside.Count == 0)
            {
                reason = "The part answered GetBodies2 with no readable solid body: there is nothing to measure.";
                return null;
            }
            return outside;
        }

        /// <summary>
        /// THE BODY A «body_uid» NAMES, through the document state and its
        /// signature (Z-C21.5, scheme S5): <c>BodiesFromUid</c>, which calls
        /// SignatureMatches, the helper combine_bodies and OperazioniSporgenze already use.
        /// Null with <paramref name="reason"/> when the uid is not a solid body
        /// of the state (the words of the old refusal, which the bench cases
        /// read), or when it no longer names the body the plan looked at (a cut
        /// renumbered the bodies, E4).
        ///
        /// The Z-C21.3 probe of L12-3 found the solids in the same order in its
        /// four parts: that is a tolerance for the bench, not a licence to
        /// resolve a uid by its position (the lesson of the wrong number that
        /// looks plausible). body_bounding_box, mass_properties and
        /// check_body_geometry come through here.
        /// </summary>
        private IBody2 BodyAskedFor(DocumentState docState, Dictionary<string, object> a, string uid, out string reason)
        {
            reason = null;
            DocumentState.EntitySignature signature;
            if (docState == null || !docState.Signatures.TryGetValue(uid, out signature) || signature.Type != "BODY")
            {
                reason = "Non trovo il corpo «" + uid + "». Gli identificativi dei corpi " +
                         "stanno nello stato del documento, dentro «bodies».";
                return null;
            }
            var bodies = BodiesFromUid(docState, new List<string> { uid }, a, out string why);
            if (bodies == null || bodies.Count != 1)
            {
                reason = "«body_uid» «" + uid + "» no longer names the body the plan looked at, and I do not " +
                         "measure another one in its place: " + why;
                return null;
            }
            return bodies[0];
        }

        /// <summary>
        /// La scatola di un corpo in mm: {minX, minY, minZ, maxX, maxY, maxZ}.
        /// Null se `GetExtremePoint` non risponde — e allora si fallisce,
        /// invece di ripiegare su `GetBodyBox` e dare per buono un numero
        /// approssimato senza dirlo.
        /// </summary>
        private static double[] BodyBox(IBody2 body)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(body);
                if (box == null) return null;
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = Units.Mm(box[i]);
                return outside;
            }
            catch { return null; }
        }

        private static string PartMaterial(IModelDoc2 model, string configuration = "")
        {
            try
            {
                string database;
                var partDoc = model as IPartDoc;
                if (partDoc == null) return null;
                // The empty string means the active configuration (measured by
                // Z-C21.3, L17-1), as `DocumentState` asks for it. Since M10
                // `apply_material` sets and reads back every configuration by
                // name instead. The material is PER CONFIGURATION: in an assembly
                // the configuration the component uses is asked.
                string itemName = partDoc.GetMaterialPropertyName2(configuration ?? "", out database);
                return string.IsNullOrWhiteSpace(itemName) ? null : itemName;
            }
            catch { return null; }
        }

        private static double[] Scale(double[] v, double factor)
        {
            if (v == null || v.Length < 3) return null;
            return new[] { Round(v[0] * factor), Round(v[1] * factor), Round(v[2] * factor) };
        }

        private static double Round(double x)
        {
            return Math.Round(x, 4);
        }
    }
}
