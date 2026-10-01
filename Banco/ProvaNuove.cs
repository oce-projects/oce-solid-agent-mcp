using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ProvaSaldatura
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
    ///   smusso_spigoli  swChamferType_e: e' davvero uno smusso o un raccordo?
    ///   rivoluzione     l'auto-selezione risolve profilo E centerline?
    ///   guscio          «Outward» vuol dire davvero verso l'esterno?
    ///   crea_piano      il segno: valore assoluto + OptionFlip, o segnato?
    ///   crea_asse       l'asse si ritrova poi per nome con SelectByID2?
    ///   ripeti_lineare  il Mark della direzione, e da che parte va
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
        // R2. sopprimi_lavorazione ed elimina_lavorazione: ADESSO SONO FILE
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
        // `centri_x_mm` passa da [-60, 0, 60] a [-60, 0] - e quante
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
        private static void ScattaLaVista()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato0 = LeggiStato(doc);
                string davanti = FacciaConVerso(stato0, "davanti");
                string errore, nomeForo;
                EseguiConNome(doc, "foro_guidato", new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["tipo"] = "filettato",
                    ["taglia"] = "M8",
                    ["passante"] = true,
                    ["posizioni"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = 40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = 40.0 },
                    },
                }, out errore, out nomeForo, stato0);

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
                // uscire da questa macchina (`Controlli\controlla-pubblicabilita.ps1`).
                string cartella = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".",
                    "lavoro", "viste");
                if (!System.IO.Directory.Exists(cartella)) System.IO.Directory.CreateDirectory(cartella);

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
                var viste = new[]
                {
                    Tuple.Create("isometrica", (int)swStandardViews_e.swIsometricView),
                    Tuple.Create("trimetrica", (int)swStandardViews_e.swTrimetricView),
                    Tuple.Create("frontale", (int)swStandardViews_e.swFrontView),
                    Tuple.Create("superiore", (int)swStandardViews_e.swTopView),
                };
                int fatte = 0;
                foreach (var vista in viste)
                {
                    try
                    {
                        doc.ShowNamedView2("", vista.Item2);
                        doc.GraphicsRedraw2();
                        // LO ZOOM VA DOPO IL RIDISEGNO, e va ridisegnato
                        // ancora: al primo giro il pezzo usciva tagliato sul
                        // bordo destro perche' l'adattamento lavorava sulla
                        // vista vecchia.
                        doc.ViewZoomtofit2();
                        doc.GraphicsRedraw2();
                        string nome = vista.Item1;
                        string file = System.IO.Path.Combine(cartella, "piastra_" + nome + ".bmp");
                        if (doc.SaveBMP(file, 1024, 768) && System.IO.File.Exists(file))
                        {
                            fatte++;
                            Console.WriteLine("    " + nome + ": " +
                                new System.IO.FileInfo(file).Length / 1024 + " KB");
                        }
                    }
                    catch (Exception ex) { Console.WriteLine("    " + vista.Item1 + " non riuscita: " + ex.Message); }
                }
                Console.WriteLine();
                Console.WriteLine("=== scatto della vista ===");
                Console.WriteLine("    cartella: " + cartella);
                Segna("scatta la vista del pezzo", fatte > 0,
                      fatte + " viste su " + viste.Length + " salvate in " + cartella);
            }
            catch (Exception ex) { Segna("scatta la vista del pezzo", false, Perche(ex)); }
            finally { Chiudi(doc); }
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
        private static void QuantoVedeIlModello()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato0 = LeggiStato(doc);
                string davanti = FacciaConVerso(stato0, "davanti");
                string errore, nomeForo;
                EseguiConNome(doc, "foro_guidato", new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["tipo"] = "filettato",
                    ["taglia"] = "M8",
                    ["passante"] = true,
                    ["posizioni"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = 40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = 40.0 },
                    },
                }, out errore, out nomeForo, stato0);

                var stato = LeggiStato(doc);
                string json = Json(stato);

                Console.WriteLine();
                Console.WriteLine("=== quanto vede il modello (piastra + 4 fori M8) ===");
                Console.WriteLine("  lo stato pesa " + json.Length + " caratteri");
                foreach (var chiave in stato.Keys.OrderBy(k => k))
                {
                    var v = stato[chiave];
                    var lista = v as System.Collections.IList;
                    Console.WriteLine(string.Format("  {0,-22} {1}",
                        chiave,
                        lista != null ? lista.Count + " voci" : Convert.ToString(v)));
                }

                // LE FEATURE: e' qui che si vede la cecita'. Un progettista che
                // dice «quel foro portalo a M10» sta indicando una feature, e
                // il modello di quella feature deve sapere almeno CHE FORO E'.
                Console.WriteLine();
                Console.WriteLine("  --- cosa dice lo stato di ogni lavorazione ---");
                var feature = stato["feature"] as System.Collections.IList;
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
                var facce = stato["facce"] as System.Collections.IList;
                if (facce != null && facce.Count > 0)
                {
                    var d = facce[0] as Dictionary<string, object>;
                    if (d != null)
                        foreach (var k in d.Keys)
                            Console.WriteLine("    " + k + " = " + Descrivi(d[k]));
                }
                Segna("censimento di cosa vede il modello", true,
                      json.Length + " caratteri, " + (feature == null ? 0 : feature.Count) +
                      " lavorazioni, " + (facce == null ? 0 : facce.Count) + " facce");
            }
            catch (Exception ex) { Segna("censimento di cosa vede il modello", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        private static string Descrivi(object v)
        {
            var lista = v as System.Collections.IEnumerable;
            if (lista != null && !(v is string))
                return "[" + string.Join(",", lista.Cast<object>().Select(o => Convert.ToString(o))) + "]";
            return Convert.ToString(v);
        }

        /// <summary>
        /// Che facce ha davvero la piastra di prova.
        ///
        /// NON E' UNA PROVA, E' UNA DOMANDA. Al primo giro dentro SolidWorks
        /// quindici casi sono morti tutti sulla stessa riga — «nessuna faccia
        /// davanti» — e la causa poteva stare in tre posti diversi: la piastra
        /// costruita su un piano che non e' quello che credevo, lo stato che
        /// non elenca quella faccia, o `Verso` che la chiama in un altro modo.
        /// Stampare l'elenco toglie di mezzo la deduzione.
        /// </summary>
        private static void StampaFaccePiastra()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato = LeggiStato(doc);
                Console.WriteLine();
                Console.WriteLine("=== che facce ha la piastra di prova 200x120x20 ===");
                var facce = stato["facce"] as System.Collections.IList;
                Console.WriteLine("  corpi solidi: " + ContaCorpi(doc) +
                                  ", facce nello stato: " + (facce == null ? 0 : facce.Count));
                if (facce != null)
                    foreach (Dictionary<string, object> f in facce)
                    {
                        object v, n, area;
                        f.TryGetValue("verso", out v);
                        f.TryGetValue("normale", out n);
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
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 00. sposta_corpi — il baricentro dice se la distanza e' quella giusta
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
        private static void ProvaSpostaCorpi()
        {
            Console.WriteLine();
            Console.WriteLine("=== sposta_corpi (add-in) ===");

            // Due piastre uguali, staccate: 100x100x10 = 100.000 mm3 ciascuna.
            // Spostandone UNA di 50 mm in Z, il baricentro di tutto il pezzo si
            // sposta della meta': 25 mm. E' un'uguaglianza, non una stima.
            Sposta("sposta una piastra di 50 in Z", 0, 0, 50, false, 0,
                   corpiAttesi: 2, volumeAttesa: 200000, spostamentoCentroZ: 25);
            // NOTA per chi passa di qui: il 15/9/2026 questo 200000 e' stato
            // messo a 199000 apposta, per vedere `banco.ps1` uscire 1 e
            // nominare il caso con atteso e misurato nel resoconto. E' il
            // criterio di chiusura della voce F2, e va rifatto cosi' se
            // qualcuno tocca il resoconto.
            //
            // E lo stesso giorno, con un errore di sintassi messo qui apposta,
            // si e' visto `banco.ps1` fermarsi PRIMA di lanciare e uscire 2:
            // e' il difetto che la voce F2 nominava per nome — uno script che
            // esce 0 perche' la compilazione e' fallita e nessuno ha guardato.
            Sposta("sposta di 600 in Z", 0, 0, 600, false, 0,
                   corpiAttesi: 2, volumeAttesa: 200000, spostamentoCentroZ: 300);
            Sposta("sposta all'indietro, -120 in Z", 0, 0, -120, false, 0,
                   corpiAttesi: 2, volumeAttesa: 200000, spostamentoCentroZ: -60);

            // Copia: gli originali restano, e ne compare una in piu'.
            // Tre corpi da 100.000, e la copia sta a +600.
            // baricentroZ = (100000*0 + 100000*0 + 100000*600) / 300000 = 200.
            Sposta("copia una piastra a +600 in Z (il castello)", 0, 0, 600, true, 1,
                   corpiAttesi: 3, volumeAttesa: 300000, spostamentoCentroZ: 200);
            // Due copie: +600 e +1200.
            // baricentroZ = (0 + 0 + 600 + 1200) / 4 = 450.
            Sposta("due copie, a +600 e +1200", 0, 0, 600, true, 2,
                   corpiAttesi: 4, volumeAttesa: 400000, spostamentoCentroZ: 450);

            // Spostamento nullo: l'add-in deve rifiutarlo da solo, senza
            // aspettare il validatore del server.
            Sposta("spostamento nullo (deve fallire)", 0, 0, 0, false, 0,
                   corpiAttesi: 0, volumeAttesa: 0, spostamentoCentroZ: 0,
                   deveFallire: true);
            Sposta("copia con spostamento nullo (deve fallire)", 0, 0, 0, true, 1,
                   corpiAttesi: 0, volumeAttesa: 0, spostamentoCentroZ: 0,
                   deveFallire: true);
            // Un corpo che non c'e'.
            Sposta("corpo inesistente (deve fallire)", 0, 0, 50, false, 0,
                   corpiAttesi: 0, volumeAttesa: 0, spostamentoCentroZ: 0,
                   deveFallire: true, uidFinto: "C99");
        }

        private static void Sposta(string nome, double dx, double dy, double dz,
                                   bool copia, int copie,
                                   int corpiAttesi, double volumeAttesa,
                                   double spostamentoCentroZ,
                                   bool deveFallire = false, string uidFinto = null)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                // Due corpi separati: il secondo NON si fonde col primo.
                PiastraDove(doc, 100, 100, 10, 0, 0, fondi: true);
                PiastraDove(doc, 100, 100, 10, 200, 0, fondi: false);
                if (ContaCorpi(doc) != 2)
                {
                    Segna(nome, false, "il pezzo di partenza ha " + ContaCorpi(doc) + " corpi invece di 2");
                    return;
                }

                var stato = LeggiStato(doc);
                var uid = CorpiUid(stato);
                if (uid.Count < 2) { Segna(nome, false, "lo stato elenca " + uid.Count + " corpi"); return; }

                double centroPrima = CentroZDelDocumento(doc);
                var argomenti = new Dictionary<string, object>
                {
                    // Si muove UN corpo solo: e' cio' che rende il baricentro
                    // una misura sensibile. Muovendoli tutti, il baricentro si
                    // sposterebbe esattamente di d e non distinguerebbe un
                    // corpo dimenticato indietro.
                    ["corpi_uid"] = new List<string> { uidFinto ?? uid[1] },
                    ["dx_mm"] = dx,
                    ["dy_mm"] = dy,
                    ["dz_mm"] = dz,
                    ["copia"] = copia,
                };
                if (copia) argomenti["quante_copie"] = copie;

                string errore;
                var misure = Esegui(doc, "sposta_corpi", argomenti, out errore, stato);
                if (deveFallire)
                {
                    Segna(nome, misure == null && !string.IsNullOrEmpty(errore),
                          misure == null ? "rifiutato: " + Corto(errore) : "NON ha rifiutato");
                    return;
                }
                if (misure == null) { Segna(nome, false, "fallita: " + Corto(errore)); return; }

                int corpi = ContaCorpi(doc);
                double volume = VolumeDelDocumento(doc);
                double centroDopo = CentroZDelDocumento(doc);
                double spostato = centroDopo - centroPrima;

                bool ok = corpi == corpiAttesi
                          && Math.Abs(volume - volumeAttesa) < Math.Max(2, volumeAttesa * 0.005)
                          && Math.Abs(spostato - spostamentoCentroZ) < 0.05;
                Segna(nome, ok, string.Format(
                    "{0} corpi (attesi {1}), volume {2:0.#} (atteso {3:0.#}), " +
                    "baricentro Z spostato di {4:0.###} mm (atteso {5:0.###})",
                    corpi, corpiAttesi, volume, volumeAttesa, spostato, spostamentoCentroZ));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        /// <summary>Gli identificativi dei corpi, come li elenca lo stato.</summary>
        private static List<string> CorpiUid(Dictionary<string, object> stato)
        {
            var fuori = new List<string>();
            object grezzi;
            if (stato == null || !stato.TryGetValue("corpi", out grezzi)) return fuori;
            var lista = grezzi as System.Collections.IEnumerable;
            if (lista == null) return fuori;
            foreach (var c in lista)
            {
                var d = c as Dictionary<string, object>;
                object u;
                if (d != null && d.TryGetValue("uid", out u) && u != null) fuori.Add(u.ToString());
            }
            return fuori;
        }

        /// <summary>
        /// Il baricentro del pezzo lungo Z, in mm. E' la misura con cui il
        /// banco controlla lo spostamento per conto proprio, senza fidarsi di
        /// quello che l'operazione dichiara di aver fatto.
        /// </summary>
        private static double CentroZDelDocumento(IModelDoc2 doc)
        {
            var parte = doc as IPartDoc;
            if (parte == null) return 0;
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null || corpi.Length == 0) return 0;
            double somma = 0, volume = 0;
            foreach (var c in corpi)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) continue;
                double v = mp[3] * 1e9;
                if (v <= 0) continue;
                volume += v;
                somma += mp[2] * 1000.0 * v;
            }
            return volume > 0 ? somma / volume : 0;
        }

        /// <summary>
        /// Una piastra centrata in (x, y). Con `fondi` a falso resta un corpo
        /// SEPARATO invece di fondersi con quello che c'e' gia': serve per
        /// avere piu' corpi da spostare.
        /// </summary>
        private static void PiastraDove(IModelDoc2 doc, double L, double W, double H,
                                        double x, double y, bool fondi)
        {
            var piani = PianiDiRiferimento(doc);
            doc.ClearSelection2(true);
            piani[0].Select2(false, 0);
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
                fondi, true, true, 0, 0, false);
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
        // `volumeAtteso * 0.02` — sui 240.000 mm³ della piastra sono **4.800
        // mm³, piu' di un foro M12 passante**. Una prova che accetta un foro di
        // scarto non prova il foro, e tenerla in giro come rete di sicurezza
        // sarebbe tenere in giro proprio il difetto che F3 chiude.
        // ==================================================================
        // 4. crea_piano ADESSO E' UN GRUPPO DI FILE
        // ==================================================================
        //
        // `cases\plane\*.json` (voce F11) - tre casi. L'attesa non e' sulla
        // distanza che il ponte ricopia dalla richiesta, ma su DOVE E' FINITO
        // il piano: `ultimo_piano_mm`, tre numeri letti dalla matrice del
        // piano creato. Il segno si vede solo cosi'. `--gruppo casi`.

        // 5. crea_asse — e la domanda vera: si ritrova poi per nome?
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
        // quella riga non aggancia, `crea_asse` riesce, lo stato mostra
        // l'asse, e la ripetizione fallisce subito dopo — cioe' l'operazione
        // e' inutile e niente lo dice.
        //
        // Un caso dichiarato non puo' provarlo: `SelectByID2` non e'
        // un'operazione del catalogo, e' una chiamata COM. Resta in C#, e
        // resta scritto il perche'.
        private static void ProvaCreaAsse()
        {
            Console.WriteLine();
            Console.WriteLine("=== crea_asse: l'asse si ritrova per nome? (add-in) ===");
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 120, 120, 15);
                var stato = LeggiStato(doc);
                string facciaPiana = FacciaConVerso(stato, "davanti");
                if (facciaPiana == null) { Segna("crea_asse: faccia di partenza", false, "assente"); return; }

                string errore, nomeForo;
                var foro = EseguiConNome(doc, "foro_guidato", new Dictionary<string, object>
                {
                    ["faccia_uid"] = facciaPiana,
                    ["tipo"] = "passaggio",
                    ["taglia"] = "M10",
                    ["passante"] = true,
                    ["posizioni"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = 0.0, ["y_mm"] = 0.0 },
                    },
                }, out errore, out nomeForo, stato);
                if (foro == null) { Segna("crea_asse: foro di prova", false, Corto(errore)); return; }

                stato = LeggiStato(doc);
                string cilindrica = FacciaNonPiana(stato);
                if (cilindrica == null)
                {
                    Segna("crea_asse: la parete del foro compare nello stato", false,
                          "nessuna faccia con «piana: false»: il modello non avrebbe " +
                          "niente da passare a crea_asse");
                    return;
                }

                string nomeAsse;
                var misure = EseguiConNome(doc, "crea_asse", new Dictionary<string, object>
                {
                    ["faccia_uid"] = cilindrica,
                }, out errore, out nomeAsse, stato);
                if (misure == null) { Segna("crea_asse: l'asse per il gancio", false, Corto(errore)); return; }
                string asse = misure.ContainsKey("asse") ? Convert.ToString(misure["asse"]) : "";
                if (string.IsNullOrEmpty(asse))
                {
                    // NON E' IL ROSSO DI QUESTA PROVA: senza il nome non c'e'
                    // niente da agganciare, e dirlo e' meglio che agganciare
                    // la stringa vuota e chiamarlo fallimento del gancio.
                    Segna("crea_asse: l'operazione dice come si chiama l'asse", false,
                          "le misure non hanno «asse»");
                    return;
                }

                // LA PROVA CHE CONTA: «ripeti_lineare» e «ripeti_circolare»
                // ritrovano l'asse con SelectByID2(nome, "AXIS", ...). Se
                // quella riga non aggancia, crea_asse riesce, lo stato mostra
                // l'asse, e la ripetizione fallisce subito dopo — cioe'
                // l'operazione e' inutile e niente lo dice.
                bool agganciato = false;
                try
                {
                    doc.ClearSelection2(true);
                    agganciato = doc.Extension.SelectByID2(asse, "AXIS", 0, 0, 0, false, 1, null, 0);
                }
                catch { }
                Segna("crea_asse: l'asse si aggancia con SelectByID2(\"AXIS\")", agganciato,
                      agganciato ? "asse «" + asse + "»" : "la ripetizione non lo trovera' mai");
            }
            catch (Exception ex) { Segna("crea_asse", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }
        // ==================================================================
        // 6. ripeti_lineare e 7. specchia ADESSO SONO DUE GRUPPI DI FILE
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
        private static Dictionary<string, object> EseguiConNome(
            IModelDoc2 doc, string operazione, Dictionary<string, object> argomenti,
            out string errore, out string featureCreata, object stato = null)
        {
            errore = null;
            featureCreata = null;
            var costruttore = _tOperazioni.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
            var op = costruttore.Invoke(new object[] { _sw, (Action<string>)(s => { }) });
            object statoOggetto = (stato == null || stato is Dictionary<string, object>)
                ? LeggiStatoOggetto(doc) : stato;

            var esegui = _tOperazioni.GetMethod("Esegui",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var esito = esegui.Invoke(op, new object[] { doc, operazione, argomenti, statoOggetto, null });
            if (!(bool)_tEsito.GetField("Riuscito").GetValue(esito))
            {
                errore = _tEsito.GetField("Errore").GetValue(esito) as string;
                return null;
            }
            featureCreata = _tEsito.GetField("FeatureCreata").GetValue(esito) as string;
            try { doc.EditRebuild3(); } catch { }
            return _tEsito.GetField("Misure").GetValue(esito) as Dictionary<string, object>;
        }

        // ==================================================================
        // misure sul pezzo — sempre dalla geometria, mai dai parametri
        // ==================================================================

        /// <summary>
        /// Il volume solido in mm3. `GetMassProperties(densita)[3]` e' il
        /// volume in m3: misurato, non ricordato.
        /// </summary>
        private static double VolumeDelDocumento(IModelDoc2 doc)
        {
            double v = 0;
            // Una TAVOLA non ha corpi. Da quando la sessione puo' tenere
            // aperti due documenti, il documento attivo puo' benissimo
            // essere il disegno, e qui si arrivava con un IPartDoc nullo.
            var parte = doc as IPartDoc;
            if (parte == null) return 0;
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null) return 0;
            foreach (var c in corpi)
            {
                var mp = (c as IBody2).GetMassProperties(1000.0) as double[];
                if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
            }
            return v;
        }

        private static int ContaCorpi(IModelDoc2 doc)
        {
            var parte = doc as IPartDoc;
            if (parte == null) return 0;
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            return corpi == null ? 0 : corpi.Length;
        }

        private static double[] IngombroDelDocumento(IModelDoc2 doc)
        {
            var b = ScatolaDelDocumento(doc);
            if (b == null) return null;
            return new[] { b[3] - b[0], b[4] - b[1], b[5] - b[2] };
        }

        private static int ContaPiani(IModelDoc2 doc)
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
        private static double[] TraslazioneUltimoPiano(IModelDoc2 doc)
        {
            IFeature ultimo = null;
            var f = (doc as IPartDoc).IFirstFeature();
            while (f != null)
            {
                try { if (f.GetTypeName2() == "RefPlane") ultimo = f; } catch { }
                try { f = f.IGetNextFeature(); } catch { break; }
            }
            if (ultimo == null) return null;
            try
            {
                var piano = ultimo.GetSpecificFeature2() as IRefPlane;
                if (piano == null) return null;
                var trasformata = piano.Transform as IMathTransform;
                var v = trasformata == null ? null : trasformata.ArrayData as double[];
                if (v == null || v.Length < 12) return null;
                return new[] { v[9] * 1000, v[10] * 1000, v[11] * 1000 };
            }
            catch { return null; }
        }

        // ==================================================================
        // lettura dello stato
        // ==================================================================
        private static string FacciaConVerso(Dictionary<string, object> stato, string verso)
        {
            var facce = stato["facce"] as System.Collections.IList;
            if (facce == null) return null;
            foreach (Dictionary<string, object> f in facce)
            {
                object v;
                if (f.TryGetValue("verso", out v) && Convert.ToString(v) == verso)
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        private static string FacciaNonPiana(Dictionary<string, object> stato)
        {
            var facce = stato["facce"] as System.Collections.IList;
            if (facce == null) return null;
            foreach (Dictionary<string, object> f in facce)
            {
                object v;
                if (f.TryGetValue("piana", out v) && !Convert.ToBoolean(v))
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        private static string SpigoloPiuLungo(Dictionary<string, object> stato)
        {
            var spigoli = stato.ContainsKey("spigoli") ? stato["spigoli"] as System.Collections.IList : null;
            if (spigoli == null || spigoli.Count == 0) return null;
            // Lo stato li ordina gia' per lunghezza decrescente.
            return Convert.ToString((spigoli[0] as Dictionary<string, object>)["uid"]);
        }

        // ==================================================================
        // la piastra di prova
        // ==================================================================
        /// <summary>
        /// Una piastra centrata su TUTTI E TRE gli assi, spessore compreso.
        ///
        /// La `Piastra` delle prove vecchie estrude da z=0 in avanti: va bene
        /// per un foro sulla faccia grande, ma sulle facce laterali l'origine
        /// dello schizzo cade sullo SPIGOLO, non al centro, e un foro chiesto
        /// a (0,0) uscirebbe meta' fuori dal materiale. Con la condizione
        /// finale «piano medio» il pezzo sta fra -H/2 e +H/2 e l'origine cade
        /// dove uno se l'aspetta.
        /// </summary>
        private static void PiastraCentrata(IModelDoc2 doc, double L, double W, double H)
        {
            var piani = PianiDiRiferimento(doc);
            doc.ClearSelection2(true);
            piani[0].Select2(false, 0);          // frontale: la faccia grande guarda +Z
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
