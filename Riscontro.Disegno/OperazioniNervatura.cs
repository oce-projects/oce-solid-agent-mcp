using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
    /// ── LA SONDA DEL 28/9/2026 (`Controlli\Nervatura\Programma.cs`) ──
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
    /// `nervatura_nella_scatola_svuotata`, dove il primo verso fallisce e il
    /// secondo deve fare +720.
    ///
    /// La piastra senza pareti non fa nascere niente, e va bene cosi': dalla
    /// linea non c'e' niente da incontrare. Si rifiuta dicendolo, e nominando
    /// `estrudi_su_faccia` per un rilievo su una faccia piana.
    ///
    /// ── COSA RESTA NEL PEZZO SE MI RIFIUTO ───────────────────────────────
    ///
    /// Niente. Il piano sfalsato (se l'ho fatto io), lo schizzo e l'eventuale
    /// nervatura nata storta si tolgono con `DeleteSelection2` (non con
    /// EditDelete, che apre una finestra di conferma e la chiamata COM non
    /// torna: vedi `EliminaLavorazione`), dal piu' giovane al piu' vecchio, e
    /// si rilegge il volume (`ETornatoComEra` in OperazioniGuardie).
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>
        /// Quanto puo' stare fuori dal piano un estremo della linea, in mm. Lo
        /// stesso margine di `piega_da_schizzo`: cinque centesimi coprono gli
        /// arrotondamenti dello stato e restano lontani da qualunque errore vero.
        /// </summary>
        private const double NervaturaFuoriPianoMm = 0.05;

        /// <summary>
        /// Sotto questo volume «cresciuto» non e' una misura, e' rumore della
        /// lettura in metri cubi (`VolumeMm3`).
        /// </summary>
        private const double NervaturaCrescitaMinimaMm3 = 0.01;

        private Esito Nervatura(IModelDoc2 modello, Dictionary<string, object> a)
        {
            if (!(modello is IPartDoc))
                return Fallito("La nervatura si fa su un pezzo. Aprimi il pezzo.");

            // ── GLI ARGOMENTI, PRIMA DI TOCCARE IL PEZZO ─────────────────
            string piano = (Str(a, "piano", "") ?? "").Trim().ToLowerInvariant();
            if (piano != "frontale" && piano != "superiore" && piano != "destro")
                return Fallito(
                    "Dimmi «piano»: frontale, superiore o destro. E' il piano in cui sta la linea " +
                    "e in cui la nervatura cresce; con «distanza_mm» lo sposto parallelo a se stesso " +
                    "(per esempio a meta' profondita' di una staffa).");
            // FACOLTATIVA, MA SE C'E' DEV'ESSERE UN NUMERO: `Num` inghiottirebbe
            // «25mm» e restituirebbe zero, cioe' la nervatura sul piano base.
            double distanza = 0;
            if (a != null && a.TryGetValue("distanza_mm", out var grezza) && grezza != null &&
                !NumObbligatorio(a, "distanza_mm", out distanza, out string erroreDistanza))
                return Fallito(erroreDistanza + " Scrivila in mm, per esempio 25; senza, la nervatura sta " +
                               "sul piano base.");

            if (!NumObbligatorio(a, "spessore_mm", out double spessore, out string errore))
                return Fallito(errore + " E' lo spessore della nervatura: scrivilo in mm (si divide a " +
                               "meta' dalle due parti del piano).");
            if (spessore <= 0)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "«spessore_mm» dev'essere piu' di zero: {0:0.###} non e' una nervatura. Per una " +
                    "staffa di lamiera da 10 si usa di solito 4-6 mm.", spessore));

            var da = Terna(a, "da_mm");
            var fino = Terna(a, "a_mm");
            if (da == null || fino == null)
                return Fallito(
                    "«da_mm» e «a_mm» sono i due estremi della linea della nervatura: punti [x, y, z] " +
                    "in mm nel sistema del modello, tutti e due sul piano indicato. Per esempio, " +
                    "nell'angolo di una L: da [10, 40, 25] a [40, 10, 25].");

            // I DUE PUNTI STANNO SUL PIANO? Il piano base «frontale» e' XY
            // (normale +Z), «superiore» XZ (+Y), «destro» YZ (+X): la stessa
            // tabella di `Riferimenti.NormaleDelPiano`, e il segno della
            // distanza e' quello di `crea_piano` (frontale -250 -> z = -250,
            // misurato dal caso piano_frontale_meno_250).
            var normale = Riferimenti.NormaleDelPiano(piano);
            Func<double[], double> quota = p => p[0] * normale[0] + p[1] * normale[1] + p[2] * normale[2];
            double scarto = Math.Max(Math.Abs(quota(da) - distanza), Math.Abs(quota(fino) - distanza));
            if (scarto > NervaturaFuoriPianoMm)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "La linea non sta sul piano {0} a distanza {1:0.###} mm: un estremo ne dista " +
                    "{2:0.###} mm. I due punti vanno presi sul piano della nervatura (sul {0} " +
                    "sfalsato di {1:0.###}, la coordinata {3} di tutti e due e' {1:0.###}); oppure " +
                    "cambia «distanza_mm».",
                    piano, distanza, scarto, piano == "frontale" ? "z" : piano == "superiore" ? "y" : "x"));
            double lunga = Math.Sqrt(Math.Pow(fino[0] - da[0], 2) + Math.Pow(fino[1] - da[1], 2) + Math.Pow(fino[2] - da[2], 2));
            if (lunga < 1)
                return Fallito("La linea della nervatura e' lunga meno di un millimetro: i due punti " +
                               "coincidono. Indica due estremi distinti.");

            // ── IL PEZZO DI PARTENZA ─────────────────────────────────────
            var foto = FotografaIlPezzo(modello);
            if (foto.Volume == null) return Fallito(VolumeNonLettoPrima("fare la nervatura"));
            if (foto.Corpi == 0)
                return Fallito(
                    "Nel pezzo non c'e' un corpo solido: la nervatura riempie lo spazio fra una linea e " +
                    "pareti che ci sono gia'. Fai prima il pezzo (per esempio con «estrudi_profilo»).");
            var ribPrima = NervaturaNomiDelTipo(modello, "Rib");
            var schizziPrima = NervaturaNomiDelTipo(modello, "ProfileFeature");

            // Cio' che creo io, dal piu' vecchio al piu' giovane: se mi
            // rifiuto si toglie al contrario.
            var mie = new List<IFeature>();

            // ── IL PIANO DELLO SCHIZZO ───────────────────────────────────
            IFeature pianoDelloSchizzo;
            bool pianoFattoQui = Math.Abs(distanza) > 1e-9;
            if (!pianoFattoQui)
            {
                pianoDelloSchizzo = TrovaPianoBase(modello, piano);
                if (pianoDelloSchizzo == null)
                    return Fallito("Non ho trovato il piano «" + piano + "» nell'albero.");
            }
            else
            {
                pianoDelloSchizzo = NervaturaPianoSfalsato(modello, piano, distanza, out string motivoPiano);
                if (pianoDelloSchizzo == null) return Fallito(motivoPiano);
                mie.Add(pianoDelloSchizzo);
            }

            // ── LO SCHIZZO: UNA LINEA, PASSATA PER ModelToSketchTransform ──
            //
            // Sul piano destro la x dello schizzo va in -Z del modello (RAG 81r):
            // non ci si ragiona, si trasforma. E' la ricetta della sonda e di
            // `piega_da_schizzo`.
            modello.ClearSelection2(true);
            if (!pianoDelloSchizzo.Select2(false, 0))
                return Fallito("Non sono riuscito a selezionare il piano dello schizzo." +
                               NervaturaRitira(modello, mie, foto));
            modello.SketchManager.InsertSketch(true);
            var sk = modello.SketchManager.ActiveSketch as ISketch;
            var matematica = _sw.GetMathUtility() as IMathUtility;
            if (sk == null || matematica == null)
            {
                try { if (modello.SketchManager.ActiveSketch != null) modello.SketchManager.InsertSketch(true); } catch { }
                var aperto = NervaturaNuovaDelTipo(modello, "ProfileFeature", schizziPrima);
                if (aperto != null) mie.Add(aperto);
                return Fallito("Lo schizzo della nervatura non si e' aperto." + NervaturaRitira(modello, mie, foto));
            }
            var tr = sk.ModelToSketchTransform;
            Func<double[], double[]> nelloSchizzo = p =>
                ((IMathPoint)((IMathPoint)matematica.CreatePoint(new[] { Unita.M(p[0]), Unita.M(p[1]), Unita.M(p[2]) }))
                    .MultiplyTransform(tr)).ArrayData as double[];
            var s0 = nelloSchizzo(da);
            var s1 = nelloSchizzo(fino);
            object linea = null;
            bool addPrima = modello.SketchManager.AddToDB;
            try
            {
                modello.SketchManager.AddToDB = true;
                linea = modello.SketchManager.CreateLine(s0[0], s0[1], 0, s1[0], s1[1], 0);
            }
            finally { modello.SketchManager.AddToDB = addPrima; }
            modello.SketchManager.InsertSketch(true);
            var schizzo = NervaturaNuovaDelTipo(modello, "ProfileFeature", schizziPrima);
            if (schizzo != null) mie.Add(schizzo);
            if (linea == null || schizzo == null)
                return Fallito("La linea della nervatura non e' nata nello schizzo." + NervaturaRitira(modello, mie, foto));

            // L'ALBERO DOPO LO SCHIZZO: e' come deve essere prima di OGNI
            // tentativo. Un tentativo fallito che lascia una feature di un altro
            // tipo (non una «Rib») non la vede nessuno, e il secondo tentativo
            // le costruirebbe sopra. La sonda non l'ha mai visto succedere; se
            // succede non si prova l'altro verso, si ritira e si dice.
            var nomiDopoLoSchizzo = NervaturaNomiDiTutte(modello);

            // ── I DUE VERSI DEL MATERIALE ────────────────────────────────
            IFeature rib = null;
            bool girato = false;
            int tentativi = 0;
            var esiti = new List<string>();
            foreach (bool gira in new[] { false, true })
            {
                tentativi++;
                string alzato = NervaturaInserisci(modello, schizzo, spessore, gira);
                try { modello.EditRebuild3(); } catch { }
                var nata = NervaturaNuovaDelTipo(modello, "Rib", ribPrima);
                string verso = gira ? "materiale girato" : "materiale dritto";
                if (nata == null)
                {
                    esiti.Add(verso + ": nessuna nervatura" + (alzato == null ? "" : " (" + alzato + ")"));
                    var intruse = NervaturaNuoveDiTutte(modello, nomiDopoLoSchizzo);
                    if (intruse.Count > 0)
                    {
                        var nomiIntrusi = new List<string>();
                        foreach (var f in intruse) { nomiIntrusi.Add(NervaturaNome(f)); mie.Add(f); }
                        return Fallito(
                            "Col " + verso + " la nervatura non e' nata, ma l'albero e' cresciuto lo stesso («" +
                            string.Join("», «", nomiIntrusi) + "»): non provo l'altro verso sopra una " +
                            "lavorazione che non ho chiesto. Di solito la linea non sta fra due pareti: " +
                            "spostala nel vuoto, con gli estremi (o il loro prolungamento) sulle pareti da " +
                            "unire; per un rilievo su una faccia piana usa «estrudi_su_faccia»." +
                            NervaturaRitira(modello, mie, foto));
                    }
                    continue;
                }
                // UNA NERVATURA NATA CHE NON AGGIUNGE MATERIALE. La sonda non
                // l'ha mai vista: nel verso sbagliato non nasceva niente. Se
                // succede non si prova l'altro verso (togliere la sola
                // nervatura lasciando lo schizzo non e' misurato): si ritira
                // tutto e si dice.
                mie.Add(nata);
                double? dopo = VolumeMm3(modello);
                if (dopo == null || dopo.Value - foto.Volume.Value < NervaturaCrescitaMinimaMm3)
                    return Fallito(string.Format(CultureInfo.InvariantCulture,
                        "Con il {0} SOLIDWORKS ha fatto nascere «{1}», ma il volume del pezzo {2}: una " +
                        "nervatura che non aggiunge materiale non irrigidisce niente. Di solito la linea " +
                        "sta dentro il materiale invece che nel vuoto fra le pareti: prova a spostarla nel vuoto, " +
                        "con gli estremi (o il loro prolungamento) sulle pareti da unire.{3}",
                        verso, NervaturaNome(nata),
                        dopo == null ? "non si rilegge" : "e' rimasto " + dopo.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3",
                        NervaturaRitira(modello, mie, foto)));
                rib = nata;
                girato = gira;
                esiti.Add(verso + ": nata «" + NervaturaNome(nata) + "»");
                break;
            }

            if (rib == null)
                return Fallito(
                    "La nervatura non e' nata in nessuno dei due versi del materiale (" +
                    string.Join("; ", esiti) + "). Una nervatura riempie, nel piano dello schizzo, lo " +
                    "spazio fra la linea e le pareti del pezzo che la linea prolungata incontra: " +
                    "l'angolo di una L, il fondo e i fianchi di una scatola svuotata. Quando non nasce " +
                    "in nessun verso, di solito dalla linea non c'e' niente da incontrare: una piastra " +
                    "senza pareti (misurato), o una linea fuori dal pezzo. Per un rilievo su una faccia " +
                    "piana usa «estrudi_su_faccia»; oppure sposta la linea fra due pareti." +
                    NervaturaRitira(modello, mie, foto));

            // ── LE GUARDIE, SUL PEZZO E NON SULLA FEATURE ────────────────
            bool ricostruito = true;
            try { ricostruito = modello.EditRebuild3(); } catch { }
            string rotto = CosaSiERotto(modello, foto, ricostruito, aperture: false);
            if (rotto != null)
                return Fallito("La nervatura «" + NervaturaNome(rib) + "» e' nata, ma " + rotto + "." +
                               NervaturaRitira(modello, mie, foto));

            double? volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            double? aggiunto = volumeDopo - foto.Volume;

            // COSA DICHIARA LA FEATURE (IRibFeatureData2), accanto a cio' che
            // si e' misurato. Non decide niente: lo spessore e il verso
            // dichiarati sono quelli chiesti anche quando il pezzo e' un altro
            // (la lezione dello sformo, `angolo_dichiarato_dalla_feature`).
            object spessoreDichiarato = null, versoDichiarato = null, paralleloDichiarato = null;
            try
            {
                var d = rib.GetDefinition() as IRibFeatureData2;
                if (d != null)
                {
                    spessoreDichiarato = Math.Round(Unita.Mm(d.Thickness), 3);
                    versoDichiarato = d.FlipSide;
                    paralleloDichiarato = d.ExtrusionDirection == (int)swRibExtrusionDirection_e.swRibParallelToSketch;
                }
            }
            catch { }

            var misure = new Dictionary<string, object>
            {
                // CHIESTI, E DETTI COSI': sono gli argomenti (e la lunghezza
                // della linea fatta con quelli), non misure. Le misure sono il
                // volume, i corpi, i tentativi e i «declared_*» riletti.
                ["requested_thickness_mm"] = Math.Round(spessore, 3),
                ["requested_sketch_plane"] = piano,
                ["requested_plane_offset_mm"] = Math.Round(distanza, 3),
                ["requested_line_length_mm"] = Math.Round(lunga, 3),
                ["plane_created"] = pianoFattoQui,
                ["material_side"] = girato ? "flipped" : "straight",
                ["attempts"] = tentativi,
                ["added_material_mm3"] = NumeroOppureNull(aggiunto, 2),
                ["volume_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["bodies"] = NumeroOppureNull(corpiDopo),
                ["declared_thickness_mm"] = spessoreDichiarato,
                ["declared_flip_side"] = versoDichiarato,
                ["declared_parallel_to_sketch"] = paralleloDichiarato,
                ["not_checked"] =
                    "Ho misurato che la nervatura e' nata e che il pezzo e' cresciuto, e che non ha rotto " +
                    "niente; non ho guardato QUALI pareti ha unito. Il verso del materiale l'ho provato io " +
                    "(prima dritto, poi girato) e ho tenuto il primo in cui nasce: se la linea sta fra " +
                    "pareti da tutte e due le parti, il verso dritto vince anche se tu pensavi l'altro. " +
                    "Nessuno sformo: la nervatura ha le facce dritte.",
            };
            if (volumeDopo == null) misure["volume_non_riletto"] = NotaVolumeNonRiletto;

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] nervatura {0} su {1}{2}: {3}, volume {4} -> {5}",
                NervaturaNome(rib), piano, pianoFattoQui ? " a " + distanza.ToString("0.###", CultureInfo.InvariantCulture) : "",
                string.Join("; ", esiti), Letto(foto.Volume), Letto(volumeDopo)));

            // Nessuna direzione nell'esito: lo spessore va lungo la normale del
            // piano, ma le facce della nervatura non sono il «fondo» di niente.
            return Riuscito(rib, misure);
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
        private static string NervaturaInserisci(IModelDoc2 modello, IFeature schizzo, double spessoreMm, bool giraMateriale)
        {
            try
            {
                modello.ClearSelection2(true);
                string nome = NervaturaNome(schizzo);
                bool preso = false;
                try { preso = modello.Extension.SelectByID2(nome, "SKETCH", 0, 0, 0, false, 0, null, 0); } catch { }
                if (!preso) preso = schizzo.Select2(false, 0);
                if (!preso) return "lo schizzo non si seleziona";
                modello.FeatureManager.InsertRib(
                    Is2Sided: true,                 // spessore diviso a meta' dalle due parti del piano
                    ReverseThicknessDir: false,     // vale solo per una nervatura da un lato
                    Thickness: Unita.M(spessoreMm),
                    ReferenceEdgeIndex: 0,
                    ReverseMaterialDir: giraMateriale,
                    IsDrafted: false,
                    DraftOutward: false,
                    DraftAngle: 0,
                    IsNormToSketch: false,          // PARALLELA allo schizzo: il nono argomento dell'esempio
                    IsDraftedFromWall: false);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { try { modello.ClearSelection2(true); } catch { } }
        }

        /// <summary>
        /// Il piano parallelo al piano base, sfalsato di `distanzaMm`. La stessa
        /// strada di `crea_piano` (CreatePlaneAtOffset3 dopo aver selezionato il
        /// piano base per nome, tipo "PLANE", marca 1), misurata dal banco nei
        /// casi di `casi/piano`; ed e' la chiamata della sonda
        /// (`CreatePlaneAtOffset3(0.025, false, true)`). Il valore di ritorno e'
        /// un RefPlane e non una Feature: chi decide e' l'albero.
        /// </summary>
        private static IFeature NervaturaPianoSfalsato(IModelDoc2 modello, string piano, double distanzaMm, out string motivo)
        {
            motivo = null;
            var basePiano = TrovaPianoBase(modello, piano);
            if (basePiano == null) { motivo = "Non ho trovato il piano «" + piano + "» nell'albero."; return null; }
            modello.ClearSelection2(true);
            bool selezionato = false;
            try { selezionato = modello.Extension.SelectByID2(NervaturaNome(basePiano), "PLANE", 0, 0, 0, false, SelectionMarks.OffsetPlaneReference, null, 0); } catch { }
            if (!selezionato) selezionato = basePiano.Select2(false, SelectionMarks.OffsetPlaneReference);
            if (!selezionato) { motivo = "Non sono riuscito a selezionare il piano «" + piano + "»."; return null; }

            int pianiPrima = ContaPiani(modello);
            try
            {
                modello.CreatePlaneAtOffset3(Unita.M(Math.Abs(distanzaMm)), distanzaMm < 0, true);
            }
            catch (Exception ex) { motivo = "Il piano della nervatura non e' nato: " + ex.Message; return null; }
            finally { try { modello.ClearSelection2(true); } catch { } }

            int pianiDopo = ContaPiani(modello);
            var ultima = UltimaFeature(modello);
            string tipo = "";
            try { tipo = ultima?.GetTypeName2() ?? ""; } catch { }
            if (pianiPrima < 0 || pianiDopo != pianiPrima + 1 || tipo != "RefPlane")
            {
                motivo = "Il piano della nervatura, parallelo al " + piano + " a " +
                         distanzaMm.ToString("0.###", CultureInfo.InvariantCulture) +
                         " mm, non e' nato (piani " + pianiPrima + " -> " + pianiDopo + "). Puoi farlo prima " +
                         "con «crea_piano» e guardare dove finisce.";
                return null;
            }
            return ultima;
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
        private string NervaturaRitira(IModelDoc2 modello, List<IFeature> mie, FotoDelPezzo prima)
        {
            if (mie.Count == 0) return " Non ho toccato il pezzo.";
            bool tutto = true;
            var nomi = new List<string>();
            for (int i = mie.Count - 1; i >= 0; i--)
            {
                string nome = NervaturaNome(mie[i]);
                nomi.Add(nome);
                if (string.IsNullOrEmpty(nome) || TrovaLavorazione(modello, nome) == null) continue;   // assorbita da chi l'ha tolta prima
                try
                {
                    if (mie[i].IGetChildCount() > 0) { tutto = false; continue; }
                    modello.ClearSelection2(true);
                    if (!mie[i].Select2(false, 0)) { tutto = false; continue; }
                    if (!modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed))
                        tutto = false;
                }
                catch (Exception ex)
                {
                    _log("[agente] nervatura: non riesco a togliere «" + nome + "»: " + ex.Message);
                    tutto = false;
                }
                finally { try { modello.ClearSelection2(true); } catch { } }
            }
            try { modello.EditRebuild3(); } catch { }
            // SI RILEGGE L'ALBERO, NON IL VALORE DI RITORNO.
            foreach (var n in nomi)
                if (!string.IsNullOrEmpty(n) && TrovaLavorazione(modello, n) != null) tutto = false;
            nomi.Reverse();
            return ETornatoComEra(modello, prima, tutto,
                "il pezzo come l'avevo trovato (tolti «" + string.Join("», «", nomi) + "»)");
        }

        /// <summary>I nomi delle feature di quel tipo, adesso.</summary>
        private static HashSet<string> NervaturaNomiDelTipo(IModelDoc2 modello, string tipo)
        {
            var nomi = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == tipo) nomi.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return nomi;
        }

        /// <summary>I nomi di tutte le feature dell'albero, adesso.</summary>
        private static HashSet<string> NervaturaNomiDiTutte(IModelDoc2 modello)
        {
            var nomi = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    nomi.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return nomi;
        }

        /// <summary>Le feature che prima non c'erano, dalla piu' vecchia alla piu' giovane.</summary>
        private static List<IFeature> NervaturaNuoveDiTutte(IModelDoc2 modello, HashSet<string> prima)
        {
            var nuove = new List<IFeature>();
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (!prima.Contains(f.Name)) nuove.Add(f);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return nuove;
        }

        /// <summary>L'ultima feature di quel tipo che prima non c'era, o null.</summary>
        private static IFeature NervaturaNuovaDelTipo(IModelDoc2 modello, string tipo, HashSet<string> prima)
        {
            IFeature nuova = null;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == tipo && !prima.Contains(f.Name)) nuova = f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return nuova;
        }

        private static string NervaturaNome(IFeature f)
        {
            try { return f?.Name ?? ""; } catch { return ""; }
        }
    }
}
