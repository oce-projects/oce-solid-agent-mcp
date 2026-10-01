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
                UndoHonesty(p, eseguibile);
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
        /// `annulla` TELLS THE TRUTH (Z-C21.14), from the client's door.
        ///
        /// `annulla` lives in the bridge, not in the library, so its cases are
        /// here, where the bridge runs with a real document in front.
        /// Banco/prova_annulla.js has the first three refusals; it is a script
        /// run by hand, and these four are part of the full bench. Each is
        /// written against the description of `annulla` in Strumenti/Attrezzi.cs
        /// (it withdraws the last plan, whole, only while the part is as that
        /// plan left it; and the paragraph added in Z-C21.14) and read back
        /// through COM.
        ///
        ///  a. plan 1 builds a plate with two holes; plan 2 only reorders the
        ///     holes (`riordina_lavorazione`: same volume, same feature count,
        ///     its own undo entry on top). Plan 2 is the last plan, so `annulla`
        ///     must withdraw it: the reorder goes, every plan-1 feature stays.
        ///     RED ON BRIDGE 0.124.1, by the code path: plan 2 did not build, so
        ///     the memory `_ultimoPiano` was set to null there (`haCostruito`
        ///     false) and `annulla` answered that it had no plan to withdraw;
        ///     the answer of plan 2 had no `undo_entry_recorded` either. (Red on
        ///     the first version of this lot too: it refused before pressing.)
        ///  b. a plan on a saved part ends with `crea_disegno`, so the drawing
        ///     is in front; the part comes back with the tool `documento`,
        ///     action `attiva`; `annulla` must withdraw the plan from the part.
        ///     RED ON BRIDGE 0.124.1, by the code path, for the reason the live
        ///     probe M19 measured in its phase N: the state after the plan was
        ///     read on the window in front (Attrezzi.cs:2113, `_sessione.Stato`),
        ///     the drawing, whose volume and feature count did not change, so
        ///     `haCostruito` was false, no memory was written, and `annulla`
        ///     answered that it had no plan to withdraw («NOTHING TO WITHDRAW
        ///     (haCostruito false: no memory written)» in the probe); and the
        ///     answer of the plan named no document in front.
        ///  c. plan 1 builds a plate; a `misura` (`proprieta_di_massa`) follows;
        ///     `annulla` must withdraw plan 1, since a measure is not a plan and
        ///     writes nothing. This case DECIDES review F3: the measure's undo
        ///     recording is empty and SOLIDWORKS answers false for it (probe M19,
        ///     H4a/H4b); whether that leaves an entry on the stack is undecided,
        ///     and if it did, the press would take away that empty entry, the
        ///     part would read unchanged and the case would be red. RED ON
        ///     BRIDGE 0.124.1, by the code path: the measure did not build, the
        ///     memory was set to null, and the answer was that there was no plan.
        ///  d. two bridge processes on the same part (review F14): bridge 1
        ///     builds a plate with two holes, bridge 2 reorders the holes, bridge
        ///     1 calls `annulla`. The part no longer reads as bridge 1's plan
        ///     left it, so `annulla` must refuse BEFORE pressing, and the reorder
        ///     must still be there. RED ON BRIDGE 0.124.1, by the code path:
        ///     volume and count were as plan 1 left them, so it pressed, took
        ///     away bridge 2's reorder, which a redo does not put back (probe
        ///     `redo`), and answered that it had put the part back because
        ///     EditRedo2 did not throw; no `refused_before_pressing`. (Red on the
        ///     first version of this lot too: it pressed, and answered «NOT as it
        ///     was».)
        /// </summary>
        private static void UndoHonesty(Mcp.Ponte p, string bridgeExe)
        {
            IModelDoc2 partA = null, partB = null, partC = null, partD = null;
            Mcp.Ponte second = null;
            string drawingTitle = null, savedB = null;
            try
            {
                UndoWithdrawsTheLastPlanEvenIfItOnlyReordered(p, ref partA);
                UndoAfterAnotherWindowCameInFront(p, ref partB, ref savedB, ref drawingTitle);
                UndoAfterAMeasure(p, ref partC);
                UndoAfterAnotherBridgeReordered(p, bridgeExe, ref partD, ref second);
            }
            catch (Exception ex)
            {
                ErroreDiBanco("undo honesty", Perche(ex));
            }
            finally
            {
                if (second != null) { try { Mcp.Chiudi(second); second.Dispose(); } catch { } }
                // The drawing first: it holds a reference to the part.
                try { if (!string.IsNullOrEmpty(drawingTitle)) _sw.CloseDoc(drawingTitle); } catch { }
                Chiudi(partA);
                try { if (!string.IsNullOrEmpty(savedB)) _sw.CloseDoc(savedB); else Chiudi(partB); } catch { }
                RemoveQuietly(savedB);
                Chiudi(partC);
                Chiudi(partD);
            }
        }

        /// <summary>The plate with two through holes far apart, as a `disegna` plan.</summary>
        private const string PlateWithTwoHoles =
            "{\"passi\":[" +
            "{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":{\"piano\":\"superiore\"," +
            "\"lunghezza_mm\":120,\"larghezza_mm\":80,\"spessore_mm\":10,\"centrato\":true}}," +
            "{\"operazione\":\"taglio_cilindrico\",\"argomenti\":{\"faccia_rif\":{\"passo\":1," +
            "\"genere\":\"faccia_superiore\"},\"x_mm\":-30,\"y_mm\":0,\"diametro_mm\":10,\"passante\":true}}," +
            "{\"operazione\":\"taglio_cilindrico\",\"argomenti\":{\"faccia_rif\":{\"passo\":1," +
            "\"genere\":\"faccia_superiore\"},\"x_mm\":30,\"y_mm\":0,\"diametro_mm\":10,\"passante\":true}}]}";

        /// <summary>The `disegna` plan that puts the second hole before the first.</summary>
        private static string ReorderPlan(List<string> cuts)
        {
            return "{\"passi\":[{\"operazione\":\"riordina_lavorazione\",\"argomenti\":{\"feature_uid\":\"" + cuts[1] +
                   "\",\"rispetto_a_uid\":\"" + cuts[0] + "\",\"dove\":\"prima\"}}]}";
        }

        /// <summary>
        /// The cuts a plan added, read through COM, in tree order. GetTypeName2
        /// answers «ICE» (Instant3D) for every extrusion after the base, cut or
        /// boss (measured 28/9/2026), so «Cut» alone found none of the two holes
        /// of the cylindrical cuts and the first bench of these cases was red on
        /// its own reading (1/10/2026). The
        /// plans here add only cuts after the base, and the callers also check
        /// the volume.
        /// </summary>
        private static List<string> NewCuts(List<Tuple<string, string>> before, List<Tuple<string, string>> after)
        {
            var cuts = new List<string>();
            foreach (var f in after)
                if ((f.Item2 == "Cut" || f.Item2 == "ICE") && !before.Exists(b => b.Item1 == f.Item1)) cuts.Add(f.Item1);
            return cuts;
        }

        /// <summary>True when <paramref name="first"/> comes before <paramref name="second"/> in the tree.</summary>
        private static bool Before(List<Tuple<string, string>> features, string first, string second)
        {
            int a = IndexOf(features, first), b = IndexOf(features, second);
            return a >= 0 && b >= 0 && a < b;
        }

        /// <summary>Case a of <see cref="UndoHonesty"/>.</summary>
        private static void UndoWithdrawsTheLastPlanEvenIfItOnlyReordered(Mcp.Ponte p, ref IModelDoc2 part)
        {
            part = NuovaParte();
            var look = TestoDiRisposta(Mcp.Chiedi(p, 40, Chiamata(40, "guarda_il_pezzo", "{}")));
            if (Mcp.Ogg(look) == null)
            {
                ErroreDiBanco("undo honesty, case a", "guarda_il_pezzo gave no state: " + Corto(look));
                return;
            }

            var before = FeaturesOf(part);
            var plan1 = TestoDiRisposta(Mcp.Chiedi(p, 41, Chiamata(41, "disegna", PlateWithTwoHoles)));
            var afterPlan1 = FeaturesOf(part);
            var cuts = NewCuts(before, afterPlan1);
            Segna("undo case a: plan 1 builds a plate with two holes, read by the bench through COM",
                  cuts.Count == 2 && VolumeDiProva(part) > 1, "holes «" + string.Join("», «", cuts) + "», volume " +
                  VolumeDiProva(part).ToString("0.#") + " mm3 — " + Corto(plan1));
            if (cuts.Count != 2) return;

            // Plan 2: the second hole before the first. Same volume, same count.
            double volumeAfterPlan1 = VolumeDiProva(part);
            var plan2Text = TestoDiRisposta(Mcp.Chiedi(p, 42, Chiamata(42, "disegna", ReorderPlan(cuts))));
            var plan2 = Mcp.Ogg(plan2Text);
            bool reordered = Before(FeaturesOf(part), cuts[1], cuts[0]);
            Segna("undo case a: plan 2 reorders the two holes, read by the bench through COM",
                  reordered && Math.Abs(VolumeDiProva(part) - volumeAfterPlan1) < 0.01, Corto(plan2Text));
            if (!reordered) return;

            // Measured on the bench of 1/10/2026: a reorder does not land in the
            // undo entry the executor records (FinishRecordingUndoObject answers
            // False), so nobody knows what sits on top of the undo stack. The
            // safe answer is to refuse without pressing, and say why.
            object recorded = Mcp.Dentro(plan2, "undo_entry_recorded");
            Segna("undo case a: plan 2 says it left no undo entry of its own (undo_entry_recorded false)",
                  recorded is bool && !(bool)recorded,
                  "undo_entry_recorded = " + (recorded == null ? "(absent)" : Convert.ToString(recorded)));

            var undoText = TestoDiRisposta(Mcp.Chiedi(p, 43, Chiamata(43, "annulla", "{}")));
            var undo = Mcp.Ogg(undoText);
            object done = Mcp.Dentro(undo, "annullato");
            Segna("undo case a: `annulla` refuses without pressing: the top of the undo stack is unknown",
                  !(done is bool && (bool)done), Corto(undoText));

            var afterUndo = FeaturesOf(part);
            Segna("undo case a: and the reorder is still there, read by the bench through COM",
                  Before(afterUndo, cuts[1], cuts[0]),
                  "positions " + IndexOf(afterUndo, cuts[0]) + " and " + IndexOf(afterUndo, cuts[1]));
            bool allThere = true;
            foreach (var f in afterPlan1) if (IndexOf(afterUndo, f.Item1) < 0) allThere = false;
            Segna("undo case a: and every plan-1 feature is still there, with the same volume",
                  allThere && Math.Abs(VolumeDiProva(part) - volumeAfterPlan1) < 0.01,
                  afterUndo.Count + " features (" + afterPlan1.Count + " after plan 1), volume " +
                  volumeAfterPlan1.ToString("0.#") + " -> " + VolumeDiProva(part).ToString("0.#") + " mm3");
        }

        /// <summary>Case b of <see cref="UndoHonesty"/>.</summary>
        private static void UndoAfterAnotherWindowCameInFront(
            Mcp.Ponte p, ref IModelDoc2 part, ref string saved, ref string drawingTitle)
        {
            // crea_disegno wants a part saved on disk.
            part = NuovaParte();
            string path = Path.Combine(Attese.Lavoro, "client_door_undo_drawing.sldprt");
            RemoveQuietly(path);
            int e = 0, w = 0;
            // SaveAs3, not the obsolete SaveAs frozen for Z-C21.7 (review F13).
            part.Extension.SaveAs3(path, 0, 1, null, null, ref e, ref w);
            if (!File.Exists(path))
            {
                ErroreDiBanco("undo honesty, case b", "could not save «" + path + "» (e=" + e + ", w=" + w + ")");
                return;
            }
            saved = path;
            string json = path.Replace("\\", "\\\\");

            var look = TestoDiRisposta(Mcp.Chiedi(p, 50, Chiamata(50, "guarda_il_pezzo", "{}")));
            if (Mcp.Ogg(look) == null)
            {
                ErroreDiBanco("undo honesty, case b", "guarda_il_pezzo gave no state: " + Corto(look));
                return;
            }

            var planText = TestoDiRisposta(Mcp.Chiedi(p, 51, Chiamata(51, "disegna",
                "{\"passi\":[" +
                "{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":{\"piano\":\"superiore\"," +
                "\"lunghezza_mm\":100,\"larghezza_mm\":60,\"spessore_mm\":10,\"centrato\":true}}," +
                "{\"operazione\":\"crea_disegno\",\"argomenti\":{}}]}")));
            var plan = Mcp.Ogg(planText);
            IModelDoc2 front = _sw.IActiveDoc2;
            bool drawingInFront = front != null &&
                front.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocDRAWING;
            if (drawingInFront) drawingTitle = front.GetTitle();
            Segna("undo case b: the plan ends with its drawing in front, read by the bench through COM",
                  drawingInFront, "in front «" + (front == null ? "(nothing)" : front.GetTitle()) + "» — " + Corto(planText));
            if (!drawingInFront) return;

            string named = Convert.ToString(Mcp.Dentro(plan, "document_in_front")) ?? "";
            Segna("undo case b: the answer of `disegna` says which document is in front",
                  named.Length > 0 && named.IndexOf(drawingTitle, StringComparison.OrdinalIgnoreCase) >= 0,
                  "document_in_front «" + named + "», the bench sees «" + drawingTitle + "»");
            object volumeAfter = Mcp.Dentro(plan, "volume_dopo_mm3");
            double volumeAnswered;
            bool readable = volumeAfter != null &&
                double.TryParse(Convert.ToString(volumeAfter, System.Globalization.CultureInfo.InvariantCulture),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out volumeAnswered) &&
                Math.Abs(volumeAnswered - 60000) < 1;
            // 100 x 60 x 10 = 60000 mm3, and the part started empty.
            Segna("undo case b: and its volume_dopo_mm3 is the part's (60000 mm3), not the drawing's",
                  readable, "volume_dopo_mm3 = " + (volumeAfter == null ? "(absent)" : Convert.ToString(volumeAfter)));

            string extrusion = null;
            foreach (var f in FeaturesOf(part)) if (f.Item2 == "Extrusion") extrusion = f.Item1;

            var back = TestoDiRisposta(Mcp.Chiedi(p, 52, Chiamata(52, "documento",
                "{\"azione\":\"attiva\",\"percorso\":\"" + json + "\"}")));
            IModelDoc2 nowInFront = _sw.IActiveDoc2;
            bool partInFront = nowInFront != null &&
                string.Equals(nowInFront.GetPathName(), path, StringComparison.OrdinalIgnoreCase);
            Segna("undo case b: `documento` with `attiva` brings the part back to the front, read by the bench through COM",
                  partInFront, "in front «" + (nowInFront == null ? "(nothing)" : nowInFront.GetPathName()) + "» — " + Corto(back));
            if (!partInFront) return;

            var undoText = TestoDiRisposta(Mcp.Chiedi(p, 53, Chiamata(53, "annulla", "{}")));
            var undo = Mcp.Ogg(undoText);
            object done = Mcp.Dentro(undo, "annullato");
            Segna("undo case b: `annulla` withdraws the plan from the part, the drawing having been in front",
                  done is bool && (bool)done, Corto(undoText));
            var left = FeaturesOf(part);
            Segna("undo case b: and the bench reads the part empty again: the extrusion is gone",
                  extrusion != null && IndexOf(left, extrusion) < 0 && VolumeDiProva(part) < 0.001,
                  "extrusion «" + (extrusion ?? "(not found before)") + "» at position " +
                  (extrusion == null ? -1 : IndexOf(left, extrusion)) + ", volume " +
                  VolumeDiProva(part).ToString("0.###") + " mm3");
        }

        /// <summary>Case c of <see cref="UndoHonesty"/>.</summary>
        private static void UndoAfterAMeasure(Mcp.Ponte p, ref IModelDoc2 part)
        {
            part = NuovaParte();
            var look = TestoDiRisposta(Mcp.Chiedi(p, 70, Chiamata(70, "guarda_il_pezzo", "{}")));
            if (Mcp.Ogg(look) == null)
            {
                ErroreDiBanco("undo honesty, case c", "guarda_il_pezzo gave no state: " + Corto(look));
                return;
            }

            var plan = TestoDiRisposta(Mcp.Chiedi(p, 71, Chiamata(71, "disegna",
                "{\"passi\":[{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":{\"piano\":\"superiore\"," +
                "\"lunghezza_mm\":80,\"larghezza_mm\":50,\"spessore_mm\":10,\"centrato\":true}}]}")));
            string extrusion = null;
            foreach (var f in FeaturesOf(part)) if (f.Item2 == "Extrusion") extrusion = f.Item1;
            // 80 x 50 x 10 = 40000 mm3, and the part started empty.
            Segna("undo case c: the plan builds a plate, read by the bench through COM",
                  extrusion != null && Math.Abs(VolumeDiProva(part) - 40000) < 1,
                  "extrusion «" + (extrusion ?? "(none)") + "», volume " + VolumeDiProva(part).ToString("0.#") +
                  " mm3 — " + Corto(plan));
            if (extrusion == null) return;

            var measureText = TestoDiRisposta(Mcp.Chiedi(p, 72, Chiamata(72, "misura",
                "{\"passi\":[{\"operazione\":\"proprieta_di_massa\",\"argomenti\":{}}]}")));
            object measureEntry = Mcp.Dentro(Mcp.Ogg(measureText), "undo_entry_recorded");

            var undoText = TestoDiRisposta(Mcp.Chiedi(p, 73, Chiamata(73, "annulla", "{}")));
            object done = Mcp.Dentro(Mcp.Ogg(undoText), "annullato");
            Segna("undo case c: after a measure, `annulla` still withdraws the plan",
                  done is bool && (bool)done,
                  "the measure answered undo_entry_recorded = " +
                  (measureEntry == null ? "(absent)" : Convert.ToString(measureEntry)) + " — " + Corto(undoText));
            Segna("undo case c: and the bench reads the part empty again: the extrusion is gone",
                  IndexOf(FeaturesOf(part), extrusion) < 0 && VolumeDiProva(part) < 0.001,
                  "extrusion at position " + IndexOf(FeaturesOf(part), extrusion) + ", volume " +
                  VolumeDiProva(part).ToString("0.###") + " mm3");
        }

        /// <summary>Case d of <see cref="UndoHonesty"/>.</summary>
        private static void UndoAfterAnotherBridgeReordered(
            Mcp.Ponte p, string bridgeExe, ref IModelDoc2 part, ref Mcp.Ponte second)
        {
            part = NuovaParte();
            var look = TestoDiRisposta(Mcp.Chiedi(p, 60, Chiamata(60, "guarda_il_pezzo", "{}")));
            if (Mcp.Ogg(look) == null)
            {
                ErroreDiBanco("undo honesty, case d", "guarda_il_pezzo gave no state: " + Corto(look));
                return;
            }
            var before = FeaturesOf(part);
            var plan1 = TestoDiRisposta(Mcp.Chiedi(p, 61, Chiamata(61, "disegna", PlateWithTwoHoles)));
            var afterPlan1 = FeaturesOf(part);
            var cuts = NewCuts(before, afterPlan1);
            Segna("undo case d: bridge 1 builds a plate with two holes, read by the bench through COM",
                  cuts.Count == 2, "holes «" + string.Join("», «", cuts) + "» — " + Corto(plan1));
            if (cuts.Count != 2) return;
            double volumeAfterPlan1 = VolumeDiProva(part);

            // A second bridge process, on the same SOLIDWORKS and the same part.
            second = bridgeExe == null ? null : Mcp.Apri(bridgeExe);
            var hello = second == null ? null : Mcp.Chiedi(second, 1,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                "\"clientInfo\":{\"name\":\"banco-2\",\"version\":\"1\"}}}");
            if (hello == null)
            {
                ErroreDiBanco("undo honesty, case d", "the second bridge did not start or did not answer initialize");
                return;
            }
            var look2 = TestoDiRisposta(Mcp.Chiedi(second, 2, Chiamata(2, "guarda_il_pezzo", "{}")));
            var reorderText = TestoDiRisposta(Mcp.Chiedi(second, 3, Chiamata(3, "disegna", ReorderPlan(cuts))));
            bool reordered = Before(FeaturesOf(part), cuts[1], cuts[0]);
            Segna("undo case d: bridge 2 reorders the two holes, read by the bench through COM",
                  reordered, Corto(reorderText) + " (it looked: " + Corto(look2) + ")");
            if (!reordered) return;

            var undoText = TestoDiRisposta(Mcp.Chiedi(p, 62, Chiamata(62, "annulla", "{}")));
            var undo = Mcp.Ogg(undoText);
            object refused = Mcp.Dentro(undo, "refused_before_pressing");
            object done = Mcp.Dentro(undo, "annullato");
            Segna("undo case d: bridge 1's `annulla` refuses BEFORE pressing: the part is not as its plan left it",
                  refused is bool && (bool)refused && done is bool && !(bool)done &&
                  (Convert.ToString(Mcp.Dentro(undo, "motivo")) ?? "").IndexOf("order", StringComparison.OrdinalIgnoreCase) >= 0,
                  Corto(undoText));

            var afterUndo = FeaturesOf(part);
            bool allThere = true;
            foreach (var f in afterPlan1) if (IndexOf(afterUndo, f.Item1) < 0) allThere = false;
            Segna("undo case d: and bridge 2's reorder and every plan-1 feature are still there, read by the bench through COM",
                  Before(afterUndo, cuts[1], cuts[0]) && allThere &&
                  Math.Abs(VolumeDiProva(part) - volumeAfterPlan1) < 0.01,
                  "positions " + IndexOf(afterUndo, cuts[1]) + " and " + IndexOf(afterUndo, cuts[0]) + ", " +
                  afterUndo.Count + " features, volume " + VolumeDiProva(part).ToString("0.#") + " mm3");
        }

        /// <summary>The features of a document as (name, type), in tree order.</summary>
        private static List<Tuple<string, string>> FeaturesOf(IModelDoc2 doc)
        {
            var found = new List<Tuple<string, string>>();
            if (doc == null) return found;
            IFeature f = doc.FirstFeature() as IFeature;
            for (int i = 0; f != null && i < 2000; i++)
            {
                string name = "", type = "";
                try { name = f.Name ?? ""; } catch { }
                try { type = f.GetTypeName2() ?? ""; } catch { }
                found.Add(Tuple.Create(name, type));
                f = f.GetNextFeature() as IFeature;
            }
            return found;
        }

        /// <summary>The position of a feature by name, -1 when it is not there.</summary>
        private static int IndexOf(List<Tuple<string, string>> features, string name)
        {
            return features.FindIndex(f => f.Item1 == name);
        }

        /// <summary>Deletes a bench file if it is there, read-only or not; quiet on failure.</summary>
        private static void RemoveQuietly(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists) return;
                file.Attributes = FileAttributes.Normal;
                file.Delete();
            }
            catch { }
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
