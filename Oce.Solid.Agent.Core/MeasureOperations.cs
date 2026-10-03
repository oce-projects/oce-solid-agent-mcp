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
    /// 300x260x40». Tutte e due si calcolavano gia' dentro `StatoDocumento` —
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
    public partial class OperazioniSw
    {
        private Esito ProprietaDiMassa(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            // L'ASSIEME HA LA SUA STRADA (voce Z-D1): CreateMassProperty sull'assieme
            // intero, controllata contro la somma dei pezzi (OperazioniMisuraAssieme).
            if (modello is IAssemblyDoc) return ProprietaDiMassaAssieme(modello, a);
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo ne' un assieme: non ha corpi da pesare. " +
                               "Aprimi il pezzo o l'assieme.");

            var corpi = CorpiScelti(parte, stato, a, Str(a, "body_uid", null), out string motivo);
            if (corpi == null) return Fallito(motivo);

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
            var prendi = new Func<Func<IMassProperty2, object>, string, object>((leggi, cosa) =>
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
                    var mp = ApiHelpers.NewPartMassProperty(modello, corpi, out bool attached);
                    if (mp == null || !attached) return null;
                    return leggi(mp);
                }
                catch { return null; }
            });

            object m = prendi(p => p.Mass, "massa");
            object d = prendi(p => p.Density, "densita");
            object v = prendi(p => p.Volume, "volume");
            object s = prendi(p => p.SurfaceArea, "area");
            object c = prendi(p => p.CenterOfMass, "baricentro");

            if (m == null || d == null || v == null || s == null)
                return Fallito(
                    "SOLIDWORKS non ha dato le proprieta' di massa: o non ha agganciato i " +
                    "corpi, o la misura non e' riuscita. Non ti do numeri a meta'.");

            double massaKg = Convert.ToDouble(m);
            double densita = Convert.ToDouble(d);
            double volumeM3 = Convert.ToDouble(v);
            double areaM2 = Convert.ToDouble(s);
            var centro = Scala(c as double[], 1000.0);

            string materiale = MaterialeDelPezzo(modello);

            var misure = new Dictionary<string, object>
            {
                ["bodies"] = corpi.Count,
                ["volume_mm3"] = Math.Round(volumeM3 * 1e9, 2),
                ["area_mm2"] = Math.Round(areaM2 * 1e6, 2),
                ["center_of_mass_mm"] = centro,
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
            if (string.IsNullOrEmpty(materiale))
            {
                misure["material"] = null;
                misure["mass_kg"] = null;
                misure["density_kg_m3"] = null;
                misure["why_no_mass"] =
                    "Al pezzo non e' stato dato nessun materiale, quindi la massa non si sa. " +
                    "SOLIDWORKS risponderebbe lo stesso usando la densita' predefinita del " +
                    "modello (di solito 1000 kg/m3, cioe' acqua): un numero credibile e " +
                    "sbagliato di otto volte su un pezzo d'acciaio. Usa «apply_material» " +
                    "e richiedimelo.";
            }
            else
            {
                misure["material"] = materiale;
                misure["mass_kg"] = Math.Round(massaKg, 4);
                misure["density_kg_m3"] = Math.Round(densita, 2);
                // IL CONTO RIFATTO A MANO, accanto a quello di SOLIDWORKS. Se
                // i due divergono, una delle due misure e' in unita' diverse
                // da quelle che dice — ed e' il modo in cui un fattore 1000 si
                // fa vedere invece di nascondersi.
                double aMano = volumeM3 * densita;
                misure["mass_recomputed_kg"] = Math.Round(aMano, 4);
                misure["deviation_on_recompute"] = massaKg > 0
                    ? (object)Math.Round(Math.Abs(aMano - massaKg) / massaKg, 6)
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
                 (string.IsNullOrEmpty(materiale) ? ", senza materiale" : ", " + materiale +
                  ", " + massaKg.ToString("0.###") + " kg"));
            return RiuscitoSenzaCreare(misure);
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
        /// E' la stessa scelta gia' fatta in `StatoDocumento`, e sta scritta
        /// in tutti e due i posti perche' e' il genere di cosa che qualcuno
        /// «semplifica» leggendo solo uno dei due.
        /// </summary>
        private Esito IngombroCorpo(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (modello is IAssemblyDoc) return IngombroAssieme(modello, a);   // voce Z-D1
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo ne' un assieme: non ha corpi da misurare. " +
                               "Aprimi il pezzo o l'assieme.");

            string askedUid = Str(a, "body_uid", null);
            var corpi = CorpiScelti(parte, stato, a, askedUid, out string motivo);
            if (corpi == null) return Fallito(motivo);
            if (corpi.Count == 0)
                return Fallito("Il pezzo non ha nessun corpo solido da misurare.");

            var elenco = new List<object>();
            double[] tutto = null;
            for (int i = 0; i < corpi.Count; i++)
            {
                var scatola = ScatolaDelCorpo(corpi[i] as IBody2);
                if (scatola == null)
                    return Fallito(
                        "Non sono riuscito a leggere l'ingombro del corpo " + (i + 1) +
                        ": GetExtremePoint non ha risposto.");
                elenco.Add(new Dictionary<string, object>
                {
                    // The body asked for keeps its own uid: numbering the list
                    // of one body said «C1» of the body «C2» (Z-C21.5, S5).
                    ["body"] = string.IsNullOrWhiteSpace(askedUid) ? "C" + (i + 1) : askedUid.Trim().ToUpperInvariant(),
                    ["from_mm"] = new[] { Tondo(scatola[0]), Tondo(scatola[1]), Tondo(scatola[2]) },
                    ["to_mm"] = new[] { Tondo(scatola[3]), Tondo(scatola[4]), Tondo(scatola[5]) },
                    ["bounding_box_mm"] = new[]
                    {
                        Tondo(scatola[3] - scatola[0]),
                        Tondo(scatola[4] - scatola[1]),
                        Tondo(scatola[5] - scatola[2]),
                    },
                });
                if (tutto == null) tutto = (double[])scatola.Clone();
                else
                {
                    for (int k = 0; k < 3; k++) tutto[k] = Math.Min(tutto[k], scatola[k]);
                    for (int k = 3; k < 6; k++) tutto[k] = Math.Max(tutto[k], scatola[k]);
                }
            }

            var misure = new Dictionary<string, object>
            {
                ["bodies"] = elenco.ToArray(),
                ["from_mm"] = new[] { Tondo(tutto[0]), Tondo(tutto[1]), Tondo(tutto[2]) },
                ["to_mm"] = new[] { Tondo(tutto[3]), Tondo(tutto[4]), Tondo(tutto[5]) },
                ["bounding_box_mm"] = new[]
                {
                    Tondo(tutto[3] - tutto[0]),
                    Tondo(tutto[4] - tutto[1]),
                    Tondo(tutto[5] - tutto[2]),
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
                 string.Join(" x ", ((double[])misure["bounding_box_mm"]).Select(x => x.ToString("0.##"))) +
                 " mm su " + corpi.Count + " corpi");
            return RiuscitoSenzaCreare(misure);
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

        private Esito DistanzaMinima(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string uidA = Str(a, "start_uid", null);
            string uidB = Str(a, "end_uid", null);
            if (string.IsNullOrWhiteSpace(uidA) || string.IsNullOrWhiteSpace(uidB))
                return Fallito(
                    "Mi servono due cose fra cui misurare: «start_uid» e «end_uid». Gli " +
                    "identificativi di facce e spigoli stanno nello stato del documento.");
            if (string.Equals(uidA, uidB, StringComparison.OrdinalIgnoreCase))
                return Fallito(
                    "«start_uid» e «end_uid» sono la stessa cosa (" + uidA + "): la distanza " +
                    "di una faccia da se stessa e' zero, e non e' una misura.");

            string motivo;
            object entA = EntitaDalloStato(stato, uidA, out motivo);
            if (entA == null) return Fallito(motivo);
            object entB = EntitaDalloStato(stato, uidB, out motivo);
            if (entB == null) return Fallito(motivo);

            double metri;
            object p1, p2;
            try { metri = modello.ClosestDistance(entA, entB, out p1, out p2); }
            catch (Exception ex) { return Fallito("La misura non e' riuscita: " + ex.Message); }

            // «NO SOLUTION» IS SAID AS SUCH (Z-C21.8, finding L12-9).
            // IModelDoc2~ClosestDistance.html: «Distance in meters between Point1
            // and Point2; -1.0 if no solution». The check on the two points below
            // refuses it too (a distance between two points is never negative,
            // so the gap is at least a metre), but with a sentence about a
            // distance of «-1000 mm» contradicted by its own points, or about
            // points that are missing, which sends the reader to the wrong place.
            if (metri < 0)
                return Fallito(
                    "SOLIDWORKS found no closest distance between «" + uidA + "» and «" + uidB +
                    "»: ClosestDistance answered " + metri.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    ", which its guide page gives as «no solution». Check that both are faces, edges or vertices " +
                    "of the model, and ask again with the identifiers read from the state now.");

            var puntoA = p1 as double[];
            var puntoB = p2 as double[];
            if (puntoA == null || puntoB == null || puntoA.Length < 3 || puntoB.Length < 3)
                return Fallito(
                    "SOLIDWORKS ha dato la distanza (" + (metri * 1000).ToString("0.###") +
                    " mm) ma non i due punti fra cui l'ha misurata. Senza quelli il numero " +
                    "non si puo' controllare, e non te lo do.");

            // IL CONTROLLO INDIPENDENTE, e non e' uno scrupolo: se il valore
            // dice 0 e i punti distano 4 mm, la chiamata e' fallita e ha
            // risposto lo stesso.
            double fraIPunti = Math.Sqrt(
                Math.Pow(puntoA[0] - puntoB[0], 2) +
                Math.Pow(puntoA[1] - puntoB[1], 2) +
                Math.Pow(puntoA[2] - puntoB[2], 2));
            double scartoMm = Math.Abs(fraIPunti - metri) * 1000.0;
            if (scartoMm > 0.001)
                return Fallito(
                    "SOLIDWORKS dice che la distanza e' " + (metri * 1000).ToString("0.####") +
                    " mm, ma i due punti che ha dato ne distano " +
                    (fraIPunti * 1000).ToString("0.####") + ". Il numero e' smentito dai suoi " +
                    "stessi punti: non te lo do.");

            var misure = new Dictionary<string, object>
            {
                ["source"] = uidA,
                ["target"] = uidB,
                ["distance_mm"] = Tondo(metri * 1000.0),
                ["point_source_mm"] = Scala(puntoA, 1000.0),
                ["point_target_mm"] = Scala(puntoB, 1000.0),
                // SI RIPORTA ANCHE IL CONTROLLO, non solo il suo esito: chi
                // legge vede che il numero e' stato verificato e con che
                // scarto, invece di doverci credere.
                ["distance_between_points_mm"] = Tondo(fraIPunti * 1000.0),
                ["deviation_of_check_mm"] = Tondo(scartoMm),
                ["units"] = "millimetri; SOLIDWORKS li da' in metri e la conversione e' qui",
            };
            if (metri * 1000.0 < 0.001)
                misure["touch"] =
                    "Le due cose si toccano o si intersecano: la distanza e' zero, e i due " +
                    "punti sono lo stesso punto.";

            _log("[agente] distanza minima " + uidA + " -> " + uidB + ": " +
                 (metri * 1000).ToString("0.###") + " mm");
            return RiuscitoSenzaCreare(misure);
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
        //   Calculate(new object[] { facciaA, facciaB })   ->  FALSE
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

        private Esito MisuraFraEntita(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var uid = new List<string>();
            foreach (var chiave in new[] { "uid", "uid2" })
            {
                string u = Str(a, chiave, null);
                if (!string.IsNullOrWhiteSpace(u)) uid.Add(u);
            }
            if (uid.Count == 0)
                return Fallito(
                    "Mi serve almeno una cosa da misurare: «uid» (e «uid2» per misurare " +
                    "FRA due). Gli identificativi di facce e spigoli stanno nello stato " +
                    "del documento.");
            if (uid.Count > 2)
                return Fallito(
                    "Ne hai date " + uid.Count + ": ne misuro una sola o due. Con piu' di " +
                    "due, «distanza» e «fillet» non vogliono dire piu' niente.");

            string arcMeasure = (Str(a, "arc_measure", "centre") ?? "centre").Trim().ToLowerInvariant();
            int arcOption;
            if (!ArcMeasureOptions.TryGetValue(arcMeasure, out arcOption))
                return Fallito(
                    "«arc_measure» is «centre», «minimum» or «maximum», not «" + arcMeasure + "»: it says how " +
                    "a distance to an arc or a circle is taken (centre to centre, between the nearest points, " +
                    "between the farthest points).");

            var entita = new List<object>();
            foreach (var u in uid)
            {
                string motivo;
                var e = EntitaDalloStato(stato, u, out motivo);
                if (e == null) return Fallito(motivo);
                entita.Add(e);
            }

            IMeasure misura;
            try { misura = modello.Extension.CreateMeasure() as IMeasure; }
            catch (Exception ex) { return Fallito("Non riesco a preparare la misura: " + ex.Message); }
            if (misura == null)
                return Fallito("Questa installazione non espone lo strumento di misura.");

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
                misura.ArcOption = arcOption;
                misura.ProjectionOption = (int)swMeasureProjectOnOption_e.swMeasureProjectOn_None;
            }
            catch (Exception ex) { return Fallito("The measure did not take the arc option: " + ex.Message); }

            bool riuscita;
            try
            {
                // LA SELEZIONE E' L'ARGOMENTO VERO. Vedi il commento qui sopra.
                modello.ClearSelection2(true);
                for (int i = 0; i < entita.Count; i++)
                {
                    var ent = entita[i] as IEntity;
                    if (ent == null || !ent.Select4(i > 0, null))
                        return Fallito(
                            "Non sono riuscito a selezionare «" + uid[i] + "»: senza la " +
                            "selezione la misura guarderebbe qualcos'altro.");
                }
                riuscita = misura.Calculate(null);
            }
            catch (Exception ex) { return Fallito("La misura non e' riuscita: " + ex.Message); }
            finally { try { modello.ClearSelection2(true); } catch { } }

            if (!riuscita)
                return Fallito(
                    "Lo strumento di misura ha risposto «no» su " +
                    string.Join(" e ", uid.Select(u => "«" + u + "»")) +
                    ". Non ti do i numeri che aveva dentro: sarebbero quelli di prima.");

            var fuori = new Dictionary<string, object>
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
            Metti(fuori, "area_mm2", misura.Area, 1e6);
            Metti(fuori, "area_total_mm2", misura.TotalArea, 1e6);
            Metti(fuori, "perimetro_mm", misura.Perimeter, 1000.0);
            Metti(fuori, "length_mm", misura.TotalLength, 1000.0);
            Metti(fuori, "radius_mm", misura.Radius, 1000.0);
            Metti(fuori, "diameter_mm", misura.Diameter, 1000.0);
            if (uid.Count == 2)
            {
                // Read back after Calculate: the probe read the value it had set.
                // If SOLIDWORKS answered with another option, «distance_mm» would
                // be another kind of distance under the name the caller asked for.
                int arcOptionRead = misura.ArcOption;
                if (arcOptionRead != arcOption)
                    return Fallito(
                        "I asked the measure for «arc_measure» " + arcMeasure + " (ArcOption " + arcOption +
                        ") and it reads back ArcOption " + arcOptionRead + ": the distance would be another " +
                        "kind of distance, and I do not give it.");
                fuori["arc_measure"] = arcMeasure;
                fuori["projection"] = "none";
                Metti(fuori, "distance_mm", misura.Distance, 1000.0);
                Metti(fuori, "distanza_normale_mm", misura.NormalDistance, 1000.0);
                Metti(fuori, "distance_between_centers_mm", misura.CenterDistance, 1000.0);
                // I DELTA POSSONO ESSERE NEGATIVI PER DAVVERO: un delta e' una
                // differenza, e -20 vuol dire «venti dall'altra parte». Qui
                // `-1` non e' un segnale, quindi non si filtra.
                fuori["delta_mm"] = new[]
                {
                    Tondo(misura.DeltaX * 1000.0),
                    Tondo(misura.DeltaY * 1000.0),
                    Tondo(misura.DeltaZ * 1000.0),
                };
                fuori["parallel"] = misura.IsParallel;
                fuori["perpendicular"] = misura.IsPerpendicular;
                fuori["intersect"] = misura.IsIntersect;
                double gradi = misura.Angle * 180.0 / Math.PI;
                if (gradi >= 0) fuori["angle_deg"] = Tondo(gradi);
            }

            _log("[agente] misura fra " + string.Join(" e ", uid) +
                 (fuori.ContainsKey("distance_mm") ? ": " + fuori["distance_mm"] + " mm" : ""));
            return RiuscitoSenzaCreare(fuori);
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
        private static void Metti(Dictionary<string, object> dove, string nome,
                                  double valore, double fattore)
        {
            if (double.IsNaN(valore) || valore < 0) return;
            dove[nome] = Tondo(valore * fattore);
        }

        /// <summary>
        /// Una faccia o uno spigolo, presi dallo stato per identificativo.
        /// Il rifiuto dice di che tipo era quell'uid, se c'era: «E3 e' uno
        /// spigolo» e' un aiuto, «non lo trovo» no.
        /// </summary>
        private static object EntitaDalloStato(StatoDocumento stato, string uid, out string motivo)
        {
            motivo = null;
            if (stato == null)
            {
                motivo = "Non ho lo stato del documento: senza, gli identificativi non " +
                         "vogliono dire niente.";
                return null;
            }
            StatoDocumento.FirmaEntita firma;
            if (!stato.Firme.TryGetValue(uid, out firma))
            {
                motivo = "«" + uid + "» non c'e' in questo documento. Gli identificativi " +
                         "delle facce e degli spigoli stanno nello stato, e si rinumerano " +
                         "a ogni lettura: rileggi il pezzo prima di nominarli.";
                return null;
            }
            if (firma.Tipo != "FACE" && firma.Tipo != "EDGE")
            {
                motivo = "«" + uid + "» e' di tipo «" + firma.Tipo + "»: so misurare " +
                         "facce e spigoli.";
                return null;
            }
            if (firma.Entita == null)
            {
                motivo = "«" + uid + "» non e' piu' utilizzabile: rileggi il pezzo.";
                return null;
            }
            return firma.Entita;
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
        private List<object> CorpiScelti(
            IPartDoc parte, StatoDocumento stato, Dictionary<string, object> a, string uid, out string motivo)
        {
            motivo = null;
            var fuori = new List<object>();
            var grezzi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (grezzi == null || grezzi.Length == 0)
            {
                motivo = "Il pezzo non ha nessun corpo solido. Fai prima un'estrusione.";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var asked = BodyAskedFor(stato, a, uid.Trim(), out motivo);
                if (asked == null) return null;
                fuori.Add(asked);
                return fuori;
            }
            for (int i = 0; i < grezzi.Length; i++)
            {
                if (grezzi[i] == null) continue;
                // SI TIENE L'OGGETTO COM'E' USCITO da `GetBodies2`, non
                // ricastato: e' quello che `AddBodies` aggancia. Vedi il
                // commento accanto ad `AddBodies`.
                fuori.Add(grezzi[i]);
            }
            if (fuori.Count == 0)
            {
                motivo = "The part answered GetBodies2 with no readable solid body: there is nothing to measure.";
                return null;
            }
            return fuori;
        }

        /// <summary>
        /// THE BODY A «body_uid» NAMES, through the document state and its
        /// signature (Z-C21.5, scheme S5): <c>CorpiDaUid</c>, which calls
        /// FirmaCombacia, the helper combine_bodies and OperazioniSporgenze already use.
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
        private IBody2 BodyAskedFor(StatoDocumento stato, Dictionary<string, object> a, string uid, out string motivo)
        {
            motivo = null;
            StatoDocumento.FirmaEntita firma;
            if (stato == null || !stato.Firme.TryGetValue(uid, out firma) || firma.Tipo != "BODY")
            {
                motivo = "Non trovo il corpo «" + uid + "». Gli identificativi dei corpi " +
                         "stanno nello stato del documento, dentro «bodies».";
                return null;
            }
            var corpi = CorpiDaUid(stato, new List<string> { uid }, a, out string perche);
            if (corpi == null || corpi.Count != 1)
            {
                motivo = "«body_uid» «" + uid + "» no longer names the body the plan looked at, and I do not " +
                         "measure another one in its place: " + perche;
                return null;
            }
            return corpi[0];
        }

        /// <summary>
        /// La scatola di un corpo in mm: {minX, minY, minZ, maxX, maxY, maxZ}.
        /// Null se `GetExtremePoint` non risponde — e allora si fallisce,
        /// invece di ripiegare su `GetBodyBox` e dare per buono un numero
        /// approssimato senza dirlo.
        /// </summary>
        private static double[] ScatolaDelCorpo(IBody2 corpo)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(corpo);
                if (box == null) return null;
                var fuori = new double[6];
                for (int i = 0; i < 6; i++) fuori[i] = Unita.Mm(box[i]);
                return fuori;
            }
            catch { return null; }
        }

        private static string MaterialeDelPezzo(IModelDoc2 modello, string configurazione = "")
        {
            try
            {
                string database;
                var parte = modello as IPartDoc;
                if (parte == null) return null;
                // The empty string means the active configuration (measured by
                // Z-C21.3, L17-1), as `StatoDocumento` asks for it. Since M10
                // `apply_material` sets and reads back every configuration by
                // name instead. The material is PER CONFIGURATION: in an assembly
                // the configuration the component uses is asked.
                string nome = parte.GetMaterialPropertyName2(configurazione ?? "", out database);
                return string.IsNullOrWhiteSpace(nome) ? null : nome;
            }
            catch { return null; }
        }

        private static double[] Scala(double[] v, double fattore)
        {
            if (v == null || v.Length < 3) return null;
            return new[] { Tondo(v[0] * fattore), Tondo(v[1] * fattore), Tondo(v[2] * fattore) };
        }

        private static double Tondo(double x)
        {
            return Math.Round(x, 4);
        }
    }
}
