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
        // V3/V7. la forma detta in una frase, e quando NON si dice
        // ==================================================================
        /// <summary>
        /// La frase che descrive il pezzo, e soprattutto **quando tace**.
        ///
        /// ── IL PROFILO DI UN TORNITO SI RICAVA DALL'AREA ──────────────────
        ///
        /// La lunghezza di un tratto non sta da nessuna parte nello stato, ma
        /// un cilindro di diametro d e lunghezza L ha area **π·d·L**. Su un
        /// albero pulito il conto torna al decimo.
        ///
        /// **E su un albero forato no.** Un foro assiale aggiunge una faccia
        /// cilindrica INTERNA: la formula la tratta come un tratto in piu', la
        /// somma delle lunghezze sfora l'ingombro e il volume ricostruito conta
        /// materiale che e' stato tolto. Il profilo che ne uscirebbe e'
        /// plausibile e falso — la corona di bulloni inventata di V3, con
        /// un'altra formula.
        ///
        /// Per questo `ProfiloTornito` ricostruisce e confronta, e **tace se
        /// non torna**. Questa prova e' il rosso di quel controllo: senza,
        /// «il profilo esce giusto sull'albero pulito» passerebbe anche se il
        /// controllo non ci fosse.
        /// </summary>
        private static void ProvaFormaAParole()
        {
            Console.WriteLine();
            Console.WriteLine("=== la forma in una frase (V3/V7) ===");

            var tipo = _addin.GetType("Riscontro.Disegno.FormaAParole", false);
            if (tipo == null) { Segna("forma: la classe c'e'", false, "FormaAParole assente"); return; }

            // ── IL VERDE: un albero pulito ──────────────────────────────
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                string errore;
                var m = Esegui(doc, "rivoluzione", AlberoAGradini(), out errore);
                if (m == null) { Segna("forma: l'albero", false, Corto(errore)); return; }

                string frase = Convert.ToString(FraseDi(doc));
                Segna("forma: l'albero pulito dichiara il profilo",
                      frase != null && frase.Contains("profilo") &&
                      frase.Contains("Ø30 per 40") &&
                      frase.Contains("Ø20 per 30") &&
                      frase.Contains("Ø34 per 20"),
                      Corto(frase));
            }
            catch (Exception ex) { Segna("forma: albero pulito", false, Perche(ex)); }
            finally { Chiudi(doc); doc = null; }

            // ── IL ROSSO: lo stesso albero, ma forato ───────────────────
            try
            {
                doc = NuovaParte();
                string errore;
                var m = Esegui(doc, "rivoluzione", AlberoAGradini(), out errore);
                if (m == null) { Segna("forma: l'albero da forare", false, Corto(errore)); return; }

                // Un foro assiale sulla faccia piatta di testa: la faccia
                // cilindrica interna che ne nasce e' cio' che manda in vacca la
                // formula dell'area.
                var stato = LeggiStato(doc);
                string testa = FacciaPiattaPiuGrande(stato);
                if (testa == null)
                {
                    Saltata("forma ROSSO: l'albero forato tace",
                            "non ho trovato la faccia di testa su cui forare");
                }
                else
                {
                    var f = Esegui(doc, "taglio_cilindrico", new Dictionary<string, object>
                    {
                        ["faccia_uid"] = testa,
                        ["x_mm"] = 0.0,
                        ["y_mm"] = 0.0,
                        ["diametro_mm"] = 10.0,
                        ["passante"] = true,
                    }, out errore, stato);
                    if (f == null)
                    {
                        Saltata("forma ROSSO: l'albero forato tace",
                                "il foro non e' venuto: " + Corto(errore));
                    }
                    else
                    {
                        string frase = Convert.ToString(FraseDi(doc));
                        Segna("forma ROSSO: sull'albero forato NON dichiara il profilo",
                              frase != null && frase.Contains("rivoluzione") &&
                              !frase.Contains("profilo"),
                              Corto(frase));
                    }
                }
            }
            catch (Exception ex) { Segna("forma: albero forato", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        /// <summary>Il profilo dell'albero a gradini Ø30/Ø20/Ø34.</summary>
        private static Dictionary<string, object> AlberoAGradini()
        {
            return new Dictionary<string, object>
            {
                ["piano"] = "frontale",
                ["angolo_gradi"] = 360.0,
                ["asse"] = new object[]
                {
                    new double[] { 0, 0 }, new double[] { 90, 0 },
                },
                ["contorni"] = new object[]
                {
                    new object[]
                    {
                        new double[] { 0, 0 }, new double[] { 0, 15 },
                        new double[] { 40, 15 }, new double[] { 40, 10 },
                        new double[] { 70, 10 }, new double[] { 70, 17 },
                        new double[] { 90, 17 }, new double[] { 90, 0 },
                    },
                },
            };
        }

        /// <summary>La frase `forma` dello stato, o null.</summary>
        private static object FraseDi(IModelDoc2 doc)
        {
            var stato = LeggiStato(doc);
            object v;
            return stato != null && stato.TryGetValue("forma", out v) ? v : null;
        }

        private static string FacciaPiattaPiuGrande(Dictionary<string, object> stato)
        {
            string migliore = null;
            double area = 0;
            object g;
            if (stato == null || !stato.TryGetValue("facce", out g) || g == null) return null;
            var elenco = g as System.Collections.IEnumerable;
            if (elenco == null) return null;
            foreach (var v in elenco)
            {
                var voce = v as Dictionary<string, object>;
                object piana, a, uid;
                if (voce == null) continue;
                if (!voce.TryGetValue("piana", out piana) || !Convert.ToBoolean(piana)) continue;
                if (!voce.TryGetValue("area_mm2", out a)) continue;
                double quanta = Convert.ToDouble(a);
                if (quanta <= area) continue;
                if (!voce.TryGetValue("uid", out uid) || uid == null) continue;
                area = quanta;
                migliore = Convert.ToString(uid);
            }
            return migliore;
        }

        // ==================================================================
        // R2. sopprimi_lavorazione ed elimina_lavorazione
        // ==================================================================
        /// <summary>
        /// Togliere una lavorazione: spegnerla, riaccenderla, cancellarla.
        ///
        /// IL CRITERIO DELLA VOCE, alla lettera: «su un pezzo con tre fori,
        /// sopprimere il secondo riporta il volume al valore che aveva con due,
        /// e lo stato lo mostra `soppressa: true`. Eliminarlo lo toglie
        /// dall'elenco.»
        ///
        /// E' un'uguaglianza, non una stima: il volume con due fori si MISURA
        /// prima di fare il terzo, e dopo la soppressione dev'essere quello.
        ///
        /// ── I ROSSI, CHE SONO LA PARTE CHE CONTA ──────────────────────────
        ///
        /// Togliere una lavorazione riesce sempre: SOLIDWORKS la toglie e
        /// basta. La voce esiste per i casi in cui **non si deve**:
        ///
        ///   1. una lavorazione da cui ne dipendono altre -> rifiuta, e DICE
        ///      quali. Senza, sparirebbero anche quelle, senza chiedere.
        ///   2. un nome che non c'e' -> rifiuta, e dice quali ci sono.
        ///   3. sopprimere cio' che e' gia' soppresso -> lo dice invece di
        ///      rispondere «fatto», che non distinguerebbe piu' un
        ///      cambiamento da un nulla.
        /// </summary>
        private static void ProvaTogliere()
        {
            Console.WriteLine();
            Console.WriteLine("=== sopprimi/elimina lavorazione (R2) ===");

            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();

                string errore;
                var misure = Esegui(doc, "estrudi_rettangolo", new Dictionary<string, object>
                {
                    ["piano"] = "superiore",
                    ["lunghezza_mm"] = 200.0,
                    ["larghezza_mm"] = 100.0,
                    ["spessore_mm"] = 20.0,
                    ["centrato"] = true,
                }, out errore);
                if (misure == null) { Segna("R2: la piastra", false, Corto(errore)); return; }
                string estrusioneDiBase = UltimoNomeFeature(doc);

                // Tre fori, uno per volta, misurando il volume dopo il secondo:
                // e' quello il numero che la soppressione deve far tornare.
                string[] fori = new string[3];
                double volumeConDue = 0;
                for (int i = 0; i < 3; i++)
                {
                    var stato = LeggiStato(doc);
                    string alto = FacciaInAlto(stato);
                    if (alto == null) { Segna("R2: faccia in alto", false, "non trovata"); return; }
                    var m = Esegui(doc, "taglio_cilindrico", new Dictionary<string, object>
                    {
                        ["faccia_uid"] = alto,
                        ["x_mm"] = -60.0 + i * 60.0,
                        ["y_mm"] = 0.0,
                        ["diametro_mm"] = 14.0,
                        ["passante"] = true,
                    }, out errore, stato);
                    if (m == null) { Segna("R2: foro " + (i + 1), false, Corto(errore)); return; }
                    fori[i] = UltimoNomeFeature(doc);
                    if (i == 1) volumeConDue = VolumeDelDocumento(doc);
                }
                double volumeConTre = VolumeDelDocumento(doc);
                Segna("R2: tre fori, e il terzo toglie materiale",
                      volumeConTre < volumeConDue - 1,
                      "con due " + volumeConDue.ToString("0.#") +
                      ", con tre " + volumeConTre.ToString("0.#"));

                // ── ROSSO 2: un nome che non c'e' ───────────────────────
                var nulla = Esegui(doc, "elimina_lavorazione", new Dictionary<string, object>
                {
                    ["feature_uid"] = "Taglio-Che-Non-Esiste",
                }, out errore);
                Segna("R2 ROSSO: un nome inventato e' rifiutato, coi nomi veri",
                      nulla == null && errore != null && errore.Contains("Ci sono"),
                      nulla != null ? "NON ha rifiutato" : Corto(errore));

                // ── IL VERDE: sopprimere il secondo ─────────────────────
                var s = Esegui(doc, "sopprimi_lavorazione", new Dictionary<string, object>
                {
                    ["feature_uid"] = fori[2],
                }, out errore);
                Segna("R2: sopprimere il terzo foro riesce", s != null, Corto(errore));
                double volumeSoppresso = VolumeDelDocumento(doc);
                Segna("R2: il volume torna a quello con due fori",
                      Math.Abs(volumeSoppresso - volumeConDue) < 0.01,
                      volumeSoppresso.ToString("0.###") + " contro " +
                      volumeConDue.ToString("0.###"));
                Segna("R2: lo stato la mostra soppressa",
                      Soppressa(LeggiStato(doc), fori[2]),
                      "cerco «soppressa: true» su " + fori[2]);

                // ── ROSSO 3: sopprimerla di nuovo ───────────────────────
                var ancora = Esegui(doc, "sopprimi_lavorazione", new Dictionary<string, object>
                {
                    ["feature_uid"] = fori[2],
                }, out errore);
                Segna("R2 ROSSO: sopprimere cio' che e' gia' spento e' rifiutato",
                      ancora == null && errore != null && errore.Contains("gia'"),
                      ancora != null ? "NON ha rifiutato" : Corto(errore));

                // ── E si riaccende ──────────────────────────────────────
                var r = Esegui(doc, "sopprimi_lavorazione", new Dictionary<string, object>
                {
                    ["feature_uid"] = fori[2],
                    ["riattiva"] = true,
                }, out errore);
                Segna("R2: riattivarla rimette il volume com'era",
                      r != null && Math.Abs(VolumeDelDocumento(doc) - volumeConTre) < 0.01,
                      r == null ? Corto(errore) : VolumeDelDocumento(doc).ToString("0.###"));

                // ── ROSSO 1: L'ESTRUSIONE DI BASE ───────────────────────
                //
                // E' il caso che la voce chiama pericoloso per nome: «togliere
                // l'estrusione di base fa crollare tutto quello che ci sta
                // sopra, e SOLIDWORKS lo fa senza chiedere». I tre fori sono
                // suoi figli, quindi cancellarla li porterebbe via — e con
                // loro il pezzo.
                //
                // La prima scrittura di questo rosso costruiva un raccordo sul
                // terzo foro per dargli un figlio, e il raccordo non e' venuto:
                // la prova e' finita SALTATA, cioe' il controllo che questa
                // voce esiste per fare non e' mai scattato. Un rosso saltato
                // non e' un rosso passato — il banco lo dice apposta.
                var conFigli = Esegui(doc, "elimina_lavorazione", new Dictionary<string, object>
                {
                    ["feature_uid"] = estrusioneDiBase,
                }, out errore);
                // SI CERCA IL NOME DI UN FIGLIO, non la parola «dipendono».
                // La prima scrittura cercava «dipende», e correggendo la frase
                // in «da lei dipendono» la prova sarebbe diventata rossa su un
                // codice giusto: un'asserzione sul FRASEGGIO si rompe ogni
                // volta che si sistema una virgola.
                //
                // Quello che la voce chiede e' che il rifiuto DICA QUALI sono i
                // figli. Allora e' quello che si cerca: il nome del primo foro,
                // che dell'estrusione di base e' figlio.
                Segna("R2 ROSSO: l'estrusione di base e' rifiutata, coi nomi dei figli",
                      conFigli == null && errore != null && errore.Contains(fori[0]),
                      conFigli != null ? "NON ha rifiutato" : Corto(errore));

                // ── IL VERDE: eliminare il PRIMO, che figli non ne ha ───
                double volumePrimaDellaCancellazione = VolumeDelDocumento(doc);
                var e = Esegui(doc, "elimina_lavorazione", new Dictionary<string, object>
                {
                    ["feature_uid"] = fori[0],
                }, out errore);
                Segna("R2: eliminare un foro senza figli riesce", e != null, Corto(errore));
                Segna("R2: non e' piu' nell'elenco delle lavorazioni",
                      !NellElenco(LeggiStato(doc), fori[0]),
                      "cerco «" + fori[0] + "» fra le feature");
                Segna("R2: il volume e' cresciuto di un foro",
                      VolumeDelDocumento(doc) > volumePrimaDellaCancellazione + 1,
                      volumePrimaDellaCancellazione.ToString("0.#") + " -> " +
                      VolumeDelDocumento(doc).ToString("0.#"));
            }
            catch (Exception ex) { Segna("R2", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        /// <summary>Il nome dell'ultima lavorazione dell'albero.</summary>
        private static string UltimoNomeFeature(IModelDoc2 doc)
        {
            string ultimo = null;
            try
            {
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null) { try { ultimo = f.Name; } catch { } f = f.IGetNextFeature(); }
            }
            catch { }
            return ultimo;
        }

        private static bool Soppressa(Dictionary<string, object> stato, string uid)
        {
            foreach (var v in Feature(stato))
            {
                object u, s;
                if (!v.TryGetValue("uid", out u) || Convert.ToString(u) != uid) continue;
                return v.TryGetValue("soppressa", out s) && Convert.ToBoolean(s);
            }
            return false;
        }

        private static bool NellElenco(Dictionary<string, object> stato, string uid)
        {
            foreach (var v in Feature(stato))
            {
                object u;
                if (v.TryGetValue("uid", out u) && Convert.ToString(u) == uid) return true;
            }
            return false;
        }

        private static IEnumerable<Dictionary<string, object>> Feature(
            Dictionary<string, object> stato)
        {
            object g;
            if (stato == null || !stato.TryGetValue("feature", out g) || g == null) yield break;
            var elenco = g as System.Collections.IEnumerable;
            if (elenco == null) yield break;
            foreach (var v in elenco)
            {
                var voce = v as Dictionary<string, object>;
                if (voce != null) yield return voce;
            }
        }


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

        // ==================================================================
        // 0. asola — il volume di uno stadio si calcola a mano
        // ==================================================================
        private static void ProvaAsola()
        {
            Console.WriteLine();
            Console.WriteLine("=== asola (add-in) ===");

            // Un'asola per M10 che scorre 40 mm: larga 11, lunga 51.
            // Area = (51-11)*11 + pi*5,5^2 = 440 + 95,03 = 535,03 mm2.
            // IL DUBBIO E' IL VERSO DEGLI ARCHI. Se «Direction» e' sbagliato
            // l'arco gira dalla parte lunga — 270 gradi invece di 90 — e il
            // contorno si autointerseca: il taglio riesce lo stesso ma toglie
            // una forma diversa. Il volume lo dice al primo confronto.
            double area = (51.0 - 11.0) * 11.0 + Math.PI * 5.5 * 5.5;

            Asola("asola M10 passante, 51x11", 51, 11, 0, 0, 0, true, 0, area * 20);
            Asola("asola cieca profonda 5", 51, 11, 0, 0, 0, false, 5, area * 5);
            Asola("asola ruotata di 90 gradi", 51, 11, 0, 0, 90, true, 0, area * 20);
            Asola("asola ruotata di 30 gradi", 51, 11, 0, 0, 30, true, 0, area * 20);
            // Fuori centro: la posizione non deve cambiare il volume.
            Asola("asola fuori centro", 51, 11, 60, 30, 0, true, 0, area * 20);

            // OGNI FACCIA, NON UNA SOLA. Il verso degli archi e' una costante
            // nel piano dello SCHIZZO, mentre i punti sono costruiti nel
            // sistema del MODELLO: la trasformazione fra i due non conserva la
            // mano, e per due facce opposte l'asse Y risulta invertito. Provare
            // solo «davanti» copre meta' dei casi e non lo dice.
            //
            // Se il verso e' sbagliato su una faccia, l'asola esce lunga L-W
            // (40 invece di 51) con le estremita' MORSE: area 344,97 invece di
            // 535,03. Su una piastra 200x120x20 il volume lo dice subito.
            foreach (var verso in new[] { "dietro", "alto", "basso", "destra", "sinistra" })
            {
                // Sulle facce di costa la piastra e' spessa 20 o 120: l'asola
                // ci sta comunque, ma il materiale sotto cambia. Si prova
                // CIECA, dove il confronto e' un'uguaglianza esatta e non un
                // tetto.
                Asola("asola cieca sulla faccia «" + verso + "»", 51, 11, 0, 0, 0,
                      false, 5, area * 5, verso: verso);
            }

            // LA ROTAZIONE VA VISTA, E IL VOLUME NON LA VEDE: girare un'asola
            // non cambia quanto materiale toglie. Per renderla osservabile si
            // usa una piastra STRETTA, dove l'asola ci sta per il lungo e non
            // per il largo.
            //
            // Piastra 200x40x20: a 0 gradi l'asola 51x11 sta tutta dentro
            // (x da -25,5 a +25,5, y da -5,5 a +5,5) e toglie area*20. A 90
            // gradi sporgerebbe di 5,5 mm da tutte e due le coste (y da -25,5
            // a +25,5 contro una semialtezza di 20), quindi toglie MENO.
            // Se la rotazione non venisse applicata, i due casi darebbero lo
            // stesso numero — ed e' esattamente quello che questa prova
            // impedisce.
            AsolaSuStretta("rotazione: 0 gradi su piastra stretta", 0, area * 20, uguale: true);
            AsolaSuStretta("rotazione: 90 gradi su piastra stretta", 90, area * 20, uguale: false);

            // Lunga quanto larga non e' un'asola, e' un foro tondo.
            Asola("asola 20x20 (deve fallire)", 20, 20, 0, 0, 0, true, 0, 0, deveFallire: true);
            // Cieca senza profondita'.
            Asola("asola cieca senza profondita' (deve fallire)", 51, 11, 0, 0, 0, false, 0, 0,
                  deveFallire: true);
            // Tutta fuori dal pezzo: il taglio non toglie niente, e va detto.
            Asola("asola fuori dal pezzo (deve fallire)", 51, 11, 400, 0, 0, true, 0, 0,
                  deveFallire: true);
        }

        /// <summary>
        /// La stessa asola su una piastra STRETTA, dove l'orientamento cambia
        /// il risultato. Serve a rendere osservabile la rotazione, che il
        /// volume da solo non vede.
        /// </summary>
        private static void AsolaSuStretta(string nome, double angolo,
                                           double volumePieno, bool uguale)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 40, 20);
                var stato = LeggiStato(doc);
                string davanti = FacciaConVerso(stato, "davanti");
                if (davanti == null) { Segna(nome, false, "nessuna faccia «davanti»"); return; }

                double v0 = VolumeDelDocumento(doc);
                string errore;
                var misure = Esegui(doc, "asola", new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["x_mm"] = 0.0,
                    ["y_mm"] = 0.0,
                    ["lunghezza_mm"] = 51.0,
                    ["larghezza_mm"] = 11.0,
                    ["angolo_gradi"] = angolo,
                    ["passante"] = true,
                }, out errore, stato);
                if (misure == null) { Segna(nome, false, "fallita: " + Corto(errore)); return; }

                double tolto = v0 - VolumeDelDocumento(doc);
                bool ok = uguale
                    ? Math.Abs(tolto - volumePieno) < Math.Max(2, volumePieno * 0.02)
                    : tolto < volumePieno * 0.95;
                Segna(nome, ok, string.Format(
                    "tolti {0:0.#} mm3, l'asola intera ne toglierebbe {1:0.#} ({2})",
                    tolto, volumePieno,
                    uguale ? "devono coincidere" : "devono essere meno: sporge dalle coste"));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        private static void Asola(string nome, double lunghezza, double larghezza,
                                  double x, double y, double angolo, bool passante,
                                  double profondita, double volumeAtteso,
                                  bool deveFallire = false, string verso = "davanti")
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato = LeggiStato(doc);
                string davanti = FacciaConVerso(stato, verso);
                if (davanti == null) { Segna(nome, false, "nessuna faccia «" + verso + "»"); return; }

                double v0 = VolumeDelDocumento(doc);
                var argomenti = new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["x_mm"] = x,
                    ["y_mm"] = y,
                    ["lunghezza_mm"] = lunghezza,
                    ["larghezza_mm"] = larghezza,
                    ["angolo_gradi"] = angolo,
                    ["passante"] = passante,
                };
                if (!passante) argomenti["profondita_mm"] = profondita;

                string errore;
                var misure = Esegui(doc, "asola", argomenti, out errore, stato);
                if (deveFallire)
                {
                    Segna(nome, misure == null && !string.IsNullOrEmpty(errore),
                          misure == null ? "rifiutato: " + Corto(errore) : "NON ha rifiutato");
                    return;
                }
                if (misure == null) { Segna(nome, false, "fallita: " + Corto(errore)); return; }

                double tolto = v0 - VolumeDelDocumento(doc);
                Segna(nome, Math.Abs(tolto - volumeAtteso) < Math.Max(2, volumeAtteso * 0.02),
                      string.Format("tolti {0:0.#} mm3, attesi {1:0.#}; l'operazione dice {2}",
                          tolto, volumeAtteso,
                          misure.ContainsKey("materiale_tolto_mm3") ? misure["materiale_tolto_mm3"] : "?"));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 1. smusso_spigoli — il volume tolto dice smusso o raccordo
        // ==================================================================
        private static void ProvaSmusso()
        {
            Console.WriteLine();
            Console.WriteLine("=== smusso_spigoli (add-in) ===");

            // Uno smusso 5x5 a 45 gradi su uno spigolo lungo L toglie
            // 5*5/2*L = 1250 mm3 su 100 mm. Un RACCORDO R5 ne toglierebbe
            // (1 - pi/4)*25*L = 536,5: se swChamferAngleDistance fosse letto
            // come un raccordo, il numero lo direbbe subito.
            Smusso("smusso 5 mm a 45 gradi", 5, 45, 0, l => 5.0 * 5.0 / 2.0 * l,
                   "un raccordo R5 ne toglierebbe " + ((1 - Math.PI / 4) * 25 * 100).ToString("0"));

            // Due lati diversi: DistanceDistance. Se l'enum finisse su
            // AngleDistance, il secondo lato verrebbe ignorato e si
            // toglierebbe 3*3/2*L = 450 invece di 900.
            Smusso("smusso 3x6, cianfrino", 3, 45, 6, l => 3.0 * 6.0 / 2.0 * l,
                   "se ignorasse il secondo lato sarebbero 450");

            // Uno smusso piu' grande di meta' del materiale non ci sta: deve
            // essere rifiutato PRIMA, non lasciare l'albero a meta'.
            Smusso("smusso 200 mm su piastra da 20 (deve fallire)", 200, 45, 0, null, null,
                   deveFallire: true);
        }

        private static void Smusso(string nome, double lato, double angolo, double secondoLato,
                                   Func<double, double> volumeAtteso, string controprova,
                                   bool deveFallire = false)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 100, 60, 20);
                var stato = LeggiStato(doc);
                string uid = SpigoloPiuLungo(stato);
                if (uid == null) { Segna(nome, false, "nessuno spigolo nello stato"); return; }
                double lungo = LunghezzaDiSpigolo(stato, uid);
                double v0 = VolumeDelDocumento(doc);

                var argomenti = new Dictionary<string, object>
                {
                    ["spigoli_uid"] = new List<object> { uid },
                    ["lato_mm"] = lato,
                    ["angolo_gradi"] = angolo,
                };
                if (secondoLato > 0) argomenti["secondo_lato_mm"] = secondoLato;

                string errore;
                var misure = Esegui(doc, "smusso_spigoli", argomenti, out errore, stato);
                if (deveFallire)
                {
                    Segna(nome, misure == null && !string.IsNullOrEmpty(errore),
                          misure == null ? ("rifiutato: " + Corto(errore)) : "NON ha rifiutato");
                    return;
                }
                if (misure == null) { Segna(nome, false, "fallito: " + Corto(errore)); return; }

                double tolto = v0 - VolumeDelDocumento(doc);
                double atteso = volumeAtteso(lungo);
                Segna(nome, Math.Abs(tolto - atteso) < Math.Max(2, atteso * 0.03),
                      string.Format("spigolo {0:0.#} mm, tolti {1:0.#} mm3, attesi {2:0.#} ({3})",
                                    lungo, tolto, atteso, controprova));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 2. rivoluzione — il volume di un solido di rotazione si calcola
        // ==================================================================
        private static void ProvaRivoluzioni()
        {
            Console.WriteLine();
            Console.WriteLine("=== rivoluzione (add-in) ===");

            // Cilindro pieno: rettangolo 50 x 20 ruotato attorno a y=0.
            // Raggio 20, altezza 50 -> pi*400*50 = 62831,85 mm3.
            Rivoluzione("cilindro D40 lungo 50",
                new object[]
                {
                    new object[]
                    {
                        new double[] { 0, 0 }, new double[] { 0, 20 },
                        new double[] { 50, 20 }, new double[] { 50, 0 },
                    },
                },
                new object[] { new double[] { 0, 0 }, new double[] { 50, 0 } },
                360, Math.PI * 400 * 50);

            // Albero a gradini D30 per 40, poi D20 per 30: e' l'esempio scritto
            // nella descrizione dell'operazione, quindi e' quello che il
            // modello copiera' piu' spesso. Se sbaglia questo sbaglia in
            // produzione.
            Rivoluzione("albero a gradini D30/D20 (l'esempio della descrizione)",
                new object[]
                {
                    new object[]
                    {
                        new double[] { 0, 0 }, new double[] { 0, 15 },
                        new double[] { 40, 15 }, new double[] { 40, 10 },
                        new double[] { 70, 10 }, new double[] { 70, 0 },
                    },
                },
                new object[] { new double[] { 0, 0 }, new double[] { 70, 0 } },
                360, Math.PI * 225 * 40 + Math.PI * 100 * 30);

            // Mezzo giro: esattamente meta'. Verifica che angolo_gradi arrivi
            // in radianti giusti e non venga ignorato.
            Rivoluzione("mezzo cilindro, 180 gradi",
                new object[]
                {
                    new object[]
                    {
                        new double[] { 0, 0 }, new double[] { 0, 20 },
                        new double[] { 50, 20 }, new double[] { 50, 0 },
                    },
                },
                new object[] { new double[] { 0, 0 }, new double[] { 50, 0 } },
                180, Math.PI * 400 * 50 / 2);

            // Boccola: profilo cavo, due contorni. Corona da R20 a R10, alta
            // 30 -> pi*(400-100)*30 = 28274,33.
            Rivoluzione("boccola D40/D20 alta 30 (profilo cavo)",
                new object[]
                {
                    new object[]
                    {
                        new double[] { 0, 10 }, new double[] { 0, 20 },
                        new double[] { 30, 20 }, new double[] { 30, 10 },
                    },
                },
                new object[] { new double[] { 0, 0 }, new double[] { 30, 0 } },
                360, Math.PI * (400 - 100) * 30);

            // UN PROFILO CHE ATTRAVERSA L'ASSE non da' errore in SolidWorks:
            // da' un solido che si autointerseca. L'operazione conta i corpi
            // proprio per questo. Deve rifiutare.
            Rivoluzione("profilo a cavallo dell'asse (deve fallire)",
                new object[]
                {
                    new object[]
                    {
                        new double[] { 0, -20 }, new double[] { 0, 20 },
                        new double[] { 50, 20 }, new double[] { 50, -20 },
                    },
                },
                new object[] { new double[] { 0, 0 }, new double[] { 50, 0 } },
                360, 0, deveFallire: true);
        }

        private static void Rivoluzione(string nome, object contorni, object asse,
                                        double gradi, double atteso, bool deveFallire = false)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                string errore;
                var misure = Esegui(doc, "rivoluzione", new Dictionary<string, object>
                {
                    ["contorni"] = contorni,
                    ["asse"] = asse,
                    ["angolo_gradi"] = gradi,
                    ["piano"] = "frontale",
                }, out errore);

                if (deveFallire)
                {
                    Segna(nome, misure == null && !string.IsNullOrEmpty(errore),
                          misure == null ? ("rifiutato: " + Corto(errore)) : "NON ha rifiutato");
                    return;
                }
                if (misure == null) { Segna(nome, false, "fallito: " + Corto(errore)); return; }

                double v = VolumeDelDocumento(doc);
                int corpi = ContaCorpi(doc);
                Segna(nome, corpi == 1 && Math.Abs(v - atteso) < Math.Max(5, atteso * 0.02),
                      string.Format("volume {0:0.#} mm3, atteso {1:0.#}, corpi {2}", v, atteso, corpi));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 3. guscio — il verso di «Outward», misurato sull'ingombro
        // ==================================================================
        // ── QUESTA PROVA ADESSO E' UN FILE ────────────────────────────────
        //
        // `casi/lavorazioni/guscio_interno.json` e `guscio_esterno.json` (voce
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
        // 4. crea_piano — DA CHE PARTE finisce con la distanza negativa
        // ==================================================================
        private static void ProvaCreaPiano()
        {
            Console.WriteLine();
            Console.WriteLine("=== crea_piano (add-in) ===");
            // Il piano SUPERIORE e' XZ: la sua normale e' +Y. Un piano a +100
            // deve stare a y=+100, uno a -100 a y=-100.
            //
            // L'add-in passa il valore ASSOLUTO e aggiunge OptionFlip quando la
            // distanza e' negativa. E' la lettura piu' letterale dell'enum, ma
            // circolano macro che passano il segno direttamente: se fosse
            // quella giusta, il piano a -100 finirebbe a +100 e nessun errore
            // lo direbbe. E' esattamente il caso «sembra giusto».
            Piano("crea_piano superiore +100", "superiore", 100, 1);
            Piano("crea_piano superiore -100", "superiore", -100, 1);
            Piano("crea_piano frontale -250", "frontale", -250, 2);
        }

        private static void Piano(string nome, string piano, double distanza, int indiceQuota)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                int prima = ContaPiani(doc);
                string errore;
                var misure = Esegui(doc, "crea_piano", new Dictionary<string, object>
                {
                    ["piano"] = piano,
                    ["distanza_mm"] = distanza,
                }, out errore);
                if (misure == null) { Segna(nome, false, "fallito: " + Corto(errore)); return; }

                int dopo = ContaPiani(doc);
                var t = TraslazioneUltimoPiano(doc);
                double quota = t == null ? double.NaN : t[indiceQuota];
                Segna(nome, dopo == prima + 1 && Math.Abs(quota - distanza) < 0.1,
                      string.Format("piani {0}->{1}, origine del piano {2}, quota {3:0.##} (attesa {4:0.##})",
                          prima, dopo,
                          t == null ? "non leggibile" : "[" + string.Join(",", t.Select(x => x.ToString("0.#"))) + "]",
                          quota, distanza));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 5. crea_asse — e la domanda vera: si ritrova poi per nome?
        // ==================================================================
        private static void ProvaCreaAsse()
        {
            Console.WriteLine();
            Console.WriteLine("=== crea_asse (add-in) ===");
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
                Segna("crea_asse: la parete del foro compare nello stato", true, cilindrica);

                string nomeAsse;
                var misure = EseguiConNome(doc, "crea_asse", new Dictionary<string, object>
                {
                    ["faccia_uid"] = cilindrica,
                }, out errore, out nomeAsse, stato);
                if (misure == null) { Segna("crea_asse dalla parete del foro", false, Corto(errore)); return; }
                string asse = misure.ContainsKey("asse") ? Convert.ToString(misure["asse"]) : "";
                Segna("crea_asse dalla parete del foro", !string.IsNullOrEmpty(asse), "asse « " + asse + " »");

                // LA PROVA CHE CONTA: «ripeti_lineare» e «ripeti_circolare»
                // ritrovano l'asse con SelectByID2(nome, "AXIS", ...). Se
                // quella riga non aggancia, crea_asse riesce e la ripetizione
                // fallisce subito dopo — cioe' l'operazione e' inutile.
                bool agganciato = false;
                try
                {
                    doc.ClearSelection2(true);
                    agganciato = doc.Extension.SelectByID2(asse, "AXIS", 0, 0, 0, false, 1, null, 0);
                }
                catch { }
                Segna("crea_asse: l'asse si aggancia con SelectByID2(\"AXIS\")", agganciato,
                      agganciato ? "" : "la ripetizione non lo trovera' mai");

                stato = LeggiStato(doc);
                var assi = stato.ContainsKey("assi") ? stato["assi"] as System.Collections.IList : null;
                Segna("crea_asse: l'asse compare nello stato del documento",
                      assi != null && assi.Count > 0,
                      assi == null ? "campo assente" : assi.Count + " assi: " + AssiScritti(assi));

                // La faccia piana NON e' un asse: deve essere rifiutata, e con
                // un motivo che dica cosa fare.
                string scarto;
                var no = Esegui(doc, "crea_asse", new Dictionary<string, object>
                {
                    ["faccia_uid"] = facciaPiana,
                }, out scarto, LeggiStato(doc));
                Segna("crea_asse su faccia piana (deve fallire)",
                      no == null && !string.IsNullOrEmpty(scarto),
                      no == null ? "rifiutato: " + Corto(scarto) : "NON ha rifiutato");
            }
            catch (Exception ex) { Segna("crea_asse", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 6. ripeti_lineare — la direzione, che e' il dubbio vero
        // ==================================================================
        private static void ProvaRipetiLineare()
        {
            Console.WriteLine();
            Console.WriteLine("=== ripeti_lineare (add-in) ===");
            IModelDoc2 doc = null;
            try
            {
                // LA STRADA CHE IL MODELLO HA DAVVERO, dalla 0.31.0.
                //
                // Fino alla 0.30.2 questa operazione pretendeva un asse di
                // riferimento, e l'unica operazione che ne crea uno e'
                // «crea_asse», che lo prende dal centro di una faccia
                // CILINDRICA: l'asse che ne esce e' sempre quello di un foro,
                // cioe' perpendicolare alla faccia su cui la lavorazione sta.
                // Ripetere lungo quello vuol dire ripetere dentro lo spessore,
                // e «otto fori in fila sulla piastra» non aveva nessuna forma
                // valida.
                //
                // Adesso la direzione si prende da uno SPIGOLO DRITTO, che lo
                // stato del documento elenca gia'. Questa prova percorre
                // esattamente quella strada.
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato = LeggiStato(doc);
                string davanti = FacciaConVerso(stato, "davanti");
                if (davanti == null) { Segna("ripeti_lineare: faccia di partenza", false, "assente"); return; }

                string errore, nomeForo;
                var foro = EseguiConNome(doc, "foro_guidato", new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["tipo"] = "passaggio",
                    ["taglia"] = "M8",
                    ["passante"] = true,
                    ["posizioni"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = 0.0, ["y_mm"] = -40.0 },
                    },
                }, out errore, out nomeForo, stato);
                if (foro == null) { Segna("ripeti_lineare: foro da ripetere", false, Corto(errore)); return; }

                stato = LeggiStato(doc);
                string spigoloLungo = SpigoloDiLunghezza(stato, 200);
                string spigoloCorto = SpigoloDiLunghezza(stato, 120);
                if (spigoloLungo == null)
                {
                    Segna("ripeti_lineare: lo spigolo da 200 e' nello stato", false,
                          "non trovato: il modello non avrebbe niente da indicare");
                    return;
                }
                Segna("ripeti_lineare: lo spigolo da 200 e' nello stato", true, spigoloLungo);

                // DOVE STA DAVVERO il seme: si rilegge dalla geometria, non si
                // assume dal parametro. Il verso degli assi dello schizzo su
                // una faccia non e' garantito, e la prova serve proprio a
                // misurare uno spostamento.
                var semi = CilindriPerRaggio(doc, 4.5, new double[] { 0, 0, 1 });
                if (semi.Count != 1)
                {
                    Segna("ripeti_lineare: un solo foro M8 prima di ripetere", false,
                          semi.Count + " cilindri D9 con asse lungo Z");
                    return;
                }
                double[] seme = semi[0];
                double v0 = VolumeDelDocumento(doc);
                double unForo = Math.PI * 4.5 * 4.5 * 20;

                var misure = Esegui(doc, "ripeti_lineare", new Dictionary<string, object>
                {
                    ["feature_uid"] = nomeForo,
                    ["asse1_uid"] = spigoloLungo,
                    ["passo1_mm"] = 40.0,
                    ["numero_istanze1"] = 3,
                }, out errore, stato);
                if (misure == null)
                {
                    Segna("ripeti_lineare 3 istanze passo 40 lungo lo spigolo", false,
                          "fallito: " + Corto(errore));
                    return;
                }

                double tolto = v0 - VolumeDelDocumento(doc);
                var dopo = CilindriPerRaggio(doc, 4.5, new double[] { 0, 0, 1 });
                var scostamenti = dopo
                    .Select(p => p[0] - seme[0])
                    .OrderBy(x => x)
                    .Select(x => x.ToString("0.#"))
                    .ToList();
                Segna("ripeti_lineare 3 istanze passo 40 lungo lo spigolo",
                      dopo.Count == 3 && Math.Abs(tolto - 2 * unForo) < unForo * 0.1,
                      string.Format("fori D9 {0}, tolti {1:0.#} mm3 (attesi {2:0.#}), scostamenti su X: {3}",
                          dopo.Count, tolto, 2 * unForo, string.Join(" / ", scostamenti)));

                // Il verso: le istanze a +40 e +80 vuol dire concorde con lo
                // spigolo, a -40 e -80 opposto. Non e' un fallimento — e' il
                // dato che serve alla descrizione, perche' «capovolgi_1»
                // esiste esattamente per questo.
                bool avanti = dopo.Any(p => p[0] - seme[0] > 30);
                bool indietro = dopo.Any(p => p[0] - seme[0] < -30);
                Segna("ripeti_lineare: da che parte va", avanti || indietro,
                      avanti && !indietro ? "concorde con lo spigolo"
                      : indietro && !avanti ? "opposto allo spigolo: «capovolgi_1» inverte"
                      : "istanze da tutte e due le parti, o nessuna");
            }
            catch (Exception ex) { Segna("ripeti_lineare", false, Perche(ex)); }
            finally { Chiudi(doc); }

            // ── la griglia, e lo spigolo curvo che deve essere rifiutato ──
            doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato = LeggiStato(doc);
                string davanti = FacciaConVerso(stato, "davanti");
                string errore, nomeForo;
                var foro = EseguiConNome(doc, "foro_guidato", new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["tipo"] = "passaggio",
                    ["taglia"] = "M8",
                    ["passante"] = true,
                    ["posizioni"] = new List<object>
                    {
                        // IL SEME STA AL CENTRO, E NON E' UN DETTAGLIO.
                        //
                        // Uno spigolo ha DUE versi e nessuno dei due e'
                        // «giusto»: quale dei due prenda la ripetizione e' un
                        // dato, non un difetto — `capovolgi_1` e `capovolgi_2`
                        // esistono esattamente per invertirlo, e la prova a una
                        // direzione lo dice gia'.
                        //
                        // Col seme a (-60, -30) la prova invece DIPENDEVA dal
                        // verso: la seconda fila cadeva a y = -60, cioe' sul
                        // filo della piastra, e i tre fori uscivano a meta'.
                        // Misurato il 10 settembre 2026: 2 fori interi (2544,7)
                        // piu' 3 mezzi fori (1908,5) fanno 4453,2 mm3 contro i
                        // 6361,7 attesi. Il conto tornava al millimetro cubo, e
                        // l'add-in non aveva nessuna colpa.
                        //
                        // Dal centro la griglia ci sta in tutte e quattro le
                        // combinazioni di verso: x entro ±80 su ±100, y entro
                        // ±30 su ±60. Cosi' la prova misura la GRIGLIA, non
                        // l'orientamento casuale di uno spigolo.
                        new Dictionary<string, object> { ["x_mm"] = 0.0, ["y_mm"] = 0.0 },
                    },
                }, out errore, out nomeForo, stato);
                if (foro == null) { Segna("griglia: foro di partenza", false, Corto(errore)); return; }

                stato = LeggiStato(doc);
                string lungo = SpigoloDiLunghezza(stato, 200);
                string corto = SpigoloDiLunghezza(stato, 120);
                if (lungo == null || corto == null)
                {
                    Segna("griglia: due spigoli perpendicolari nello stato", false,
                          "200: " + (lungo ?? "assente") + ", 120: " + (corto ?? "assente"));
                }
                else
                {
                    double v0 = VolumeDelDocumento(doc);
                    double unForo = Math.PI * 4.5 * 4.5 * 20;
                    var misure = Esegui(doc, "ripeti_lineare", new Dictionary<string, object>
                    {
                        ["feature_uid"] = nomeForo,
                        ["asse1_uid"] = lungo,
                        ["passo1_mm"] = 40.0,
                        ["numero_istanze1"] = 3,
                        ["asse2_uid"] = corto,
                        ["passo2_mm"] = 30.0,
                        ["numero_istanze2"] = 2,
                    }, out errore, stato);
                    if (misure == null)
                    {
                        Segna("griglia 3x2 su due spigoli", false, "fallita: " + Corto(errore));
                    }
                    else
                    {
                        // Griglia PIENA: 3 x 2 = 6 fori, cinque in piu' del seme.
                        double tolto = v0 - VolumeDelDocumento(doc);
                        var fori = CilindriPerRaggio(doc, 4.5, new double[] { 0, 0, 1 });
                        Segna("griglia 3x2 su due spigoli",
                              fori.Count == 6 && Math.Abs(tolto - 5 * unForo) < unForo * 0.1,
                              string.Format("fori {0} (attesi 6), tolti {1:0.#} mm3 (attesi {2:0.#})",
                                  fori.Count, tolto, 5 * unForo));
                    }
                }

                // UNO SPIGOLO CURVO NON E' UNA DIREZIONE. Il bordo del foro e'
                // un cerchio: da' la tangente in un punto che nessuno ha
                // scelto, e senza il controllo passerebbe senza errore.
                stato = LeggiStato(doc);
                string cerchio = SpigoloDiLunghezza(stato, Math.PI * 9.0);
                if (cerchio == null)
                {
                    // NON E' UNA PROVA SALTATA: E' UNA PROVA DIVERSA, E PASSA.
                    //
                    // Il controllo sulla rettilineita' dello spigolo esiste
                    // nell'add-in, ma il MODELLO non ha modo di attivarlo,
                    // perche' un bordo curvo non compare fra gli spigoli che lo
                    // stato elenca: non puo' nominarlo. Fingere una prova
                    // «rifiutato» chiamando l'add-in con un identificativo che
                    // il modello non potrebbe mai scrivere misurerebbe una
                    // strada che nessuno percorre.
                    //
                    // Quello che si puo' misurare, e che conta, e' proprio
                    // questo: che nello stato NON ci sia nessuno spigolo curvo.
                    // Se un domani ce ne finisse uno, la strada si aprirebbe e
                    // questa prova tornerebbe a essere quella di prima.
                    int curvi = 0;
                    var spigoli = stato["spigoli"] as System.Collections.IList;
                    if (spigoli != null)
                        foreach (Dictionary<string, object> sp in spigoli)
                        {
                            object dritto;
                            if (sp.TryGetValue("dritto", out dritto) && !Convert.ToBoolean(dritto)) curvi++;
                        }
                    Segna("nessuno spigolo curvo e' offerto al modello", curvi == 0,
                          curvi == 0
                              ? "lo stato elenca " + (spigoli == null ? 0 : spigoli.Count) +
                                " spigoli, tutti dritti: il bordo del foro non e' nominabile"
                              : curvi + " spigoli curvi nello stato: il modello puo' nominarli, " +
                                "e allora il controllo sulla rettilineita' va provato per davvero");
                }
                else
                {
                    string scarto;
                    var no = Esegui(doc, "ripeti_lineare", new Dictionary<string, object>
                    {
                        ["feature_uid"] = nomeForo,
                        ["asse1_uid"] = cerchio,
                        ["passo1_mm"] = 40.0,
                        ["numero_istanze1"] = 3,
                    }, out scarto, stato);
                    Segna("ripeti_lineare su spigolo curvo (deve fallire)",
                          no == null && !string.IsNullOrEmpty(scarto),
                          no == null ? "rifiutato: " + Corto(scarto) : "NON ha rifiutato");
                }
            }
            catch (Exception ex) { Segna("ripeti_lineare, griglia", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 7. specchia — il foro deve comparire DALL'ALTRA PARTE
        // ==================================================================
        private static void ProvaSpecchia()
        {
            Console.WriteLine();
            Console.WriteLine("=== specchia (add-in) ===");
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                PiastraCentrata(doc, 200, 120, 20);
                var stato = LeggiStato(doc);
                string davanti = FacciaConVerso(stato, "davanti");
                if (davanti == null) { Segna("specchia: faccia di partenza", false, "assente"); return; }

                // Un foro NON centrato e NON sull'asse di simmetria: se lo
                // specchio prendesse il piano sbagliato, la copia finirebbe da
                // un'altra parte e la costruzione riuscirebbe lo stesso.
                string errore, nomeForo;
                var foro = EseguiConNome(doc, "foro_guidato", new Dictionary<string, object>
                {
                    ["faccia_uid"] = davanti,
                    ["tipo"] = "passaggio",
                    ["taglia"] = "M8",
                    ["passante"] = true,
                    ["posizioni"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = 60.0, ["y_mm"] = 25.0 },
                    },
                }, out errore, out nomeForo, stato);
                if (foro == null) { Segna("specchia: foro di partenza", false, Corto(errore)); return; }

                var semi = CilindriPerRaggio(doc, 4.5, new double[] { 0, 0, 1 });
                if (semi.Count != 1) { Segna("specchia: un solo foro prima", false, semi.Count + " cilindri"); return; }
                double[] seme = semi[0];
                double v0 = VolumeDelDocumento(doc);

                // Piano DESTRO = YZ, cioe' x = 0: la copia deve finire a -x,
                // con la stessa y.
                var misure = Esegui(doc, "specchia", new Dictionary<string, object>
                {
                    ["feature_uid"] = nomeForo,
                    ["piano"] = "destro",
                }, out errore, LeggiStato(doc));
                if (misure == null) { Segna("specchia sul piano destro", false, "fallito: " + Corto(errore)); return; }

                double unForo = Math.PI * 4.5 * 4.5 * 20;
                double tolto = v0 - VolumeDelDocumento(doc);
                var dopo = CilindriPerRaggio(doc, 4.5, new double[] { 0, 0, 1 });
                var copia = dopo.FirstOrDefault(p => Math.Abs(p[0] - seme[0]) > 1);
                bool specchiata = copia != null &&
                                  Math.Abs(copia[0] + seme[0]) < 0.1 &&
                                  Math.Abs(copia[1] - seme[1]) < 0.1;

                Segna("specchia sul piano destro",
                      dopo.Count == 2 && specchiata && Math.Abs(tolto - unForo) < unForo * 0.1,
                      string.Format("seme [{0:0.#},{1:0.#}], copia {2}, tolti {3:0.#} mm3 (attesi {4:0.#})",
                          seme[0], seme[1],
                          copia == null ? "nessuna" : "[" + copia[0].ToString("0.#") + "," + copia[1].ToString("0.#") + "]",
                          tolto, unForo));

                // Un piano inventato non deve passare per buono.
                string scarto;
                var no = Esegui(doc, "specchia", new Dictionary<string, object>
                {
                    ["feature_uid"] = nomeForo,
                    ["piano"] = "diagonale",
                }, out scarto, LeggiStato(doc));
                Segna("specchia su un piano che non esiste (deve fallire)",
                      no == null && !string.IsNullOrEmpty(scarto),
                      no == null ? "rifiutato: " + Corto(scarto) : "NON ha rifiutato");
            }
            catch (Exception ex) { Segna("specchia", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

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

        /// <summary>
        /// I cilindri di un dato raggio con l'asse lungo una direzione data:
        /// {x, y, z} del punto sull'asse, in mm. E' cosi' che si scopre DOVE
        /// e' finito un foro, invece di credere alla richiesta.
        /// </summary>
        private static List<double[]> CilindriPerRaggio(IModelDoc2 doc, double raggioMm, double[] direzione)
        {
            var fuori = new List<double[]>();
            var visti = new List<double[]>();
            var corpi = (doc as IPartDoc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null) return fuori;
            foreach (var c in corpi)
            {
                var facce = (c as IBody2).GetFaces() as object[];
                if (facce == null) continue;
                foreach (var o in facce)
                {
                    ISurface su;
                    try { su = (o as IFace2).IGetSurface(); } catch { continue; }
                    if (su == null) continue;
                    bool cilindro;
                    try { cilindro = su.IsCylinder(); } catch { continue; }
                    if (!cilindro) continue;
                    var par = su.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    // CylinderParams: [0..2] punto sull'asse, [3..5] direzione,
                    // [6] raggio, tutto in metri.
                    if (Math.Abs(par[6] * 1000 - raggioMm) > 0.05) continue;
                    double coseno = Math.Abs(par[3] * direzione[0] + par[4] * direzione[1] + par[5] * direzione[2]);
                    if (coseno < 0.99) continue;

                    var punto = new[] { par[0] * 1000, par[1] * 1000, par[2] * 1000 };
                    // Un foro passante lascia piu' facce cilindriche sullo
                    // stesso asse: si conta il foro, non la faccia.
                    bool gia = visti.Any(p =>
                        Math.Abs(p[0] - punto[0]) < 0.1 &&
                        Math.Abs(p[1] - punto[1]) < 0.1 &&
                        Math.Abs(p[2] - punto[2]) < 0.1);
                    if (gia) continue;
                    visti.Add(punto);
                    fuori.Add(punto);
                }
            }
            return fuori;
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

        /// <summary>
        /// Lo spigolo di una lunghezza data, in mm. Si sceglie per MISURA e
        /// non per posizione nell'elenco: su una piastra 200x120x20 ci sono
        /// quattro spigoli da 200, e quale sia il primo non e' garantito.
        /// </summary>
        private static string SpigoloDiLunghezza(Dictionary<string, object> stato, double mm)
        {
            var spigoli = stato.ContainsKey("spigoli") ? stato["spigoli"] as System.Collections.IList : null;
            if (spigoli == null) return null;
            foreach (Dictionary<string, object> e in spigoli)
                if (Math.Abs(Convert.ToDouble(e["lunghezza_mm"]) - mm) < 0.5)
                    return Convert.ToString(e["uid"]);
            return null;
        }

        private static double LunghezzaDiSpigolo(Dictionary<string, object> stato, string uid)
        {
            var spigoli = stato["spigoli"] as System.Collections.IList;
            foreach (Dictionary<string, object> e in spigoli)
                if (Convert.ToString(e["uid"]) == uid)
                    return Convert.ToDouble(e["lunghezza_mm"]);
            return 0;
        }

        private static string AssiScritti(System.Collections.IList assi)
        {
            var nomi = new List<string>();
            foreach (Dictionary<string, object> a in assi) nomi.Add(Convert.ToString(a["uid"]));
            return string.Join(", ", nomi);
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

        // ==================================================================
        // F12. quando il banco non riesce a preparare il pezzo, lo DICE
        // ==================================================================
        /// <summary>
        /// Il null ha un nome.
        ///
        /// Il 15/9/2026 una prova e' uscita rossa con «Riferimento a un oggetto
        /// non impostato su un'istanza di oggetto.» e basta: su diciotto punti
        /// del banco `NuovaParte()` veniva usata senza guardare se avesse
        /// prodotto qualcosa, e un `NullReferenceException` non dice quale dei
        /// diciotto. Adesso `NuovaParte()` non torna null: alza un'eccezione
        /// che nomina il passo.
        ///
        /// Questa prova guarda che il nome ci sia davvero, e che `Perche()` —
        /// la funzione che il banco usa per raccontare un'eccezione — lo porti
        /// fino alla riga di esito. Senza, «il messaggio nomina il passo»
        /// resterebbe un'intenzione scritta in un commento.
        /// </summary>
        private static void ProvaCheIlNullHaUnNome()
        {
            Console.WriteLine();
            Console.WriteLine("=== quando il banco non riesce a preparare il pezzo (F12) ===");

            // ── IL ROSSO: il null alza, e il messaggio nomina il passo ────
            string detto = null;
            string raccontato = null;
            try
            {
                OppureDillo<IModelDoc2>(null, "la parte nuova su cui provare");
            }
            catch (Exception ex)
            {
                detto = ex.Message;
                raccontato = Perche(ex);
            }
            Segna("F12 ROSSO: un null alza, e dice QUALE passo",
                  detto != null && detto.Contains("la parte nuova su cui provare"),
                  Corto(detto));

            // `Perche` tronca a 110 caratteri: si guarda l'inizio, che e' dove
            // sta il nome del passo, e il tipo dell'eccezione.
            Segna("F12: il racconto dell'eccezione porta tipo e passo",
                  raccontato != null &&
                  raccontato.StartsWith("InvalidOperationException:", StringComparison.Ordinal) &&
                  raccontato.Contains("la parte nuova"),
                  Corto(raccontato));

            // ── IL VERDE: quel che c'e' passa, e torna lo stesso oggetto ──
            var qualcosa = new object();
            Segna("F12: quello che c'e' passa, e torna com'era",
                  ReferenceEquals(OppureDillo(qualcosa, "un oggetto qualunque"), qualcosa),
                  "");
        }

        // ==================================================================
        // E10. la frazione dell'ingombro: come SPIEGAZIONE, mai come identita'
        // ==================================================================
        /// <summary>
        /// La frase che dice dove stava una faccia rispetto al pezzo.
        ///
        /// Funzione pura su quattro terne di numeri: non serve SOLIDWORKS, non
        /// serve un documento, e gira in un millesimo di secondo. Sta nel banco
        /// lo stesso perche' il banco e' il posto dove si guarda se qualcosa si
        /// e' rotto, e una funzione che non ha bisogno del CAD e' proprio
        /// quella che nessuno si ricordera' di riprovare a mano.
        /// </summary>
        private static void ProvaDoveStava()
        {
            Console.WriteLine();
            Console.WriteLine("=== dove stava, in frazione dell'ingombro (E10) ===");

            var tipo = _addin.GetType("Riscontro.Disegno.Frasi", false);
            if (tipo == null) { Segna("E10: la classe c'e'", false, "Frasi assente"); return; }
            var metodo = tipo.GetMethod("DoveStavaEDoveSta");
            if (metodo == null)
            {
                Segna("E10: il metodo c'e'", false, "DoveStavaEDoveSta assente");
                return;
            }

            Func<double[], double[], double[], double[], string> di =
                (prima, adesso, sPrima, sAdesso) =>
                    Convert.ToString(metodo.Invoke(
                        null, new object[] { "F6", prima, adesso, sPrima, sAdesso }));

            // ── IL CASO A DELLA VOCE E2, coi numeri misurati il 14/9 ──────
            // Blocco 100 x 60 x 20 centrato: la scatola va da -50 a +50 in X.
            // La quota passa da 100 a 160 e il pezzo cresce DA UNA PARTE SOLA:
            // -50 .. +110. La faccia destra va da 50 a 110 — sessanta
            // millimetri — e sta al 100% del lato tutte e due le volte.
            string caso = di(
                new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 },
                new[] { -50.0, -30.0, 0.0, 50.0, 30.0, 20.0 },
                new[] { -50.0, -30.0, 0.0, 110.0, 30.0, 20.0 });
            Segna("E10: al 100% del lato X, e ci sta ancora",
                  caso != null && caso.Contains("100%") && caso.Contains("ci sta ancora") &&
                  caso.Contains("da 100 a 160"),
                  Corto(caso));

            // ── IL ROSSO: una faccia che si e' spostata RISPETTO al pezzo ──
            //
            // Senza questo, «la frase esce» passerebbe anche se dicesse «ci sta
            // ancora» sempre — cioe' anche se il confronto fra le due frazioni
            // non ci fosse. E' il caso che conta: un foro a 20 mm dal bordo su
            // una piastra che passa da 100 a 300 RESTA a 20 mm dal bordo, e la
            // frazione dice che e' andato a 60. Dirlo e' utile; deciderlo
            // sarebbe il difetto che la voce E10 esiste per non introdurre.
            string spostata = di(
                new[] { 20.0, 0.0, 0.0 }, new[] { 20.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Segna("E10 ROSSO: il foro fermo a 20 mm non si e' mosso, e si tace",
                  string.IsNullOrEmpty(spostata),
                  spostata == null ? "(niente, giusto)" : Corto(spostata));

            // Lo stesso pezzo che cresce, ma la faccia che si sposta a META'
            // invece che restare dov'era in proporzione: la frase deve dire
            // due percentuali diverse e NON deve dire «ci sta ancora».
            string diverse = di(
                new[] { 100.0, 0.0, 0.0 }, new[] { 150.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Segna("E10 ROSSO: frazioni diverse, «ci sta ancora» non si dice",
                  diverse != null && diverse.Contains("100%") && diverse.Contains("50%") &&
                  !diverse.Contains("ci sta ancora"),
                  Corto(diverse));

            // Senza la scatola non si inventa niente.
            Segna("E10: senza la scatola, nessuna frase",
                  string.IsNullOrEmpty(di(
                      new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 }, null, null)),
                  "");
        }

        /// <summary>
        /// IL CRITERIO VERO DELLA VOCE E10: una ricerca di «frazione» o
        /// «proporzional» dentro `FirmaCombacia` o dentro il ciclo di
        /// ritrovamento deve tornare **vuota**.
        ///
        /// ── PERCHE' UNA PROVA CHE LEGGE IL SORGENTE ──────────────────────
        ///
        /// Perche' quello che va difeso non e' un comportamento: e' l'ASSENZA
        /// di un comportamento. La frazione dell'ingombro e' semplice, il
        /// numero adesso c'e' gia', e promuoverla da spiegazione a criterio
        /// costa dieci minuti a chiunque passi di qui fra due mesi — chi l'ha
        /// scritta compreso. Nessuna prova su un pezzo puo' accorgersene finche' i due
        /// criteri danno la stessa risposta, e quando daranno risposte diverse
        /// sara' perche' un pezzo e' gia' venuto sbagliato.
        ///
        /// ── E IL SUO ROSSO ───────────────────────────────────────────────
        ///
        /// Un controllo che cerca una parola e non la trova mai e' identico a
        /// un controllo rotto che non cerca niente. Lo stesso setaccio si passa
        /// su `Frasi.cs`, dove quelle parole ci sono per davvero: se li' non
        /// trova niente, il setaccio e' rotto e lo dice.
        /// </summary>
        private static void ProvaCheLaFrazioneNonDecide()
        {
            Console.WriteLine();
            Console.WriteLine("=== la frazione non decide: il grep che deve tornare vuoto (E10) ===");

            string radice;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                radice = Directory.GetParent(banco.FullName).FullName;
            }
            catch (Exception ex)
            {
                Segna("E10: trovo la radice del repo", false, Perche(ex));
                return;
            }

            string operazioni = Path.Combine(radice, "Riscontro.Disegno", "OperazioniSw.cs");
            string frasi = Path.Combine(radice, "Riscontro.Disegno", "Frasi.cs");
            if (!File.Exists(operazioni) || !File.Exists(frasi))
            {
                Saltata("E10: il grep che deve tornare vuoto",
                        "i sorgenti non sono accanto all'eseguibile");
                return;
            }

            // IL SETACCIO FUNZIONA? In `Frasi.cs` quelle parole ci sono.
            string testoFrasi = File.ReadAllText(frasi);
            Segna("E10 ROSSO: il setaccio trova le parole dove ci sono",
                  Pesca(testoFrasi, 0, testoFrasi.Length),
                  "Frasi.cs");

            string testo = File.ReadAllText(operazioni);

            int a = testo.IndexOf("private static bool FirmaCombacia(", StringComparison.Ordinal);
            Segna("E10: FirmaCombacia non parla di frazioni",
                  a >= 0 && !Pesca(testo, a, FineDelMetodo(testo, a)),
                  a < 0 ? "FirmaCombacia non trovata" : "");

            int b = testo.IndexOf("IFace2 ritrovata = null;", StringComparison.Ordinal);
            Segna("E10: il ciclo di ritrovamento non parla di frazioni",
                  b >= 0 && !Pesca(testo, b, FineDelMetodo(testo, b)),
                  b < 0 ? "il ciclo non trovato" : "");
        }

        /// <summary>Le parole vietate, in un pezzo di sorgente.</summary>
        private static bool Pesca(string testo, int da, int a)
        {
            if (da < 0 || a <= da || a > testo.Length) return false;
            string pezzo = testo.Substring(da, a - da);
            return pezzo.IndexOf("frazione", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   pezzo.IndexOf("proporzional", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Dove finisce il metodo che comincia a `da`: la prima graffa aperta,
        /// e poi si conta fino a quando torna a zero. Rozzo — una graffa dentro
        /// una stringa lo confonderebbe — e sufficiente: qui serve a delimitare
        /// un pezzo di testo su cui cercare due parole, non a compilare.
        /// </summary>
        private static int FineDelMetodo(string testo, int da)
        {
            int i = testo.IndexOf('{', da);
            if (i < 0) return testo.Length;
            int aperte = 0;
            for (; i < testo.Length; i++)
            {
                if (testo[i] == '{') aperte++;
                else if (testo[i] == '}' && --aperte == 0) return i + 1;
            }
            return testo.Length;
        }
    }
}
