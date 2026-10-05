using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// LE SETTE OPERAZIONI NON ANCORA PROVATE.
    ///
    /// PERCHE' ESISTE IL CAMPO «provata» IN ai_disegno.py.
    /// Una firma verificata per riflessione non basta. HoleWizard5, chiamata
    /// con la firma giusta e i parametri giusti, costruiva un foro da 50,8 mm
    /// — due pollici — con il nome giusto nell'albero e senza dare nessun
    /// errore. Il difetto si vedeva solo misurando il volume asportato.
    ///
    /// Ognuna di queste sette ha almeno un dubbio che la riflessione non puo'
    /// sciogliere, e che se sbagliato produce un pezzo diverso da quello
    /// confermato SENZA dare errore:
    ///
    ///   chamfer_edges  swChamferType_e: e' davvero uno smusso o un raccordo?
    ///   rivoluzione     l'auto-selezione risolve profilo E centerline?
    ///   guscio          «Outward» vuol dire davvero verso l'esterno?
    ///   create_plane      il segno: valore assoluto + OptionFlip, o segnato?
    ///   create_axis       l'asse si ritrova poi per nome con SelectByID2?
    ///   linear_pattern  il Mark della direzione, e da che parte va
    ///   specchia        i Mark del piano (1) e della lavorazione (4)
    ///
    /// Ogni prova COSTRUISCE e MISURA: volume asportato, ingombro, posizione
    /// dei cilindri. Le posizioni non si assumono mai dai parametri chiesti —
    /// si rileggono dalla geometria, perche' il punto della prova e' proprio
    /// sapere se il pezzo e' finito dove si credeva.
    ///
    /// Quelle che passano diventano `provata=True` in ai_disegno.py, nello
    /// stesso commit in cui il RAG registra i numeri misurati.
    /// </summary>
    internal static partial class Program
    {
        // L'ELENCO DI QUESTE PROVE NON STA PIU' QUI.
        //
        // Fino alla 0.40.0 c'era un `NuoveOperazioni()` che le chiamava in fila.
        // Dalla voce F13 stanno nella tabella dei gruppi, in `Program.cs`:
        // **nello stesso ordine**, con un nome davanti a ognuna, cosi' che
        // `--gruppo guscio` ne lanci una sola. Due elenchi — uno per lanciarle
        // tutte e uno per lanciarne una — sarebbero due posti da tenere
        // allineati, e il giorno in cui divergono il banco lancia meno prove di
        // quante ne mostra.

        // ==================================================================
        // V3/V7. la forma detta in una frase: ADESSO SONO DUE FILE
        // ==================================================================
        //
        // `cases\sentence\clean_shaft_declares_profile.json` e
        // `cases\sentence\drilled_shaft_no_longer_declares_profile.json` (voce
        // F11). Sono diventati file quando le attese hanno imparato a
        // chiedere Â«contieneÂ» e Â«non_contieneÂ» su una frase invece che
        // Â«ugualeÂ» su un numero.
        //
        // IL ROSSO STA TUTTO IN Â«non_contieneÂ». Una frase che dichiara il
        // profilo dei tre diametri su un pezzo che quel profilo non ce l'ha
        // piu' non fallisce niente: e' la descrizione giusta di un ALTRO
        // pezzo, e
        // il modello ci costruisce sopra. Si prende solo pretendendo che una
        // parola SPARISCA.


        // ==================================================================
        // R2. suppress_feature ed delete_feature: ADESSO SONO FILE
        // ==================================================================
        //
        // `cases\removal\*.json` (voce F11) - cinque casi, gli stessi numeri:
        // tre fori da 14 su una piastra 200x100x20, il terzo spento e il
        // volume che torna ESATTAMENTE a quello con due, riacceso e torna a
        // quello con tre; il primo cancellato; e i tre rifiuti - il nome
        // inventato, la soppressione chiesta due volte, l'estrusione di base
        // che porterebbe via i figli.
        //
        // IL FILE PINZA DI PIU'. Il C# guardava il volume e il campo
        // Â«soppressaÂ»; i casi guardano anche QUALE foro si e' spento -
        // `centers_x_mm` passa da [-60, 0, 60] a [-60, 0] - e quante
        // lavorazioni restano nell'albero, che e' tutta la differenza fra
        // sopprimere e cancellare. Si lanciano con `--gruppo casi`.



        /// <summary>
        /// LO SCATTO: una vista del pezzo, salvata su file.
        ///
        /// A COSA SERVE. Il modello in uso su un server GPU interno (qwen3.6:35b) dichiara la
        /// capacita' «vision»: sa leggere le immagini. Quindi «far vedere il
        /// pezzo alla AI» puo' essere LETTERALE, non solo una lista di numeri.
        ///
        /// Prima di costruire tutta la catena — add-in che scatta, server che
        /// allega, guardia di capacita' — va risposto a una domanda che nessuna
        /// documentazione puo' risolvere: **quel modello capisce davvero uno
        /// screenshot di un CAD?** Grigio su grigio, senza quote, in
        /// prospettiva. Se la risposta e' no, si e' speso mezz'ora invece di
        /// due giorni.
        ///
        /// Questo metodo produce il materiale della prova: un pezzo di cui
        /// sappiamo TUTTO, e la sua vista. Il confronto lo fa poi chi manda
        /// l'immagine al modello e legge cosa risponde.
        ///
        /// `SaveBMP` sta su IModelDoc2 e non chiede niente all'add-in: si puo'
        /// scattare senza aver ancora scritto una riga di operazione.
        /// Verificata per riflessione:
        ///   IModelDoc2.SaveBMP(String FileNameIn, Int32 WidthIn, Int32 HeightIn)
        ///   IModelDoc2.ShowNamedView2(String VName, Int32 ViewId)
        ///   IModelDoc2.ViewZoomtofit2()
        /// </summary>
        private static void CaptureView()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 200, 120, 20);
                var state0 = ReadState(doc);
                string front = FaceWithNormal(state0, "front");
                string error, holeName;
                RunWithName(doc, "hole_wizard", new Dictionary<string, object>
                {
                    ["face_uid"] = front,
                    ["variant"] = "threaded",
                    ["size"] = "M8",
                    ["through_all"] = true,
                    ["positions"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = 40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = 40.0 },
                    },
                }, out error, out holeName, state0);

                // La selezione va tolta: le facce evidenziate in verde
                // cambiano l'immagine e non fanno parte del pezzo.
                doc.ClearSelection2(true);
                doc.ViewDisplayShaded();

                // DOVE FINISCONO GLI SCATTI: accanto all'eseguibile del banco,
                // in `lavoro\viste`, che sta nel .gitignore.
                //
                // Era un percorso assoluto dentro una cartella temporanea di
                // sessione, nome utente compreso. Sbagliato due volte: quella
                // cartella non esiste piu', e il nome di chi lavora non deve
                // uscire da questa macchina (`Checks\check-publishability.ps1`).
                string folder = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".",
                    "lavoro", "views");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

                // Piu' viste, perche' UNA SOLA NON BASTA e vale la pena
                // misurarlo: da un'isometrica non si vede se un foro e'
                // passante o cieco, e non si vede il retro.
                // I NOMI DELLE VISTE SONO IN INGLESE ANCHE SU UNA
                // INSTALLAZIONE ITALIANA. Con «*Isometrica» ShowNamedView2
                // non protesta e non fa niente: lo scatto esce con la vista
                // che c'era prima. E' la stessa trappola dei nomi dei piani
                // gia' documentata nel RAG — e se l'immagine sbagliata fosse
                // arrivata al modello, la colpa sarebbe sembrata sua.
                // NE' IL NOME ITALIANO NE' QUELLO INGLESE: NON E' IL NOME.
                // `ShowNamedView2` con «*Isometrica» e con «*Isometric» ha
                // prodotto la STESSA IMMAGINE, cioe' non ha fatto niente in
                // nessuno dei due casi. Si passa allora l'identificativo
                // numerico della vista standard, che e' il secondo parametro
                // e non dipende da nessuna lingua:
                //   swFrontView=1 swTopView=5 swIsometricView=7 swTrimetricView=8
                var views = new[]
                {
                    Tuple.Create("isometric", (int)swStandardViews_e.swIsometricView),
                    Tuple.Create("trimetric", (int)swStandardViews_e.swTrimetricView),
                    Tuple.Create("front", (int)swStandardViews_e.swFrontView),
                    Tuple.Create("top", (int)swStandardViews_e.swTopView),
                };
                int done = 0;
                foreach (var view in views)
                {
                    try
                    {
                        doc.ShowNamedView2("", view.Item2);
                        doc.GraphicsRedraw2();
                        // LO ZOOM VA DOPO IL RIDISEGNO, e va ridisegnato
                        // ancora: al primo giro il pezzo usciva tagliato sul
                        // bordo destro perche' l'adattamento lavorava sulla
                        // vista vecchia.
                        doc.ViewZoomtofit2();
                        doc.GraphicsRedraw2();
                        string itemName = view.Item1;
                        string file = System.IO.Path.Combine(folder, "piastra_" + itemName + ".bmp");
                        if (doc.SaveBMP(file, 1024, 768) && System.IO.File.Exists(file))
                        {
                            done++;
                            Console.WriteLine("    " + itemName + ": " +
                                new System.IO.FileInfo(file).Length / 1024 + " KB");
                        }
                    }
                    catch (Exception ex) { Console.WriteLine("    " + view.Item1 + " non riuscita: " + ex.Message); }
                }
                Console.WriteLine();
                Console.WriteLine("=== scatto della vista ===");
                Console.WriteLine("    cartella: " + folder);
                Check("scatta la vista del pezzo", done > 0,
                      done + " viste su " + views.Length + " salvate in " + folder);
            }
            catch (Exception ex) { Check("scatta la vista del pezzo", false, Why(ex)); }
            finally { Close(doc); }
        }

        /// <summary>
        /// QUANTO VEDE IL MODELLO, su un pezzo con delle lavorazioni sopra.
        ///
        /// NON E' UNA PROVA, E' UN CENSIMENTO. Il committente ha detto che la
        /// AI e' «parzialmente cieca» e che va fatta vedere bene. Prima di
        /// decidere cosa aggiungere allo stato bisogna sapere cosa c'e' —
        /// misurato su un pezzo vero, non sulla piastra nuda.
        ///
        /// Il pezzo: piastra 200x120x20, quattro fori M8, un raccordo. Cioe'
        /// il minimo su cui un progettista direbbe «quel foro portalo a M10».
        /// </summary>
        private static void HowMuchTheModelSees()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 200, 120, 20);
                var state0 = ReadState(doc);
                string front = FaceWithNormal(state0, "front");
                string error, holeName;
                RunWithName(doc, "hole_wizard", new Dictionary<string, object>
                {
                    ["face_uid"] = front,
                    ["variant"] = "threaded",
                    ["size"] = "M8",
                    ["through_all"] = true,
                    ["positions"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = 40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = 40.0 },
                    },
                }, out error, out holeName, state0);

                var docState = ReadState(doc);
                string json = Json(docState);

                Console.WriteLine();
                Console.WriteLine("=== quanto vede il modello (piastra + 4 fori M8) ===");
                Console.WriteLine("  lo stato pesa " + json.Length + " caratteri");
                foreach (var key in docState.Keys.OrderBy(k => k))
                {
                    var v = docState[key];
                    var itemList = v as System.Collections.IList;
                    Console.WriteLine(string.Format("  {0,-22} {1}",
                        key,
                        itemList != null ? itemList.Count + " voci" : Convert.ToString(v)));
                }

                // LE FEATURE: e' qui che si vede la cecita'. Un progettista che
                // dice «quel foro portalo a M10» sta indicando una feature, e
                // il modello di quella feature deve sapere almeno CHE FORO E'.
                Console.WriteLine();
                Console.WriteLine("  --- cosa dice lo stato di ogni lavorazione ---");
                var feature = docState["feature"] as System.Collections.IList;
                if (feature != null)
                    foreach (var f in feature)
                    {
                        var d = f as Dictionary<string, object>;
                        if (d == null) { Console.WriteLine("    " + f); continue; }
                        Console.WriteLine("    " + string.Join(", ",
                            d.Keys.Select(k => k + "=" + Convert.ToString(d[k]))));
                    }

                Console.WriteLine();
                Console.WriteLine("  --- una faccia, per esteso ---");
                var faces = docState["faces"] as System.Collections.IList;
                if (faces != null && faces.Count > 0)
                {
                    var d = faces[0] as Dictionary<string, object>;
                    if (d != null)
                        foreach (var k in d.Keys)
                            Console.WriteLine("    " + k + " = " + Describe(d[k]));
                }
                Check("censimento di cosa vede il modello", true,
                      json.Length + " caratteri, " + (feature == null ? 0 : feature.Count) +
                      " lavorazioni, " + (faces == null ? 0 : faces.Count) + " facce");
            }
            catch (Exception ex) { Check("censimento di cosa vede il modello", false, Why(ex)); }
            finally { Close(doc); }
        }

        private static string Describe(object v)
        {
            var itemList = v as System.Collections.IEnumerable;
            if (itemList != null && !(v is string))
                return "[" + string.Join(",", itemList.Cast<object>().Select(o => Convert.ToString(o))) + "]";
            return Convert.ToString(v);
        }

        /// <summary>
        /// Che facce ha davvero la piastra di prova.
        ///
        /// NON E' UNA PROVA, E' UNA DOMANDA. Al primo giro dentro SolidWorks
        /// quindici casi sono morti tutti sulla stessa riga — «nessuna faccia
        /// davanti» — e la causa poteva stare in tre posti diversi: la piastra
        /// costruita su un piano che non e' quello che credevo, lo stato che
        /// non elenca quella faccia, o `Facing` che la chiama in un altro modo.
        /// Stampare l'elenco toglie di mezzo la deduzione.
        /// </summary>
        private static void PrintPlateFaces()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 200, 120, 20);
                var docState = ReadState(doc);
                Console.WriteLine();
                Console.WriteLine("=== che facce ha la piastra di prova 200x120x20 ===");
                var faces = docState["faces"] as System.Collections.IList;
                Console.WriteLine("  corpi solidi: " + CountBodies(doc) +
                                  ", facce nello stato: " + (faces == null ? 0 : faces.Count));
                if (faces != null)
                    foreach (Dictionary<string, object> f in faces)
                    {
                        object v, n, area;
                        f.TryGetValue("facing", out v);
                        f.TryGetValue("normal", out n);
                        f.TryGetValue("area_mm2", out area);
                        Console.WriteLine(string.Format(
                            "  {0,-4} verso={1,-9} area={2,-10} normale={3}",
                            f["uid"], v ?? "(nessuno)", area,
                            n is System.Collections.IEnumerable && !(n is string)
                                ? "[" + string.Join(",", (n as System.Collections.IEnumerable)
                                    .Cast<object>().Select(o => Convert.ToString(o))) + "]"
                                : Convert.ToString(n)));
                    }
            }
            catch (Exception ex) { Console.WriteLine("  diagnostica fallita: " + ex.Message); }
            finally { Close(doc); }
        }

        // ==================================================================
        // 00. move_bodies — il baricentro dice se la distanza e' quella giusta
        // ==================================================================
        //
        // LA DOMANDA A CUI QUESTE PROVE RISPONDONO. `InsertMoveCopyBody2`
        // tiene la direzione (TransformX/Y/Z) e il modulo (TransformValue) in
        // due campi distinti, e da nessuna parte e' scritto quale dei due
        // comanda. L'add-in passa una direzione UNITARIA e la distanza in
        // TransDist. Se la lettura giusta fosse un'altra — per esempio i
        // TransX/Y/Z come spostamento assoluto — il pezzo si sposterebbe di
        // una distanza sbagliata SENZA NESSUN ERRORE.
        //
        // Il banco non si fida ne' di SolidWorks ne' del controllo interno
        // dell'operazione: rimisura il baricentro per conto proprio.
        private static void TestMoveBodies()
        {
            Console.WriteLine();
            Console.WriteLine("=== move_bodies (add-in) ===");

            // Due piastre uguali, staccate: 100x100x10 = 100.000 mm3 ciascuna.
            // Spostandone UNA di 50 mm in Z, il baricentro di tutto il pezzo si
            // sposta della meta': 25 mm. E' un'uguaglianza, non una stima.
            Move("sposta una piastra di 50 in Z", 0, 0, 50, false, 0,
                   expectedBodies: 2, volumeExpected: 200000, centerMoveZ: 25);
            // NOTA per chi passa di qui: il 15/9/2026 questo 200000 e' stato
            // messo a 199000 apposta, per vedere `bench.ps1` uscire 1 e
            // nominare il caso con atteso e misurato nel resoconto. E' il
            // criterio di chiusura della voce F2, e va rifatto cosi' se
            // qualcuno tocca il resoconto.
            //
            // E lo stesso giorno, con un errore di sintassi messo qui apposta,
            // si e' visto `bench.ps1` fermarsi PRIMA di lanciare e uscire 2:
            // e' il difetto che la voce F2 nominava per nome — uno script che
            // esce 0 perche' la compilazione e' fallita e nessuno ha guardato.
            Move("sposta di 600 in Z", 0, 0, 600, false, 0,
                   expectedBodies: 2, volumeExpected: 200000, centerMoveZ: 300);
            Move("sposta all'indietro, -120 in Z", 0, 0, -120, false, 0,
                   expectedBodies: 2, volumeExpected: 200000, centerMoveZ: -60);

            // Copia: gli originali restano, e ne compare una in piu'.
            // Tre corpi da 100.000, e la copia sta a +600.
            // baricentroZ = (100000*0 + 100000*0 + 100000*600) / 300000 = 200.
            Move("copia una piastra a +600 in Z (il castello)", 0, 0, 600, true, 1,
                   expectedBodies: 3, volumeExpected: 300000, centerMoveZ: 200);
            // Due copie: +600 e +1200.
            // baricentroZ = (0 + 0 + 600 + 1200) / 4 = 450.
            Move("due copie, a +600 e +1200", 0, 0, 600, true, 2,
                   expectedBodies: 4, volumeExpected: 400000, centerMoveZ: 450);

            // Spostamento nullo: l'add-in deve rifiutarlo da solo, senza
            // aspettare il validatore del server.
            Move("spostamento nullo (deve fallire)", 0, 0, 0, false, 0,
                   expectedBodies: 0, volumeExpected: 0, centerMoveZ: 0,
                   mustFail: true);
            Move("copia con spostamento nullo (deve fallire)", 0, 0, 0, true, 1,
                   expectedBodies: 0, volumeExpected: 0, centerMoveZ: 0,
                   mustFail: true);
            // Un corpo che non c'e'.
            Move("corpo inesistente (deve fallire)", 0, 0, 50, false, 0,
                   expectedBodies: 0, volumeExpected: 0, centerMoveZ: 0,
                   mustFail: true, fakeUid: "C99");
        }

        private static void Move(string itemName, double dx, double dy, double dz,
                                   bool copy, int copies,
                                   int expectedBodies, double volumeExpected,
                                   double centerMoveZ,
                                   bool mustFail = false, string fakeUid = null)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                // Due corpi separati: il secondo NON si fonde col primo.
                PlateWhere(doc, 100, 100, 10, 0, 0, bottoms: true);
                PlateWhere(doc, 100, 100, 10, 200, 0, bottoms: false);
                if (CountBodies(doc) != 2)
                {
                    Check(itemName, false, "il pezzo di partenza ha " + CountBodies(doc) + " corpi invece di 2");
                    return;
                }

                var docState = ReadState(doc);
                var uid = BodiesUid(docState);
                if (uid.Count < 2) { Check(itemName, false, "lo stato elenca " + uid.Count + " corpi"); return; }

                double centerBefore = DocumentCenterZ(doc);
                var arguments = new Dictionary<string, object>
                {
                    // Si muove UN corpo solo: e' cio' che rende il baricentro
                    // una misura sensibile. Muovendoli tutti, il baricentro si
                    // sposterebbe esattamente di d e non distinguerebbe un
                    // corpo dimenticato indietro.
                    ["bodies_uid"] = new List<string> { fakeUid ?? uid[1] },
                    ["dx_mm"] = dx,
                    ["dy_mm"] = dy,
                    ["dz_mm"] = dz,
                    ["copy"] = copy,
                };
                if (copy) arguments["copy_count"] = copies;

                string error;
                var measurements = Run(doc, "move_bodies", arguments, out error, docState);
                if (mustFail)
                {
                    Check(itemName, measurements == null && !string.IsNullOrEmpty(error),
                          measurements == null ? "rifiutato: " + Short(error) : "NON ha rifiutato");
                    return;
                }
                if (measurements == null) { Check(itemName, false, "fallita: " + Short(error)); return; }

                int bodies = CountBodies(doc);
                double volume = DocumentVolume(doc);
                double centerAfter = DocumentCenterZ(doc);
                double moved = centerAfter - centerBefore;

                bool ok = bodies == expectedBodies
                          && Math.Abs(volume - volumeExpected) < Math.Max(2, volumeExpected * 0.005)
                          && Math.Abs(moved - centerMoveZ) < 0.05;
                Check(itemName, ok, string.Format(
                    "{0} corpi (attesi {1}), volume {2:0.#} (atteso {3:0.#}), " +
                    "baricentro Z spostato di {4:0.###} mm (atteso {5:0.###})",
                    bodies, expectedBodies, volume, volumeExpected, moved, centerMoveZ));
            }
            catch (Exception ex) { Check(itemName, false, Why(ex)); }
            finally { Close(doc); }
        }

        /// <summary>Gli identificativi dei corpi, come li elenca lo stato.</summary>
        private static List<string> BodiesUid(Dictionary<string, object> docState)
        {
            var outside = new List<string>();
            object rawItems;
            if (docState == null || !docState.TryGetValue("bodies", out rawItems)) return outside;
            var itemList = rawItems as System.Collections.IEnumerable;
            if (itemList == null) return outside;
            foreach (var c in itemList)
            {
                var d = c as Dictionary<string, object>;
                object u;
                if (d != null && d.TryGetValue("uid", out u) && u != null) outside.Add(u.ToString());
            }
            return outside;
        }

        /// <summary>
        /// Il baricentro del pezzo lungo Z, in mm. E' la misura con cui il
        /// banco controlla lo spostamento per conto proprio, senza fidarsi di
        /// quello che l'operazione dichiara di aver fatto.
        /// </summary>
        private static double DocumentCenterZ(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return 0;
            double sum = 0, volume = 0;
            foreach (var c in bodies)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) continue;
                double v = mp[3] * 1e9;
                if (v <= 0) continue;
                volume += v;
                sum += mp[2] * 1000.0 * v;
            }
            return volume > 0 ? sum / volume : 0;
        }

        /// <summary>
        /// Una piastra centrata in (x, y). Con `fondi` a falso resta un corpo
        /// SEPARATO invece di fondersi con quello che c'e' gia': serve per
        /// avere piu' corpi da spostare.
        /// </summary>
        private static void PlateWhere(IModelDoc2 doc, double L, double W, double H,
                                        double x, double y, bool bottoms)
        {
            var planes = ReferencePlanes(doc);
            doc.ClearSelection2(true);
            planes[0].Select2(false, 0);
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(x / 1000.0, y / 1000.0, 0,
                                     (x + L / 2) / 1000.0, (y + W / 2) / 1000.0, 0);
            sm.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0,
                H / 1000.0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                bottoms, true, true, 0, 0, false);
            doc.ClearSelection2(true);
        }

        // ── L'ASOLA E LO SMUSSO ADESSO SONO FILE ──────────────────────────
        //
        // `cases\slot\*.json` — quindici casi: la stessa area 535,0332 mm²,
        // le stesse cinque facce di costa, la rotazione vista su una piastra
        // stretta, i tre rifiuti con le loro gemelle.
        //
        // `cases\chamfer\*.json` — tre casi: 5x5 a 45 gradi (1250 mm³, dove un
        // raccordo R5 ne toglierebbe 537), il cianfrino 3x6 a due lati, e lo
        // smusso piu' grande dello spigolo.
        //
        // In tutti e due i gruppi i numeri sono **esatti**: le tolleranze in
        // percentuale che stavano qui — il 2% dell'asola, il 3% dello smusso —
        // la voce F3 le vieta. Si lanciano con `--gruppo casi`.

        // ==================================================================
        // 2. rivoluzione ADESSO E' UN GRUPPO DI FILE
        // ==================================================================
        //
        // `cases\revolve\*.json` (voce F11) - cinque casi: il cilindro, lo
        // albero a gradini dell'esempio, il mezzo giro, la boccola cava e il
        // profilo a cavallo dell'asse con la sua gemella.
        //
        // LA TOLLERANZA E' LA DIFFERENZA. Qui si accettava `atteso * 0.02`:
        // sul cilindro da 62.831 mm3 sono 1.256 mm3 di scarto ammesso, piu'
        // di un foro M20 passante. Nei file e' +-0,5 mm3, e il perche' della
        // tolleranza e' scritto accanto (il conto ha pi greco dentro).

        // 3. guscio — il verso di «Outward», misurato sull'ingombro
        // ==================================================================
        // ── QUESTA PROVA ADESSO E' UN FILE ────────────────────────────────
        //
        // `cases/features/internal_shell.json` e `external_shell.json` (voce
        // F3). Gli stessi due numeri — 82.500 e 106.500 — piu' l'ingombro, le
        // facce tolte, il verso dichiarato e il materiale asportato, e
        // **leggibili da chi il C# non lo scrive**.
        //
        // Il C# qui sotto e' stato tolto, non lasciato accanto. Due prove sulla
        // stessa cosa vogliono dire che un giorno una delle due cambiera' e
        // l'altra no. E soprattutto: quella vecchia accettava
        // `expectedVolume * 0.02` — sui 240.000 mm³ della piastra sono **4.800
        // mm³, piu' di un foro M12 passante**. Una prova che accetta un foro di
        // scarto non prova il foro, e tenerla in giro come rete di sicurezza
        // sarebbe tenere in giro proprio il difetto che F3 chiude.
        // ==================================================================
        // 4. create_plane ADESSO E' UN GRUPPO DI FILE
        // ==================================================================
        //
        // `cases\plane\*.json` (voce F11) - tre casi. L'attesa non e' sulla
        // distanza che il ponte ricopia dalla richiesta, ma su DOVE E' FINITO
        // il piano: `last_plane_mm`, tre numeri letti dalla matrice del
        // piano creato. Il segno si vede solo cosi'. `--gruppo casi`.

        // 5. create_axis — e la domanda vera: si ritrova poi per nome?
        // ==================================================================
        //
        // ── QUASI TUTTA QUESTA FAMIGLIA ADESSO E' DUE FILE ────────────────
        //
        // `cases\axis\axis_from_hole_wall.json` e
        // `cases\axis\limit_axis_from_a_planar_face.json` (voce F11): l'asse che
        // compare nello stato, il volume che NON cambia — un asse di
        // riferimento non toglie materiale — e la faccia piana rifiutata con
        // un motivo che dice cosa fare.
        //
        // ── QUELLO CHE RESTA QUI, E PERCHE' NON PUO' DIVENTARE UN FILE ────
        //
        // La domanda vera non e' «l'asse esiste», e' «le ripetizioni lo
        // RITROVANO». Lo ritrovano con `SelectByID2(nome, "AXIS", ...)`: se
        // quella riga non aggancia, `create_axis` riesce, lo stato mostra
        // l'asse, e la ripetizione fallisce subito dopo — cioe' l'operazione
        // e' inutile e niente lo dice.
        //
        // Un caso dichiarato non puo' provarlo: `SelectByID2` non e'
        // un'operazione del catalogo, e' una chiamata COM. Resta in C#, e
        // resta scritto il perche'.
        private static void TestCreateAxis()
        {
            Console.WriteLine();
            Console.WriteLine("=== create_axis: l'asse si ritrova per nome? (add-in) ===");
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 120, 120, 15);
                var docState = ReadState(doc);
                string planarFace = FaceWithNormal(docState, "front");
                if (planarFace == null) { Check("create_axis: faccia di partenza", false, "assente"); return; }

                string error, holeName;
                var hole = RunWithName(doc, "hole_wizard", new Dictionary<string, object>
                {
                    ["face_uid"] = planarFace,
                    ["variant"] = "clearance",
                    ["size"] = "M10",
                    ["through_all"] = true,
                    ["positions"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = 0.0, ["y_mm"] = 0.0 },
                    },
                }, out error, out holeName, docState);
                if (hole == null) { Check("create_axis: foro di prova", false, Short(error)); return; }

                docState = ReadState(doc);
                string cylindrical = FaceNotPlanar(docState);
                if (cylindrical == null)
                {
                    Check("create_axis: la parete del foro compare nello stato", false,
                          "nessuna faccia con «planar: false»: il modello non avrebbe " +
                          "niente da passare a create_axis");
                    return;
                }

                string axisName;
                var measurements = RunWithName(doc, "create_axis", new Dictionary<string, object>
                {
                    ["face_uid"] = cylindrical,
                }, out error, out axisName, docState);
                if (measurements == null) { Check("create_axis: l'asse per il gancio", false, Short(error)); return; }
                string axis = measurements.ContainsKey("axis") ? Convert.ToString(measurements["axis"]) : "";
                if (string.IsNullOrEmpty(axis))
                {
                    // NON E' IL ROSSO DI QUESTA PROVA: senza il nome non c'e'
                    // niente da agganciare, e dirlo e' meglio che agganciare
                    // la stringa vuota e chiamarlo fallimento del gancio.
                    Check("create_axis: l'operazione dice come si chiama l'asse", false,
                          "le misure non hanno «axis»");
                    return;
                }

                // LA PROVA CHE CONTA: «linear_pattern» e «circular_pattern»
                // ritrovano l'asse con SelectByID2(nome, "AXIS", ...). Se
                // quella riga non aggancia, create_axis riesce, lo stato mostra
                // l'asse, e la ripetizione fallisce subito dopo — cioe'
                // l'operazione e' inutile e niente lo dice.
                bool attached = false;
                try
                {
                    doc.ClearSelection2(true);
                    attached = doc.Extension.SelectByID2(axis, "AXIS", 0, 0, 0, false, 1, null, 0);
                }
                catch { }
                Check("create_axis: l'asse si aggancia con SelectByID2(\"AXIS\")", attached,
                      attached ? "asse «" + axis + "»" : "la ripetizione non lo trovera' mai");
            }
            catch (Exception ex) { Check("create_axis", false, Why(ex)); }
            finally { Close(doc); }
        }
        // ==================================================================
        // 6. linear_pattern e 7. specchia ADESSO SONO DUE GRUPPI DI FILE
        // ==================================================================
        //
        // `cases\patterns\*.json` - tre fori a passo 40 lungo uno spigolo, la
        // griglia 3x2 su due spigoli, e la direzione presa da uno spigolo
        // CURVO, che va rifiutata.
        //
        // `cases\mirror\*.json` - il foro a (60, 25) specchiato sul piano
        // destro, che deve comparire a (-60, 25), e il piano inventato.
        //
        // IL FILE PINZA DI PIU' DEL C# CHE SOSTITUISCE. Qui lo specchio si
        // verificava cercando UNA copia lontana dal seme; il caso scrive i
        // due centri, tutti e due, e la y che NON si ribalta. E la prova
        // Â«nessuno spigolo curvo e' offerto al modelloÂ» che stava qui era
        // verde a prescindere: contava gli spigoli con Â«drittoÂ»: false, e
        // quel campo nello stato non esiste - si chiama Â«curvoÂ». Dopo un foro
        // passante gli spigoli curvi sono DUE, il modello li puo' nominare, e
        // il rifiuto e' un caso vero invece di una strada che si credeva
        // chiusa. Si lanciano con `--gruppo casi`.


        // ==================================================================
        // riflessione: come Esegui, ma dice anche come si chiama la feature
        // ==================================================================
        private static Dictionary<string, object> RunWithName(
            IModelDoc2 doc, string operation, Dictionary<string, object> arguments,
            out string error, out string createdFeature, object docState = null)
        {
            error = null;
            createdFeature = null;
            var constructor = _tOperations.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
            var op = constructor.Invoke(new object[] { _sw, (Action<string>)(s => { }) });
            object objectState = (docState == null || docState is Dictionary<string, object>)
                ? ReadObjectState(doc) : docState;

            var run = _tOperations.GetMethod("Run",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var outcome = run.Invoke(op, new object[] { doc, operation, arguments, objectState, null });
            if (!(bool)_tOutcome.GetField("Succeeded").GetValue(outcome))
            {
                error = _tOutcome.GetField("Error").GetValue(outcome) as string;
                return null;
            }
            createdFeature = _tOutcome.GetField("CreatedFeature").GetValue(outcome) as string;
            try { doc.EditRebuild3(); } catch { }
            return _tOutcome.GetField("Measurements").GetValue(outcome) as Dictionary<string, object>;
        }

        // ==================================================================
        // misure sul pezzo — sempre dalla geometria, mai dai parametri
        // ==================================================================

        /// <summary>
        /// Il volume solido in mm3. `GetMassProperties(densita)[3]` e' il
        /// volume in m3: misurato, non ricordato.
        /// </summary>
        private static double DocumentVolume(IModelDoc2 doc)
        {
            double v = 0;
            // Una TAVOLA non ha corpi. Da quando la sessione puo' tenere
            // aperti due documenti, il documento attivo puo' benissimo
            // essere il disegno, e qui si arrivava con un IPartDoc nullo.
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null) return 0;
            foreach (var c in bodies)
            {
                var mp = (c as IBody2).GetMassProperties(1000.0) as double[];
                if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
            }
            return v;
        }

        private static int CountBodies(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            return bodies == null ? 0 : bodies.Length;
        }

        private static double[] DocumentBoundingBox(IModelDoc2 doc)
        {
            var b = DocumentBox(doc);
            if (b == null) return null;
            return new[] { b[3] - b[0], b[4] - b[1], b[5] - b[2] };
        }

        private static int CountPlanes(IModelDoc2 doc)
        {
            int n = 0;
            var f = (doc as IPartDoc).IFirstFeature();
            while (f != null)
            {
                try { if (f.GetTypeName2() == "RefPlane") n++; } catch { }
                try { f = f.IGetNextFeature(); } catch { break; }
            }
            return n;
        }

        /// <summary>
        /// L'origine dell'ultimo piano di riferimento dell'albero, in mm.
        /// IMathTransform.ArrayData: 9 numeri di rotazione, poi 3 di
        /// traslazione. E' la risposta alla domanda «da che parte e' finito».
        /// </summary>
        private static double[] LastPlaneTranslation(IModelDoc2 doc)
        {
            IFeature last = null;
            var f = (doc as IPartDoc).IFirstFeature();
            while (f != null)
            {
                try { if (f.GetTypeName2() == "RefPlane") last = f; } catch { }
                try { f = f.IGetNextFeature(); } catch { break; }
            }
            if (last == null) return null;
            try
            {
                var plane = last.GetSpecificFeature2() as IRefPlane;
                if (plane == null) return null;
                var transformed = plane.Transform as IMathTransform;
                var v = transformed == null ? null : transformed.ArrayData as double[];
                if (v == null || v.Length < 12) return null;
                return new[] { v[9] * 1000, v[10] * 1000, v[11] * 1000 };
            }
            catch { return null; }
        }

        // ==================================================================
        // lettura dello stato
        // ==================================================================
        private static string FaceWithNormal(Dictionary<string, object> docState, string facing)
        {
            var faces = docState["faces"] as System.Collections.IList;
            if (faces == null) return null;
            foreach (Dictionary<string, object> f in faces)
            {
                object v;
                if (f.TryGetValue("facing", out v) && Convert.ToString(v) == facing)
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        private static string FaceNotPlanar(Dictionary<string, object> docState)
        {
            var faces = docState["faces"] as System.Collections.IList;
            if (faces == null) return null;
            foreach (Dictionary<string, object> f in faces)
            {
                object v;
                if (f.TryGetValue("planar", out v) && !Convert.ToBoolean(v))
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        private static string LongestEdge(Dictionary<string, object> docState)
        {
            var edges = docState.ContainsKey("edges") ? docState["edges"] as System.Collections.IList : null;
            if (edges == null || edges.Count == 0) return null;
            // Lo stato li ordina gia' per lunghezza decrescente.
            return Convert.ToString((edges[0] as Dictionary<string, object>)["uid"]);
        }

        // ==================================================================
        // la piastra di prova
        // ==================================================================
        /// <summary>
        /// Una piastra centrata su TUTTI E TRE gli assi, spessore compreso.
        ///
        /// La `Plate` delle prove vecchie estrude da z=0 in avanti: va bene
        /// per un foro sulla faccia grande, ma sulle facce laterali l'origine
        /// dello schizzo cade sullo SPIGOLO, non al centro, e un foro chiesto
        /// a (0,0) uscirebbe meta' fuori dal materiale. Con la condizione
        /// finale «piano medio» il pezzo sta fra -H/2 e +H/2 e l'origine cade
        /// dove uno se l'aspetta.
        /// </summary>
        private static void CenteredPlate(IModelDoc2 doc, double L, double W, double H)
        {
            var planes = ReferencePlanes(doc);
            doc.ClearSelection2(true);
            planes[0].Select2(false, 0);          // frontale: la faccia grande guarda +Z
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(0, 0, 0, L / 2000.0, W / 2000.0, 0);
            sm.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0,
                H / 1000.0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            doc.ClearSelection2(true);
        }

    }
}
