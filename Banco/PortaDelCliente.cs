using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
// ALIAS E NON `using static`: il banco ha gia' un `Apri` e un `Chiudi` suoi,
// e i membri della classe vincono su quelli importati. Con l'alias si vede a
// colpo d'occhio quali chiamate escono da questo processo e vanno al ponte.
using Mcp = Condiviso.ClienteMcp;

namespace ProvaSaldatura
{
    /// <summary>
    /// UN GIRO DI PROVE CHE PASSA DALLA PORTA DEL CLIENTE (voce F14).
    ///
    /// ── PERCHE' ESISTE, E NON E' UNA SIMMETRIA ESTETICA ──────────────────
    ///
    /// Tutto il resto di questo banco chiama la LIBRERIA. Il 18/9/2026 il
    /// ponte rifiutava ogni assieme mentre il suo catalogo ne prometteva una
    /// famiglia intera, e il banco aveva 781 prove verdi — cinque delle quali
    /// sugli assiemi — e **nessuna poteva vederlo**: `EsecutorePiano` non
    /// compare in nessun file di `Banco/`.
    ///
    /// *Due misure vere di due cose diverse, e chi legge il numero grande crede
    /// che copra anche l'altra.*
    ///
    /// ── COSA PROVA CHE NIENT'ALTRO PROVA ─────────────────────────────────
    ///
    /// Tutto cio' che sta FRA il cliente e la libreria, con un documento vero
    /// davanti: i controlli preliminari, la traduzione fra JSON e argomenti, i
    /// rifiuti su operazioni che il documento aperto non ammette, e
    /// `guarda_il_pezzo`, che non passa nemmeno dall'esecutore.
    ///
    /// `Prove.Contratti` parla gia' al ponte sullo stdio, ma **senza
    /// SOLIDWORKS**: li' ogni piano muore su «non c'e' nessun documento
    /// aperto», che e' un rifiuto vero e non prova niente sul resto. Qui il
    /// documento c'e'. Il trasporto e' lo stesso — `Condiviso/ClienteMcp.cs` —
    /// perche' due trasporti sarebbero due posti in cui riparare il BOM.
    ///
    /// ── E UNO DEVE ESSERE ROSSO PER COSTRUZIONE ──────────────────────────
    ///
    /// Un piano che chiede un'operazione di `assiemi` con una PARTE aperta
    /// dev'essere rifiutato, e la risposta deve dire **su cosa si sta
    /// lavorando** — non una frase fissa. Un controllo mai visto scattare non
    /// e' un controllo, e un rifiuto che non nomina il documento non si
    /// distingue da uno che rifiuta tutto.
    /// </summary>
    internal static partial class Program
    {
        private static void LaPortaDelCliente()
        {
            var eseguibile = IlPonte();
            if (eseguibile == null)
            {
                ErroreDiBanco("la porta del cliente",
                              "non trovo bin\\oce-solid-agent-mcp.exe — si produce con .\\build.ps1");
                return;
            }

            // IL DOCUMENTO LO PREPARA IL BANCO, e il ponte lo trova davanti.
            // E' esattamente la differenza con `Prove.Contratti`: li' non c'e'
            // niente aperto, e ogni piano muore sul primo controllo.
            IModelDoc2 doc = null;
            Mcp.Ponte p = null;
            string nuovoTitolo = null, salvato = null;
            try
            {
                doc = NuovaParte();
                string titolo = doc.GetTitle();

                p = Mcp.Apri(eseguibile);
                if (p == null)
                {
                    ErroreDiBanco("la porta del cliente", "il ponte non e' partito");
                    return;
                }

                // ── initialize ───────────────────────────────────────────
                var r = Mcp.Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"banco\",\"version\":\"1\"}}}");
                Segna("porta: initialize risponde", r != null,
                      r != null ? "" : "niente entro " + (Mcp.AttesaMs / 1000) + "s. Righe viste: " +
                                       string.Join(" | ", p.Viste.ToArray()));
                if (r == null) return;

                // ── 1. guarda_il_pezzo VEDE IL DOCUMENTO CHE HA APERTO IL BANCO ──
                //
                // Non «risponde qualcosa»: risponde di QUESTO pezzo. E' la
                // prova che il ponte e il banco stanno guardando la stessa
                // sessione di SOLIDWORKS, ed e' la premessa di tutto il resto.
                var testo = TestoDiRisposta(Mcp.Chiedi(p, 2, Chiamata(2, "guarda_il_pezzo", "{}")));
                Segna("porta: guarda_il_pezzo nomina il documento del banco",
                      testo.IndexOf(titolo, StringComparison.OrdinalIgnoreCase) >= 0,
                      titolo + " — " + Corto(testo));

                // ── 2. UN PIANO CHE RIESCE, E IL BANCO RILEGGE IL PEZZO ──
                //
                // La risposta del ponte dice «riuscita». Il banco non si fida
                // della parola: apre lo stesso documento con la COM e misura
                // il volume. E' l'unica cosa che questo gruppo puo' fare e la
                // suite di contratto no.
                double volumePrima = VolumeDiProva(doc);
                var fatto = TestoDiRisposta(Mcp.Chiedi(p, 3, Chiamata(3, "disegna",
                    "{\"passi\":[{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":" +
                    "{\"lunghezza_mm\":120,\"larghezza_mm\":80,\"spessore_mm\":10}}]}")));
                double volumeDopo = VolumeDiProva(doc);

                Segna("porta: il piano riesce", fatto.IndexOf("\"riuscita\":true",
                          StringComparison.OrdinalIgnoreCase) >= 0 ||
                      fatto.IndexOf("\"riuscita\": true", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(fatto));

                // 120 x 80 x 10 = 96.000 mm3, e il pezzo partiva vuoto.
                Segna("porta: e il pezzo e' cambiato davvero, misurato dal banco",
                      Math.Abs(volumeDopo - volumePrima - 96000) < 1,
                      volumePrima.ToString("0.#") + " -> " + volumeDopo.ToString("0.#") +
                      " mm3 (atteso +96000)");

                // ── 3. ROSSO PER COSTRUZIONE: un'operazione di assieme su una parte ──
                var rifiuto = TestoDiRisposta(Mcp.Chiedi(p, 4, Chiamata(4, "disegna",
                    "{\"passi\":[{\"operazione\":\"inserisci_componente\",\"argomenti\":" +
                    "{\"percorso\":\"C:\\\\non\\\\esiste.SLDPRT\"}}]}")));

                // SOLO `riuscita: false`, e niente `||` di scorta. La prima
                // stesura accettava anche una risposta che contenesse la
                // parola «assieme» da qualche parte: sarebbe passata pure
                // con un piano RIUSCITO che la nominasse. *Un `||` messo per
                // sicurezza e' un `||` che un giorno regge da solo.*
                bool rifiutato = rifiuto.IndexOf("\"riuscita\":false",
                                     StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 rifiuto.IndexOf("\"riuscita\": false",
                                     StringComparison.OrdinalIgnoreCase) >= 0;
                Segna("porta: un'operazione di assieme su una PARTE e' rifiutata",
                      rifiutato, Corto(rifiuto));

                // E NON DEVE ESSERE RIUSCITA. Sembra la stessa cosa detta
                // due volte e non lo e': `riuscita` potrebbe mancare del
                // tutto — una risposta malformata, un errore di protocollo —
                // e allora la riga qui sopra sarebbe rossa per il motivo
                // giusto ma per la ragione sbagliata. Questa dice che la
                // parola «true» accanto a «riuscita» non c'e'.
                Segna("porta: e non e' riuscita per sbaglio",
                      rifiuto.IndexOf("\"riuscita\":true",
                          StringComparison.OrdinalIgnoreCase) < 0 &&
                      rifiuto.IndexOf("\"riuscita\": true",
                          StringComparison.OrdinalIgnoreCase) < 0,
                      Corto(rifiuto));

                // E IL RIFIUTO DEVE DIRE SU COSA SI STA LAVORANDO.
                //
                // «Questa operazione vuole un assieme» e' una frase fissa: la
                // stessa risposta arriverebbe con qualunque documento davanti,
                // compreso quello sbagliato. Nominare il documento e' cio' che
                // distingue un controllo che guarda da uno che recita.
                // IL TITOLO, NON LA PAROLA «parte». «parte» sta in qualunque
                // frase fissa sui tipi di documento, e accettarla vorrebbe
                // dire promuovere proprio la risposta che questa riga esiste
                // per bocciare.
                Segna("porta: e il rifiuto dice su cosa sta lavorando",
                      rifiuto.IndexOf(titolo, StringComparison.OrdinalIgnoreCase) >= 0,
                      "cerco «" + titolo + "» in: " + Corto(rifiuto));

                // ── LA TRAPPOLA: zero righe lette aspettando zero righe ──
                //
                // Una notifica non riceve risposta, e «non e' arrivato niente»
                // e' vero anche quando il ponte e' morto. Si manda la notifica
                // e SUBITO DOPO una domanda vera: se la prima riga che torna e'
                // la risposta a quella, la notifica non ha risposto E il ponte
                // e' vivo.
                Mcp.Scrivi(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                var dopoLaNotifica = Mcp.Chiedi(p, 5, Chiamata(5, "guarda_il_pezzo", "{}"));
                Segna("porta: il ponte e' ancora vivo dopo una notifica",
                      dopoLaNotifica != null,
                      dopoLaNotifica != null ? "" : "niente risposta alla domanda dopo la notifica");

                IlModale(p, doc);
                IlCicloDelDocumento(p, titolo, ref nuovoTitolo, ref salvato);
            }
            catch (Exception ex)
            {
                ErroreDiBanco("la porta del cliente", Perche(ex));
            }
            finally
            {
                if (p != null) { Mcp.Chiudi(p); p.Dispose(); }
                Chiudi(doc);
                // Quello che il ciclo del documento ha aperto o creato si chiude
                // qui, qualunque cosa sia successa: la scrivania del banco deve
                // restare pulita (voce K12), o il giro dopo rifiuta di partire.
                try { if (!string.IsNullOrEmpty(nuovoTitolo)) _sw.CloseDoc(nuovoTitolo); } catch { }
                try
                {
                    if (!string.IsNullOrEmpty(salvato))
                    {
                        _sw.CloseDoc(Path.GetFileName(salvato));
                        if (File.Exists(salvato))
                        {
                            File.SetAttributes(salvato, FileAttributes.Normal);
                            File.Delete(salvato);
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// LO STRUMENTO «documento», DALLA PORTA DEL CLIENTE (voci Z-B1, Z-B2,
        /// Z-B5, Z-B6).
        ///
        /// Il ponte ha smesso di aver bisogno di una persona per cominciare:
        /// crea, apre, elenca, porta davanti. Qui si guarda che lo faccia DAVVERO
        /// — il banco rilegge dalla COM quale documento c'e' davanti, invece di
        /// credere alla risposta — e due rossi per costruzione: un file che non
        /// esiste non cambia il documento davanti, e dopo un cambio di documento
        /// «disegna» si rifiuta finche' non si e' guardato di nuovo.
        /// </summary>
        private static void IlCicloDelDocumento(Mcp.Ponte p, string titolo,
                                                ref string nuovoTitolo, ref string salvato)
        {
            Func<string> davanti = () =>
            {
                try { var d = _sw.IActiveDoc2; return d == null ? "(niente)" : d.GetTitle(); }
                catch { return "(non leggibile)"; }
            };

            // ── elenca: c'e' il documento del banco, ed e' davanti ─────────
            var elenco = TestoDiRisposta(Mcp.Chiedi(p, 6, Chiamata(6, "documento", "{\"azione\":\"elenca\"}")));
            Segna("documento: «elenca» nomina il documento del banco",
                  elenco.IndexOf(titolo, StringComparison.OrdinalIgnoreCase) >= 0, Corto(elenco));

            // ── nuovo: una parte vuota, davanti, e il banco lo verifica ────
            var nuovo = Mcp.Ogg(TestoDiRisposta(Mcp.Chiedi(p, 7, Chiamata(7, "documento",
                "{\"azione\":\"nuovo\",\"tipo\":\"parte\"}"))));
            nuovoTitolo = Convert.ToString(Mcp.Dentro(nuovo, "titolo"));
            var attivo = _sw.IActiveDoc2;
            Segna("documento: «nuovo» crea una parte e la porta davanti, riletto dalla COM",
                  !string.IsNullOrEmpty(nuovoTitolo) && attivo != null &&
                  attivo.GetTitle() == nuovoTitolo &&
                  attivo.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART,
                  "risposta «" + nuovoTitolo + "», davanti «" + davanti() + "»");
            Segna("documento: e la parte nuova e' vuota",
                  attivo != null && VolumeDiProva(attivo) < 0.001,
                  attivo == null ? "(niente davanti)" : VolumeDiProva(attivo).ToString("0.###") + " mm3");

            // ── ROSSO PER COSTRUZIONE: disegna subito dopo, senza guardare ─
            //
            // Il documento davanti e' cambiato per mano del ponte stesso. Quello
            // che era stato guardato non vale piu', e «disegna» deve dirlo invece
            // di scrivere in un pezzo che il modello non ha mai visto.
            var subito = TestoDiRisposta(Mcp.Chiedi(p, 8, Chiamata(8, "disegna",
                "{\"passi\":[{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":" +
                "{\"lunghezza_mm\":10,\"larghezza_mm\":10,\"spessore_mm\":10}}]}")));
            Segna("documento: dopo un cambio di documento «disegna» pretende di guardare prima",
                  subito.IndexOf("non ho ancora guardato", StringComparison.OrdinalIgnoreCase) >= 0 &&
                  (attivo == null || VolumeDiProva(attivo) < 0.001),
                  Corto(subito));

            // ── attiva: si torna al documento del banco ───────────────────
            var torna = TestoDiRisposta(Mcp.Chiedi(p, 9, Chiamata(9, "documento",
                "{\"azione\":\"attiva\",\"percorso\":\"" + titolo + "\"}")));
            Segna("documento: «attiva» riporta davanti il documento del banco",
                  davanti() == titolo, "davanti «" + davanti() + "» — " + Corto(torna));

            // ── ROSSO: un file che non esiste non cambia niente ───────────
            var inesistente = Path.Combine(Attese.Lavoro, "non-esiste-" + Guid.NewGuid().ToString("N") + ".sldprt");
            var no = TestoDiRisposta(Mcp.Chiedi(p, 10, Chiamata(10, "documento",
                "{\"azione\":\"apri\",\"percorso\":\"" + inesistente.Replace("\\", "\\\\") + "\"}")));
            Segna("documento: «apri» su un file che non esiste rifiuta, e davanti resta quello di prima",
                  no.IndexOf("non esiste", StringComparison.OrdinalIgnoreCase) >= 0 && davanti() == titolo,
                  "davanti «" + davanti() + "» — " + Corto(no));

            // ── apri un file vero: si salva la parte nuova, la si chiude, la si
            //    riapre dal ponte, e il percorso deve essere quello chiesto ─
            salvato = Path.Combine(Attese.Lavoro, "porta_documento.sldprt");
            try { if (File.Exists(salvato)) { File.SetAttributes(salvato, FileAttributes.Normal); File.Delete(salvato); } } catch { }
            // IL RIFERIMENTO PRESO DOPO «nuovo», non una ricerca per nome.
            // `GetOpenDocumentByName` con il titolo di un documento MAI SALVATO
            // torna null — misurato: la prima stesura salvava cosi', riceveva
            // e=0 w=0 da una chiamata mai fatta, e il file non c'era.
            var daSalvare = attivo;
            int e = 0, w = 0;
            if (daSalvare != null) daSalvare.Extension.SaveAs(salvato, 0, 1, null, ref e, ref w);
            if (daSalvare == null || !File.Exists(salvato))
            {
                ErroreDiBanco("documento: apri", "non sono riuscito a salvare «" + salvato + "» (e=" + e + ", w=" + w +
                              (daSalvare == null ? ", nessun documento in mano" : "") + ")");
                return;
            }
            _sw.CloseDoc(daSalvare.GetTitle());
            nuovoTitolo = null;

            var aperto = Mcp.Ogg(TestoDiRisposta(Mcp.Chiedi(p, 11, Chiamata(11, "documento",
                "{\"azione\":\"apri\",\"percorso\":\"" + salvato.Replace("\\", "\\\\") + "\"}"))));
            var percorsoAperto = Convert.ToString(Mcp.Dentro(aperto, "percorso"));
            var att = _sw.IActiveDoc2;
            Segna("documento: «apri» apre il file chiesto e lo porta davanti, riletto dalla COM",
                  string.Equals(percorsoAperto, salvato, StringComparison.OrdinalIgnoreCase) &&
                  att != null && string.Equals(att.GetPathName(), salvato, StringComparison.OrdinalIgnoreCase),
                  "risposta «" + percorsoAperto + "», davanti «" + (att == null ? "(niente)" : att.GetPathName()) + "»");
            Segna("documento: e un file scrivibile non e' dichiarato in sola lettura",
                  Convert.ToString(Mcp.Dentro(aperto, "sola_lettura")).Equals("False", StringComparison.OrdinalIgnoreCase),
                  Convert.ToString(Mcp.Dentro(aperto, "sola_lettura")));

            // ── Z-B2: lo stesso file con l'attributo di sola lettura ───────
            //
            // Il caso vero e' un collega che ha il file aperto su una cartella di
            // rete; dentro una sola sessione di SOLIDWORKS non si costruisce. Il
            // rosso economico e' l'attributo del file: SOLIDWORKS lo apre lo
            // stesso, in sola lettura, SENZA chiedere niente — che e' esattamente
            // il silenzio che la voce vuole vedere detto.
            _sw.CloseDoc(Path.GetFileName(salvato));
            File.SetAttributes(salvato, FileAttributes.ReadOnly);
            var soloLettura = Mcp.Ogg(TestoDiRisposta(Mcp.Chiedi(p, 12, Chiamata(12, "documento",
                "{\"azione\":\"apri\",\"percorso\":\"" + salvato.Replace("\\", "\\\\") + "\"}"))));
            Segna("documento: un file in sola lettura si apre e il ponte LO DICE",
                  Convert.ToString(Mcp.Dentro(soloLettura, "sola_lettura")).Equals("True", StringComparison.OrdinalIgnoreCase) &&
                  Mcp.Dentro(soloLettura, "attenzione") != null,
                  Corto(Convert.ToString(Mcp.Dentro(soloLettura, "attenzione")) + " | sola_lettura=" +
                        Convert.ToString(Mcp.Dentro(soloLettura, "sola_lettura"))));

            // ── chiudi (voce Z-B1): il ROSSO prima, su un documento sporco ──
            //
            // Il documento del banco ha l'estrusione del passo 2 e non e' mai
            // stato salvato: chiuderlo butterebbe lavoro. Il ponte deve
            // rifiutare, e il banco rilegge dalla COM che sia ancora aperto.
            Func<string, bool> aperto2 = t =>
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                for (int i = 0; d != null && i < 500; i++)
                {
                    if (string.Equals(d.GetTitle(), t, StringComparison.OrdinalIgnoreCase)) return true;
                    d = d.GetNext() as IModelDoc2;
                }
                return false;
            };
            var sporco = TestoDiRisposta(Mcp.Chiedi(p, 13, Chiamata(13, "documento",
                "{\"azione\":\"chiudi\",\"percorso\":\"" + titolo + "\"}")));
            Segna("documento: «chiudi» su un documento con modifiche non salvate rifiuta, e resta aperto",
                  sporco.IndexOf("modifiche non salvate", StringComparison.OrdinalIgnoreCase) >= 0 && aperto2(titolo),
                  "ancora aperto " + aperto2(titolo) + " — " + Corto(sporco));

            // ── e la GEMELLA: un documento appena aperto, senza modifiche ──
            // Il titolo porta l'estensione o no a seconda di come Windows mostra
            // i file: si confronta il gambo del nome.
            string gambo = Path.GetFileNameWithoutExtension(salvato);
            var chiuso = Mcp.Ogg(TestoDiRisposta(Mcp.Chiedi(p, 14, Chiamata(14, "documento",
                "{\"azione\":\"chiudi\",\"percorso\":\"" + salvato.Replace("\\", "\\\\") + "\"}"))));
            bool sparito = _sw.GetOpenDocumentByName(salvato) == null;
            Segna("documento: «chiudi» su un documento senza modifiche lo chiude, riletto dalla COM",
                  sparito && Convert.ToString(Mcp.Dentro(chiuso, "chiuso"))
                                 .StartsWith(gambo, StringComparison.OrdinalIgnoreCase),
                  "sparito dalla COM " + sparito + " — chiuso «" + Convert.ToString(Mcp.Dentro(chiuso, "chiuso")) + "»");
        }

        /// <summary>
        /// LA GUARDIA CONTRO IL MODALE, VISTA SCATTARE (voce Z-C3).
        ///
        /// Fino alla libreria 0.112.0 la guardia era `CommandInProgress`, e la
        /// frase del rifiuto compariva in un solo posto del repository: la riga
        /// che la produceva. `Controlli/Modale` ha misurato che con un modale
        /// aperto `CommandInProgress` resta FALSO: la guardia non poteva
        /// scattare. Dalla 0.113.0 si guarda se la finestra principale e'
        /// abilitata.
        ///
        /// Qui un modale vero — una finestra di messaggio di SOLIDWORKS, aperta
        /// da un altro filo perche' la chiamata non torna finche' e' aperta —
        /// e un piano chiesto al ponte in quel momento: dev'essere rifiutato con
        /// la frase della guardia, e il pezzo non deve cambiare. Poi il modale si
        /// chiude e LO STESSO ponte deve tornare a lavorare: e' la gemella, senza
        /// la quale una guardia che rifiuta sempre passerebbe questa prova.
        /// </summary>
        private static void IlModale(Mcp.Ponte p, IModelDoc2 doc)
        {
            IntPtr cornice = IntPtr.Zero;
            try { cornice = new IntPtr(((IFrame)_sw.Frame()).GetHWnd()); } catch { }
            uint pid = 0;
            if (cornice != IntPtr.Zero) GetWindowThreadProcessId(cornice, out pid);
            if (pid == 0)
            {
                ErroreDiBanco("il modale", "non leggo la finestra principale di SOLIDWORKS");
                return;
            }

            var filo = new System.Threading.Thread(() =>
            {
                try
                {
                    var suo = (ISldWorks)System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application");
                    suo.SendMsgToUser2("Banco, voce Z-C3: questa finestra si chiude da sola.",
                        (int)SolidWorks.Interop.swconst.swMessageBoxIcon_e.swMbInformation,
                        (int)SolidWorks.Interop.swconst.swMessageBoxBtn_e.swMbOk);
                }
                catch { }
            });
            filo.SetApartmentState(System.Threading.ApartmentState.STA);
            filo.IsBackground = true;
            filo.Start();

            IntPtr dialogo = IntPtr.Zero;
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            while (dialogo == IntPtr.Zero && t0.ElapsedMilliseconds < 8000)
            {
                System.Threading.Thread.Sleep(100);
                dialogo = DialogoDi(pid);
            }
            if (dialogo == IntPtr.Zero)
            {
                ErroreDiBanco("il modale", "la finestra di messaggio non si e' vista entro 8 s: la guardia non si puo' provare");
                return;
            }

            string conModale;
            double volumePrima = VolumeDiProva(doc), volumeDopo;
            try
            {
                conModale = TestoDiRisposta(Mcp.Chiedi(p, 30, Chiamata(30, "disegna",
                    "{\"passi\":[{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":" +
                    "{\"lunghezza_mm\":20,\"larghezza_mm\":20,\"spessore_mm\":5}}]}")));
            }
            finally
            {
                // IL MODALE SI CHIUDE IN OGNI CASO: lasciato aperto, pianta il
                // resto del giro e quello dopo.
                PostMessage(dialogo, 0x0010, IntPtr.Zero, IntPtr.Zero);
                filo.Join(8000);
                System.Threading.Thread.Sleep(300);
            }
            volumeDopo = VolumeDiProva(doc);

            Segna("modale: con una finestra modale aperta il piano e' rifiutato con la frase della guardia",
                  conModale.IndexOf("finestra aperta che aspetta una risposta", StringComparison.OrdinalIgnoreCase) >= 0,
                  Corto(conModale));
            Segna("modale: e il pezzo non e' cambiato",
                  Math.Abs(volumeDopo - volumePrima) < 0.01,
                  volumePrima.ToString("0.#") + " -> " + volumeDopo.ToString("0.#") + " mm3");

            // LA GEMELLA: modale chiuso, finestra abilitata, e lo stesso ponte
            // lavora. Un'operazione che legge soltanto, per non cambiare il pezzo
            // su cui il resto del gruppo conta.
            bool abilitata = IsWindowEnabled(cornice);
            var senza = TestoDiRisposta(Mcp.Chiedi(p, 31, Chiamata(31, "disegna",
                "{\"passi\":[{\"operazione\":\"proprieta_di_massa\",\"argomenti\":{}}]}")));
            Segna("modale: chiuso il modale, lo stesso ponte torna a lavorare",
                  abilitata && senza.IndexOf("finestra aperta", StringComparison.OrdinalIgnoreCase) < 0 &&
                  (senza.IndexOf("\"riuscita\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   senza.IndexOf("\"riuscita\": true", StringComparison.OrdinalIgnoreCase) >= 0),
                  "finestra abilitata " + abilitata + " — " + Corto(senza));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc f, IntPtr l);

        /// <summary>Una finestra di dialogo (#32770) visibile del processo dato.</summary>
        private static IntPtr DialogoDi(uint pid)
        {
            IntPtr trovato = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                uint suo;
                GetWindowThreadProcessId(h, out suo);
                if (suo != pid || !IsWindowVisible(h)) return true;
                var nome = new System.Text.StringBuilder(64);
                GetClassName(h, nome, nome.Capacity);
                if (nome.ToString() == "#32770") { trovato = h; return false; }
                return true;
            }, IntPtr.Zero);
            return trovato;
        }

        /// <summary>L'eseguibile del ponte, o `null`.</summary>
        private static string IlPonte()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var radice = Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
                var p = Path.Combine(radice, "bin", "oce-solid-agent-mcp.exe");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>Una riga `tools/call` bell'e pronta.</summary>
        private static string Chiamata(int id, string nome, string argomenti)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":" +
                   "{\"name\":\"" + nome + "\",\"arguments\":" + argomenti + "}}";
        }

        /// <summary>
        /// Il testo di `content[0].text`, o la riga intera se non si riesce a
        /// scendere fin li'.
        ///
        /// SI TORNA LA RIGA INTERA E NON UNA STRINGA VUOTA: se la risposta ha
        /// una forma che non mi aspetto, voglio vederla nel dettaglio della
        /// prova rossa. Una stringa vuota direbbe «non c'era niente», che e'
        /// un'altra cosa.
        /// </summary>
        private static string TestoDiRisposta(string riga)
        {
            if (riga == null) return "(nessuna risposta)";
            var o = Mcp.Ogg(riga);
            var risultato = Mcp.Dentro(o, "result") as Dictionary<string, object>;
            var contenuto = Mcp.Dentro(risultato, "content") as object[];
            if (contenuto == null || contenuto.Length == 0) return riga;
            var t = Convert.ToString(Mcp.Dentro(contenuto[0], "text"));
            return string.IsNullOrEmpty(t) ? riga : t;
        }

        /// <summary>
        /// The volume in mm3 of the document (a part here), or 0.
        ///
        /// Z-C21.7 (S2 / L17-3): CreateMassProperty2, not the obsolete
        /// CreateMassProperty, with the recipe the Z-C21.3 probe
        /// probe_mass_property2 measured on a part: the selection cleared (a
        /// pre-selected body would be measured alone,
        /// IModelDocExtension~CreateMassProperty2.html), system units, hidden
        /// bodies included, Recalculate; with nothing selected that gave
        /// 21000 mm3 on its three-body part, the same as the obsolete call (N1 =
        /// M0). A part without solid bodies gives no object (the page: «a part
        /// with surface bodies only: Null»), hence 0 as before.
        /// </summary>
        private static double VolumeDiProva(IModelDoc2 doc)
        {
            try
            {
                doc.ClearSelection2(true);
                var m = doc.Extension.CreateMassProperty2() as IMassProperty2;
                if (m == null) return 0;
                m.UseSystemUnits = true;
                m.IncludeHiddenBodiesOrComponents = true;
                return m.Recalculate() ? m.Volume * 1e9 : 0;
            }
            catch { return 0; }
        }
    }
}
