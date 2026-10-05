using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
// ALIAS E NON `using static`: il banco ha gia' un `Open` e un `Close` suoi,
// e i membri della classe vincono su quelli importati. Con l'alias si vede a
// colpo d'occhio quali chiamate escono da questo processo e vanno al ponte.
using Mcp = Shared.McpClient;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// UN GIRO DI PROVE CHE PASSA DALLA PORTA DEL CLIENTE (voce F14).
    ///
    /// ── PERCHE' ESISTE, E NON E' UNA SIMMETRIA ESTETICA ──────────────────
    ///
    /// Tutto il resto di questo banco chiama la LIBRERIA. Il 18/9/2026 il
    /// ponte rifiutava ogni assieme mentre il suo catalogo ne prometteva una
    /// famiglia intera, e il banco aveva 781 prove verdi — cinque delle quali
    /// sugli assiemi — e **nessuna poteva vederlo**: `PlanExecutor` non
    /// compare in nessun file di `Bench/`.
    ///
    /// *Due misure vere di due cose diverse, e chi legge il numero grande crede
    /// che copra anche l'altra.*
    ///
    /// ── COSA PROVA CHE NIENT'ALTRO PROVA ─────────────────────────────────
    ///
    /// Tutto cio' che sta FRA il cliente e la libreria, con un documento vero
    /// davanti: i controlli preliminari, la traduzione fra JSON e argomenti, i
    /// rifiuti su operazioni che il documento aperto non ammette, e
    /// `inspect_part`, che non passa nemmeno dall'esecutore.
    ///
    /// `Oce.Solid.Agent.ContractTests` parla gia' al ponte sullo stdio, ma **senza
    /// SOLIDWORKS**: li' ogni piano muore su «non c'e' nessun documento
    /// aperto», che e' un rifiuto vero e non prova niente sul resto. Qui il
    /// documento c'e'. Il trasporto e' lo stesso — `Shared/McpClient.cs` —
    /// perche' due trasporti sarebbero due posti in cui riparare il BOM.
    ///
    /// ── E UNO DEVE ESSERE ROSSO PER COSTRUZIONE ──────────────────────────
    ///
    /// Un piano che chiede un'operazione di `assemblies` con una PARTE aperta
    /// dev'essere rifiutato, e la risposta deve dire **su cosa si sta
    /// lavorando** — non una frase fissa. Un controllo mai visto scattare non
    /// e' un controllo, e un rifiuto che non nomina il documento non si
    /// distingue da uno che rifiuta tutto.
    /// </summary>
    internal static partial class Program
    {
        private static void TheClientPort()
        {
            var executable = TheBridge();
            if (executable == null)
            {
                BenchError("la porta del cliente",
                              "non trovo bin\\oce-solid-agent-mcp.exe — si produce con .\\build.ps1");
                return;
            }

            // IL DOCUMENTO LO PREPARA IL BANCO, e il ponte lo trova davanti.
            // E' esattamente la differenza con `Oce.Solid.Agent.ContractTests`: li' non c'e'
            // niente aperto, e ogni piano muore sul primo controllo.
            IModelDoc2 doc = null;
            Mcp.Bridge p = null;
            string newTitle = null, saved = null;
            try
            {
                doc = NewPart();
                string docTitle = doc.GetTitle();

                p = Mcp.Open(executable);
                if (p == null)
                {
                    BenchError("la porta del cliente", "il ponte non e' partito");
                    return;
                }

                // ── initialize ───────────────────────────────────────────
                var r = Mcp.Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"banco\",\"version\":\"1\"}}}");
                Check("porta: initialize risponde", r != null,
                      r != null ? "" : "niente entro " + (Mcp.TimeoutMs / 1000) + "s. Righe viste: " +
                                       string.Join(" | ", p.Views.ToArray()));
                if (r == null) return;

                // ── 1. inspect_part VEDE IL DOCUMENTO CHE HA APERTO IL BANCO ──
                //
                // Non «risponde qualcosa»: risponde di QUESTO pezzo. E' la
                // prova che il ponte e il banco stanno guardando la stessa
                // sessione di SOLIDWORKS, ed e' la premessa di tutto il resto.
                var text = ResponseText(Mcp.Ask(p, 2, Call(2, "inspect_part", "{}")));
                Check("porta: inspect_part nomina il documento del banco",
                      text.IndexOf(docTitle, StringComparison.OrdinalIgnoreCase) >= 0,
                      docTitle + " — " + Short(text));

                // ── 2. UN PIANO CHE RIESCE, E IL BANCO RILEGGE IL PEZZO ──
                //
                // La risposta del ponte dice «succeeded». Il banco non si fida
                // della parola: apre lo stesso documento con la COM e misura
                // il volume. E' l'unica cosa che questo gruppo puo' fare e la
                // suite di contratto no.
                double volumeBefore = TestVolume(doc);
                var done = ResponseText(Mcp.Ask(p, 3, Call(3, "build",
                    "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":" +
                    "{\"length_mm\":120,\"width_mm\":80,\"thickness_mm\":10}}]}")));
                double volumeAfter = TestVolume(doc);

                Check("porta: il piano riesce", done.IndexOf("\"succeeded\":true",
                          StringComparison.OrdinalIgnoreCase) >= 0 ||
                      done.IndexOf("\"succeeded\": true", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(done));

                // 120 x 80 x 10 = 96.000 mm3, e il pezzo partiva vuoto.
                Check("porta: e il pezzo e' cambiato davvero, misurato dal banco",
                      Math.Abs(volumeAfter - volumeBefore - 96000) < 1,
                      volumeBefore.ToString("0.#") + " -> " + volumeAfter.ToString("0.#") +
                      " mm3 (atteso +96000)");

                // ── 3. ROSSO PER COSTRUZIONE: un'operazione di assieme su una parte ──
                var refusal = ResponseText(Mcp.Ask(p, 4, Call(4, "build",
                    "{\"steps\":[{\"operation\":\"insert_component\",\"arguments\":" +
                    "{\"path\":\"C:\\\\non\\\\esiste.SLDPRT\"}}]}")));

                // SOLO `succeeded: false`, e niente `||` di scorta. La prima
                // stesura accettava anche una risposta che contenesse la
                // parola «assembly» da qualche parte: sarebbe passata pure
                // con un piano RIUSCITO che la nominasse. *Un `||` messo per
                // sicurezza e' un `||` che un giorno regge da solo.*
                bool refused = refusal.IndexOf("\"succeeded\":false",
                                     StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 refusal.IndexOf("\"succeeded\": false",
                                     StringComparison.OrdinalIgnoreCase) >= 0;
                Check("porta: un'operazione di assieme su una PARTE e' rifiutata",
                      refused, Short(refusal));

                // E NON DEVE ESSERE RIUSCITA. Sembra la stessa cosa detta
                // due volte e non lo e': `succeeded` potrebbe mancare del
                // tutto — una risposta malformata, un errore di protocollo —
                // e allora la riga qui sopra sarebbe rossa per il motivo
                // giusto ma per la ragione sbagliata. Questa dice che la
                // parola «true» accanto a «succeeded» non c'e'.
                Check("porta: e non e' riuscita per sbaglio",
                      refusal.IndexOf("\"succeeded\":true",
                          StringComparison.OrdinalIgnoreCase) < 0 &&
                      refusal.IndexOf("\"succeeded\": true",
                          StringComparison.OrdinalIgnoreCase) < 0,
                      Short(refusal));

                // E IL RIFIUTO DEVE DIRE SU COSA SI STA LAVORANDO.
                //
                // «Questa operazione vuole un assieme» e' una frase fissa: la
                // stessa risposta arriverebbe con qualunque documento davanti,
                // compreso quello sbagliato. Nominare il documento e' cio' che
                // distingue un controllo che guarda da uno che recita.
                // IL TITOLO, NON LA PAROLA «part». «part» sta in qualunque
                // frase fissa sui tipi di documento, e accettarla vorrebbe
                // dire promuovere proprio la risposta che questa riga esiste
                // per bocciare.
                Check("porta: e il rifiuto dice su cosa sta lavorando",
                      refusal.IndexOf(docTitle, StringComparison.OrdinalIgnoreCase) >= 0,
                      "cerco «" + docTitle + "» in: " + Short(refusal));

                // ── LA TRAPPOLA: zero righe lette aspettando zero righe ──
                //
                // Una notifica non riceve risposta, e «non e' arrivato niente»
                // e' vero anche quando il ponte e' morto. Si manda la notifica
                // e SUBITO DOPO una domanda vera: se la prima riga che torna e'
                // la risposta a quella, la notifica non ha risposto E il ponte
                // e' vivo.
                Mcp.Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                var afterTheNotification = Mcp.Ask(p, 5, Call(5, "inspect_part", "{}"));
                Check("porta: il ponte e' ancora vivo dopo una notifica",
                      afterTheNotification != null,
                      afterTheNotification != null ? "" : "niente risposta alla domanda dopo la notifica");

                TheModal(p, doc);
                DocumentCycle(p, docTitle, ref newTitle, ref saved);
                UndoHonesty(p, executable);
            }
            catch (Exception ex)
            {
                BenchError("la porta del cliente", Why(ex));
            }
            finally
            {
                if (p != null) { Mcp.Close(p); p.Dispose(); }
                Close(doc);
                // Quello che il ciclo del documento ha aperto o creato si chiude
                // qui, qualunque cosa sia successa: la scrivania del banco deve
                // restare pulita (voce K12), o il giro dopo rifiuta di partire.
                try { if (!string.IsNullOrEmpty(newTitle)) _sw.CloseDoc(newTitle); } catch { }
                try
                {
                    if (!string.IsNullOrEmpty(saved))
                    {
                        _sw.CloseDoc(Path.GetFileName(saved));
                        if (File.Exists(saved))
                        {
                            File.SetAttributes(saved, FileAttributes.Normal);
                            File.Delete(saved);
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// LO STRUMENTO «document», DALLA PORTA DEL CLIENTE (voci Z-B1, Z-B2,
        /// Z-B5, Z-B6).
        ///
        /// Il ponte ha smesso di aver bisogno di una persona per cominciare:
        /// crea, apre, elenca, porta davanti. Qui si guarda che lo faccia DAVVERO
        /// — il banco rilegge dalla COM quale documento c'e' davanti, invece di
        /// credere alla risposta — e due rossi per costruzione: un file che non
        /// esiste non cambia il documento davanti, e dopo un cambio di documento
        /// «build» si rifiuta finche' non si e' guardato di nuovo.
        /// </summary>
        private static void DocumentCycle(Mcp.Bridge p, string docTitle,
                                                ref string newTitle, ref string saved)
        {
            Func<string> front = () =>
            {
                try { var d = _sw.IActiveDoc2; return d == null ? "(niente)" : d.GetTitle(); }
                catch { return "(non leggibile)"; }
            };

            // ── elenca: c'e' il documento del banco, ed e' davanti ─────────
            var list = ResponseText(Mcp.Ask(p, 6, Call(6, "document", "{\"action\":\"list\"}")));
            Check("documento: «list» nomina il documento del banco",
                  list.IndexOf(docTitle, StringComparison.OrdinalIgnoreCase) >= 0, Short(list));

            // ── nuovo: una parte vuota, davanti, e il banco lo verifica ────
            var newItem = Mcp.Obj(ResponseText(Mcp.Ask(p, 7, Call(7, "document",
                "{\"action\":\"create\",\"variant\":\"part\"}"))));
            newTitle = Convert.ToString(Mcp.Inside(newItem, "document_title"));
            var active = _sw.IActiveDoc2;
            Check("documento: «create» crea una parte e la porta davanti, riletto dalla COM",
                  !string.IsNullOrEmpty(newTitle) && active != null &&
                  active.GetTitle() == newTitle &&
                  active.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART,
                  "risposta «" + newTitle + "», davanti «" + front() + "»");
            Check("documento: e la parte nuova e' vuota",
                  active != null && TestVolume(active) < 0.001,
                  active == null ? "(niente davanti)" : TestVolume(active).ToString("0.###") + " mm3");

            // ── ROSSO PER COSTRUZIONE: disegna subito dopo, senza guardare ─
            //
            // Il documento davanti e' cambiato per mano del ponte stesso. Quello
            // che era stato guardato non vale piu', e «build» deve dirlo invece
            // di scrivere in un pezzo che il modello non ha mai visto.
            var immediately = ResponseText(Mcp.Ask(p, 8, Call(8, "build",
                "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":" +
                "{\"length_mm\":10,\"width_mm\":10,\"thickness_mm\":10}}]}")));
            Check("documento: dopo un cambio di documento «build» pretende di guardare prima",
                  immediately.IndexOf("non ho ancora guardato", StringComparison.OrdinalIgnoreCase) >= 0 &&
                  (active == null || TestVolume(active) < 0.001),
                  Short(immediately));

            // ── attiva: si torna al documento del banco ───────────────────
            var returns = ResponseText(Mcp.Ask(p, 9, Call(9, "document",
                "{\"action\":\"activate\",\"path\":\"" + docTitle + "\"}")));
            Check("documento: «activate» riporta davanti il documento del banco",
                  front() == docTitle, "davanti «" + front() + "» — " + Short(returns));

            // ── ROSSO: un file che non esiste non cambia niente ───────────
            var nonexistent = Path.Combine(Expectations.Work, "non-esiste-" + Guid.NewGuid().ToString("N") + ".sldprt");
            var no = ResponseText(Mcp.Ask(p, 10, Call(10, "document",
                "{\"action\":\"open\",\"path\":\"" + nonexistent.Replace("\\", "\\\\") + "\"}")));
            Check("documento: «open» su un file che non esiste rifiuta, e davanti resta quello di prima",
                  no.IndexOf("non esiste", StringComparison.OrdinalIgnoreCase) >= 0 && front() == docTitle,
                  "davanti «" + front() + "» — " + Short(no));

            // ── apri un file vero: si salva la parte nuova, la si chiude, la si
            //    riapre dal ponte, e il percorso deve essere quello chiesto ─
            saved = Path.Combine(Expectations.Work, "porta_documento.sldprt");
            try { if (File.Exists(saved)) { File.SetAttributes(saved, FileAttributes.Normal); File.Delete(saved); } } catch { }
            // IL RIFERIMENTO PRESO DOPO «create», non una ricerca per nome.
            // `GetOpenDocumentByName` con il titolo di un documento MAI SALVATO
            // torna null — misurato: la prima stesura salvava cosi', riceveva
            // e=0 w=0 da una chiamata mai fatta, e il file non c'era.
            var toSave = active;
            int e = 0, w = 0;
            if (toSave != null) toSave.Extension.SaveAs(saved, 0, 1, null, ref e, ref w);
            if (toSave == null || !File.Exists(saved))
            {
                BenchError("documento: apri", "non sono riuscito a salvare «" + saved + "» (e=" + e + ", w=" + w +
                              (toSave == null ? ", nessun documento in mano" : "") + ")");
                return;
            }
            _sw.CloseDoc(toSave.GetTitle());
            newTitle = null;

            var open = Mcp.Obj(ResponseText(Mcp.Ask(p, 11, Call(11, "document",
                "{\"action\":\"open\",\"path\":\"" + saved.Replace("\\", "\\\\") + "\"}"))));
            var openPath = Convert.ToString(Mcp.Inside(open, "path"));
            var att = _sw.IActiveDoc2;
            Check("documento: «open» apre il file chiesto e lo porta davanti, riletto dalla COM",
                  string.Equals(openPath, saved, StringComparison.OrdinalIgnoreCase) &&
                  att != null && string.Equals(att.GetPathName(), saved, StringComparison.OrdinalIgnoreCase),
                  "risposta «" + openPath + "», davanti «" + (att == null ? "(niente)" : att.GetPathName()) + "»");
            Check("documento: e un file scrivibile non e' dichiarato in sola lettura",
                  Convert.ToString(Mcp.Inside(open, "read_only")).Equals("False", StringComparison.OrdinalIgnoreCase),
                  Convert.ToString(Mcp.Inside(open, "read_only")));

            // ── Z-B2: lo stesso file con l'attributo di sola lettura ───────
            //
            // Il caso vero e' un collega che ha il file aperto su una cartella di
            // rete; dentro una sola sessione di SOLIDWORKS non si costruisce. Il
            // rosso economico e' l'attributo del file: SOLIDWORKS lo apre lo
            // stesso, in sola lettura, SENZA chiedere niente — che e' esattamente
            // il silenzio che la voce vuole vedere detto.
            _sw.CloseDoc(Path.GetFileName(saved));
            File.SetAttributes(saved, FileAttributes.ReadOnly);
            var readOnlyMode = Mcp.Obj(ResponseText(Mcp.Ask(p, 12, Call(12, "document",
                "{\"action\":\"open\",\"path\":\"" + saved.Replace("\\", "\\\\") + "\"}"))));
            Check("documento: un file in sola lettura si apre e il ponte LO DICE",
                  Convert.ToString(Mcp.Inside(readOnlyMode, "read_only")).Equals("True", StringComparison.OrdinalIgnoreCase) &&
                  Mcp.Inside(readOnlyMode, "warning") != null,
                  Short(Convert.ToString(Mcp.Inside(readOnlyMode, "warning")) + " | read_only=" +
                        Convert.ToString(Mcp.Inside(readOnlyMode, "read_only"))));

            // ── chiudi (voce Z-B1): il ROSSO prima, su un documento sporco ──
            //
            // Il documento del banco ha l'estrusione del passo 2 e non e' mai
            // stato salvato: chiuderlo butterebbe lavoro. Il ponte deve
            // rifiutare, e il banco rilegge dalla COM che sia ancora aperto.
            Func<string, bool> open2 = t =>
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                for (int i = 0; d != null && i < 500; i++)
                {
                    if (string.Equals(d.GetTitle(), t, StringComparison.OrdinalIgnoreCase)) return true;
                    d = d.GetNext() as IModelDoc2;
                }
                return false;
            };
            var dirty = ResponseText(Mcp.Ask(p, 13, Call(13, "document",
                "{\"action\":\"close\",\"path\":\"" + docTitle + "\"}")));
            Check("documento: «close» su un documento con modifiche non salvate rifiuta, e resta aperto",
                  dirty.IndexOf("modifiche non salvate", StringComparison.OrdinalIgnoreCase) >= 0 && open2(docTitle),
                  "ancora aperto " + open2(docTitle) + " — " + Short(dirty));

            // ── e la GEMELLA: un documento appena aperto, senza modifiche ──
            // Il titolo porta l'estensione o no a seconda di come Windows mostra
            // i file: si confronta il gambo del nome.
            string shank = Path.GetFileNameWithoutExtension(saved);
            var closed = Mcp.Obj(ResponseText(Mcp.Ask(p, 14, Call(14, "document",
                "{\"action\":\"close\",\"path\":\"" + saved.Replace("\\", "\\\\") + "\"}"))));
            bool disappeared = _sw.GetOpenDocumentByName(saved) == null;
            Check("documento: «close» su un documento senza modifiche lo chiude, riletto dalla COM",
                  disappeared && Convert.ToString(Mcp.Inside(closed, "closed"))
                                 .StartsWith(shank, StringComparison.OrdinalIgnoreCase),
                  "sparito dalla COM " + disappeared + " — chiuso «" + Convert.ToString(Mcp.Inside(closed, "closed")) + "»");
        }

        /// <summary>
        /// `undo` TELLS THE TRUTH (Z-C21.14), from the client's door.
        ///
        /// `undo` lives in the bridge, not in the library, so its cases are
        /// here, where the bridge runs with a real document in front.
        /// Bench/test_undo.js has the first three refusals; it is a script
        /// run by hand, and these four are part of the full bench. Each is
        /// written against the description of `undo` in Tools/McpTools.cs
        /// (it withdraws the last plan, whole, only while the part is as that
        /// plan left it; and the paragraph added in Z-C21.14) and read back
        /// through COM.
        ///
        ///  a. plan 1 builds a plate with two holes; plan 2 only reorders the
        ///     holes (`reorder_feature`: same volume, same feature count,
        ///     its own undo entry on top). Plan 2 is the last plan, so `undo`
        ///     must withdraw it: the reorder goes, every plan-1 feature stays.
        ///     RED ON BRIDGE 0.124.1, by the code path: plan 2 did not build, so
        ///     the memory `_lastPlan` was set to null there (`hasBuilt`
        ///     false) and `undo` answered that it had no plan to withdraw;
        ///     the answer of plan 2 had no `undo_entry_recorded` either. (Red on
        ///     the first version of this lot too: it refused before pressing.)
        ///  b. a plan on a saved part ends with `create_drawing`, so the drawing
        ///     is in front; the part comes back with the tool `document`,
        ///     action `activate`; `undo` must withdraw the plan from the part.
        ///     RED ON BRIDGE 0.124.1, by the code path, for the reason the live
        ///     probe M19 measured in its phase N: the state after the plan was
        ///     read on the window in front (McpTools.cs:2113, `_sessione.State`),
        ///     the drawing, whose volume and feature count did not change, so
        ///     `hasBuilt` was false, no memory was written, and `undo`
        ///     answered that it had no plan to withdraw («NOTHING TO WITHDRAW
        ///     (hasBuilt false: no memory written)» in the probe); and the
        ///     answer of the plan named no document in front.
        ///  c. plan 1 builds a plate; a `measure` (`mass_properties`) follows;
        ///     `undo` must withdraw plan 1, since a measure is not a plan and
        ///     writes nothing. This case DECIDES review F3: the measure's undo
        ///     recording is empty and SOLIDWORKS answers false for it (probe M19,
        ///     H4a/H4b); whether that leaves an entry on the stack is undecided,
        ///     and if it did, the press would take away that empty entry, the
        ///     part would read unchanged and the case would be red. RED ON
        ///     BRIDGE 0.124.1, by the code path: the measure did not build, the
        ///     memory was set to null, and the answer was that there was no plan.
        ///  d. two bridge processes on the same part (review F14): bridge 1
        ///     builds a plate with two holes, bridge 2 reorders the holes, bridge
        ///     1 calls `undo`. The part no longer reads as bridge 1's plan
        ///     left it, so `undo` must refuse BEFORE pressing, and the reorder
        ///     must still be there. RED ON BRIDGE 0.124.1, by the code path:
        ///     volume and count were as plan 1 left them, so it pressed, took
        ///     away bridge 2's reorder, which a redo does not put back (probe
        ///     `redo`), and answered that it had put the part back because
        ///     EditRedo2 did not throw; no `refused_before_pressing`. (Red on the
        ///     first version of this lot too: it pressed, and answered «NOT as it
        ///     was».)
        /// </summary>
        private static void UndoHonesty(Mcp.Bridge p, string bridgeExe)
        {
            IModelDoc2 partA = null, partB = null, partC = null, partD = null;
            Mcp.Bridge second = null;
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
                BenchError("undo honesty", Why(ex));
            }
            finally
            {
                if (second != null) { try { Mcp.Close(second); second.Dispose(); } catch { } }
                // The drawing first: it holds a reference to the part.
                try { if (!string.IsNullOrEmpty(drawingTitle)) _sw.CloseDoc(drawingTitle); } catch { }
                Close(partA);
                try { if (!string.IsNullOrEmpty(savedB)) _sw.CloseDoc(savedB); else Close(partB); } catch { }
                RemoveQuietly(savedB);
                Close(partC);
                Close(partD);
            }
        }

        /// <summary>The plate with two through holes far apart, as a `build` plan.</summary>
        private const string PlateWithTwoHoles =
            "{\"steps\":[" +
            "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
            "\"length_mm\":120,\"width_mm\":80,\"thickness_mm\":10,\"centered\":true}}," +
            "{\"operation\":\"cylindrical_cut\",\"arguments\":{\"face_ref\":{\"step\":1," +
            "\"kind\":\"top_face\"},\"x_mm\":-30,\"y_mm\":0,\"diameter_mm\":10,\"through_all\":true}}," +
            "{\"operation\":\"cylindrical_cut\",\"arguments\":{\"face_ref\":{\"step\":1," +
            "\"kind\":\"top_face\"},\"x_mm\":30,\"y_mm\":0,\"diameter_mm\":10,\"through_all\":true}}]}";

        /// <summary>The `build` plan that puts the second hole before the first.</summary>
        private static string ReorderPlan(List<string> cuts)
        {
            return "{\"steps\":[{\"operation\":\"reorder_feature\",\"arguments\":{\"feature_uid\":\"" + cuts[1] +
                   "\",\"relative_to_uid\":\"" + cuts[0] + "\",\"placement\":\"before\"}}]}";
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
        private static void UndoWithdrawsTheLastPlanEvenIfItOnlyReordered(Mcp.Bridge p, ref IModelDoc2 part)
        {
            part = NewPart();
            var look = ResponseText(Mcp.Ask(p, 40, Call(40, "inspect_part", "{}")));
            if (Mcp.Obj(look) == null)
            {
                BenchError("undo honesty, case a", "inspect_part gave no state: " + Short(look));
                return;
            }

            var before = FeaturesOf(part);
            var plan1 = ResponseText(Mcp.Ask(p, 41, Call(41, "build", PlateWithTwoHoles)));
            var afterPlan1 = FeaturesOf(part);
            var cuts = NewCuts(before, afterPlan1);
            Check("undo case a: plan 1 builds a plate with two holes, read by the bench through COM",
                  cuts.Count == 2 && TestVolume(part) > 1, "holes «" + string.Join("», «", cuts) + "», volume " +
                  TestVolume(part).ToString("0.#") + " mm3 — " + Short(plan1));
            if (cuts.Count != 2) return;

            // Plan 2: the second hole before the first. Same volume, same count.
            double volumeAfterPlan1 = TestVolume(part);
            var plan2Text = ResponseText(Mcp.Ask(p, 42, Call(42, "build", ReorderPlan(cuts))));
            var plan2 = Mcp.Obj(plan2Text);
            bool reordered = Before(FeaturesOf(part), cuts[1], cuts[0]);
            Check("undo case a: plan 2 reorders the two holes, read by the bench through COM",
                  reordered && Math.Abs(TestVolume(part) - volumeAfterPlan1) < 0.01, Short(plan2Text));
            if (!reordered) return;

            // Measured on the bench of 1/10/2026: a reorder does not land in the
            // undo entry the executor records (FinishRecordingUndoObject answers
            // False), so nobody knows what sits on top of the undo stack. The
            // safe answer is to refuse without pressing, and say why.
            object recorded = Mcp.Inside(plan2, "undo_entry_recorded");
            Check("undo case a: plan 2 says it left no undo entry of its own (undo_entry_recorded false)",
                  recorded is bool && !(bool)recorded,
                  "undo_entry_recorded = " + (recorded == null ? "(absent)" : Convert.ToString(recorded)));

            var undoText = ResponseText(Mcp.Ask(p, 43, Call(43, "undo", "{}")));
            var undo = Mcp.Obj(undoText);
            object done = Mcp.Inside(undo, "undone");
            Check("undo case a: `undo` refuses without pressing: the top of the undo stack is unknown",
                  !(done is bool && (bool)done), Short(undoText));

            var afterUndo = FeaturesOf(part);
            Check("undo case a: and the reorder is still there, read by the bench through COM",
                  Before(afterUndo, cuts[1], cuts[0]),
                  "positions " + IndexOf(afterUndo, cuts[0]) + " and " + IndexOf(afterUndo, cuts[1]));
            bool allThere = true;
            foreach (var f in afterPlan1) if (IndexOf(afterUndo, f.Item1) < 0) allThere = false;
            Check("undo case a: and every plan-1 feature is still there, with the same volume",
                  allThere && Math.Abs(TestVolume(part) - volumeAfterPlan1) < 0.01,
                  afterUndo.Count + " features (" + afterPlan1.Count + " after plan 1), volume " +
                  volumeAfterPlan1.ToString("0.#") + " -> " + TestVolume(part).ToString("0.#") + " mm3");
        }

        /// <summary>Case b of <see cref="UndoHonesty"/>.</summary>
        private static void UndoAfterAnotherWindowCameInFront(
            Mcp.Bridge p, ref IModelDoc2 part, ref string saved, ref string drawingTitle)
        {
            // create_drawing wants a part saved on disk.
            part = NewPart();
            string path = Path.Combine(Expectations.Work, "client_door_undo_drawing.sldprt");
            RemoveQuietly(path);
            int e = 0, w = 0;
            // SaveAs3, not the obsolete SaveAs frozen for Z-C21.7 (review F13).
            part.Extension.SaveAs3(path, 0, 1, null, null, ref e, ref w);
            if (!File.Exists(path))
            {
                BenchError("undo honesty, case b", "could not save «" + path + "» (e=" + e + ", w=" + w + ")");
                return;
            }
            saved = path;
            string json = path.Replace("\\", "\\\\");

            var look = ResponseText(Mcp.Ask(p, 50, Call(50, "inspect_part", "{}")));
            if (Mcp.Obj(look) == null)
            {
                BenchError("undo honesty, case b", "inspect_part gave no state: " + Short(look));
                return;
            }

            var planText = ResponseText(Mcp.Ask(p, 51, Call(51, "build",
                "{\"steps\":[" +
                "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                "\"length_mm\":100,\"width_mm\":60,\"thickness_mm\":10,\"centered\":true}}," +
                "{\"operation\":\"create_drawing\",\"arguments\":{}}]}")));
            var plan = Mcp.Obj(planText);
            IModelDoc2 front = _sw.IActiveDoc2;
            bool drawingInFront = front != null &&
                front.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocDRAWING;
            if (drawingInFront) drawingTitle = front.GetTitle();
            Check("undo case b: the plan ends with its drawing in front, read by the bench through COM",
                  drawingInFront, "in front «" + (front == null ? "(nothing)" : front.GetTitle()) + "» — " + Short(planText));
            if (!drawingInFront) return;

            string named = Convert.ToString(Mcp.Inside(plan, "document_in_front")) ?? "";
            Check("undo case b: the answer of `build` says which document is in front",
                  named.Length > 0 && named.IndexOf(drawingTitle, StringComparison.OrdinalIgnoreCase) >= 0,
                  "document_in_front «" + named + "», the bench sees «" + drawingTitle + "»");
            object volumeAfter = Mcp.Inside(plan, "volume_after_mm3");
            double volumeAnswered;
            bool readable = volumeAfter != null &&
                double.TryParse(Convert.ToString(volumeAfter, System.Globalization.CultureInfo.InvariantCulture),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out volumeAnswered) &&
                Math.Abs(volumeAnswered - 60000) < 1;
            // 100 x 60 x 10 = 60000 mm3, and the part started empty.
            Check("undo case b: and its volume_after_mm3 is the part's (60000 mm3), not the drawing's",
                  readable, "volume_after_mm3 = " + (volumeAfter == null ? "(absent)" : Convert.ToString(volumeAfter)));

            string extrusion = null;
            foreach (var f in FeaturesOf(part)) if (f.Item2 == "Extrusion") extrusion = f.Item1;

            var back = ResponseText(Mcp.Ask(p, 52, Call(52, "document",
                "{\"action\":\"activate\",\"path\":\"" + json + "\"}")));
            IModelDoc2 nowInFront = _sw.IActiveDoc2;
            bool partInFront = nowInFront != null &&
                string.Equals(nowInFront.GetPathName(), path, StringComparison.OrdinalIgnoreCase);
            Check("undo case b: `document` with `activate` brings the part back to the front, read by the bench through COM",
                  partInFront, "in front «" + (nowInFront == null ? "(nothing)" : nowInFront.GetPathName()) + "» — " + Short(back));
            if (!partInFront) return;

            var undoText = ResponseText(Mcp.Ask(p, 53, Call(53, "undo", "{}")));
            var undo = Mcp.Obj(undoText);
            object done = Mcp.Inside(undo, "undone");
            Check("undo case b: `undo` withdraws the plan from the part, the drawing having been in front",
                  done is bool && (bool)done, Short(undoText));
            var left = FeaturesOf(part);
            Check("undo case b: and the bench reads the part empty again: the extrusion is gone",
                  extrusion != null && IndexOf(left, extrusion) < 0 && TestVolume(part) < 0.001,
                  "extrusion «" + (extrusion ?? "(not found before)") + "» at position " +
                  (extrusion == null ? -1 : IndexOf(left, extrusion)) + ", volume " +
                  TestVolume(part).ToString("0.###") + " mm3");
        }

        /// <summary>Case c of <see cref="UndoHonesty"/>.</summary>
        private static void UndoAfterAMeasure(Mcp.Bridge p, ref IModelDoc2 part)
        {
            part = NewPart();
            var look = ResponseText(Mcp.Ask(p, 70, Call(70, "inspect_part", "{}")));
            if (Mcp.Obj(look) == null)
            {
                BenchError("undo honesty, case c", "inspect_part gave no state: " + Short(look));
                return;
            }

            var plan = ResponseText(Mcp.Ask(p, 71, Call(71, "build",
                "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                "\"length_mm\":80,\"width_mm\":50,\"thickness_mm\":10,\"centered\":true}}]}")));
            string extrusion = null;
            foreach (var f in FeaturesOf(part)) if (f.Item2 == "Extrusion") extrusion = f.Item1;
            // 80 x 50 x 10 = 40000 mm3, and the part started empty.
            Check("undo case c: the plan builds a plate, read by the bench through COM",
                  extrusion != null && Math.Abs(TestVolume(part) - 40000) < 1,
                  "extrusion «" + (extrusion ?? "(none)") + "», volume " + TestVolume(part).ToString("0.#") +
                  " mm3 — " + Short(plan));
            if (extrusion == null) return;

            var measureText = ResponseText(Mcp.Ask(p, 72, Call(72, "measure",
                "{\"steps\":[{\"operation\":\"mass_properties\",\"arguments\":{}}]}")));
            object measureEntry = Mcp.Inside(Mcp.Obj(measureText), "undo_entry_recorded");

            var undoText = ResponseText(Mcp.Ask(p, 73, Call(73, "undo", "{}")));
            object done = Mcp.Inside(Mcp.Obj(undoText), "undone");
            Check("undo case c: after a measure, `undo` still withdraws the plan",
                  done is bool && (bool)done,
                  "the measure answered undo_entry_recorded = " +
                  (measureEntry == null ? "(absent)" : Convert.ToString(measureEntry)) + " — " + Short(undoText));
            Check("undo case c: and the bench reads the part empty again: the extrusion is gone",
                  IndexOf(FeaturesOf(part), extrusion) < 0 && TestVolume(part) < 0.001,
                  "extrusion at position " + IndexOf(FeaturesOf(part), extrusion) + ", volume " +
                  TestVolume(part).ToString("0.###") + " mm3");
        }

        /// <summary>Case d of <see cref="UndoHonesty"/>.</summary>
        private static void UndoAfterAnotherBridgeReordered(
            Mcp.Bridge p, string bridgeExe, ref IModelDoc2 part, ref Mcp.Bridge second)
        {
            part = NewPart();
            var look = ResponseText(Mcp.Ask(p, 60, Call(60, "inspect_part", "{}")));
            if (Mcp.Obj(look) == null)
            {
                BenchError("undo honesty, case d", "inspect_part gave no state: " + Short(look));
                return;
            }
            var before = FeaturesOf(part);
            var plan1 = ResponseText(Mcp.Ask(p, 61, Call(61, "build", PlateWithTwoHoles)));
            var afterPlan1 = FeaturesOf(part);
            var cuts = NewCuts(before, afterPlan1);
            Check("undo case d: bridge 1 builds a plate with two holes, read by the bench through COM",
                  cuts.Count == 2, "holes «" + string.Join("», «", cuts) + "» — " + Short(plan1));
            if (cuts.Count != 2) return;
            double volumeAfterPlan1 = TestVolume(part);

            // A second bridge process, on the same SOLIDWORKS and the same part.
            second = bridgeExe == null ? null : Mcp.Open(bridgeExe);
            var hello = second == null ? null : Mcp.Ask(second, 1,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                "\"clientInfo\":{\"name\":\"banco-2\",\"version\":\"1\"}}}");
            if (hello == null)
            {
                BenchError("undo honesty, case d", "the second bridge did not start or did not answer initialize");
                return;
            }
            var look2 = ResponseText(Mcp.Ask(second, 2, Call(2, "inspect_part", "{}")));
            var reorderText = ResponseText(Mcp.Ask(second, 3, Call(3, "build", ReorderPlan(cuts))));
            bool reordered = Before(FeaturesOf(part), cuts[1], cuts[0]);
            Check("undo case d: bridge 2 reorders the two holes, read by the bench through COM",
                  reordered, Short(reorderText) + " (it looked: " + Short(look2) + ")");
            if (!reordered) return;

            var undoText = ResponseText(Mcp.Ask(p, 62, Call(62, "undo", "{}")));
            var undo = Mcp.Obj(undoText);
            object refused = Mcp.Inside(undo, "refused_before_pressing");
            object done = Mcp.Inside(undo, "undone");
            Check("undo case d: bridge 1's `undo` refuses BEFORE pressing: the part is not as its plan left it",
                  refused is bool && (bool)refused && done is bool && !(bool)done &&
                  (Convert.ToString(Mcp.Inside(undo, "reason")) ?? "").IndexOf("order", StringComparison.OrdinalIgnoreCase) >= 0,
                  Short(undoText));

            var afterUndo = FeaturesOf(part);
            bool allThere = true;
            foreach (var f in afterPlan1) if (IndexOf(afterUndo, f.Item1) < 0) allThere = false;
            Check("undo case d: and bridge 2's reorder and every plan-1 feature are still there, read by the bench through COM",
                  Before(afterUndo, cuts[1], cuts[0]) && allThere &&
                  Math.Abs(TestVolume(part) - volumeAfterPlan1) < 0.01,
                  "positions " + IndexOf(afterUndo, cuts[1]) + " and " + IndexOf(afterUndo, cuts[0]) + ", " +
                  afterUndo.Count + " features, volume " + TestVolume(part).ToString("0.#") + " mm3");
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
        /// che la produceva. `Checks/Modal` ha misurato che con un modale
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
        private static void TheModal(Mcp.Bridge p, IModelDoc2 doc)
        {
            IntPtr frameBorder = IntPtr.Zero;
            try { frameBorder = new IntPtr(((IFrame)_sw.Frame()).GetHWnd()); } catch { }
            uint pid = 0;
            if (frameBorder != IntPtr.Zero) GetWindowThreadProcessId(frameBorder, out pid);
            if (pid == 0)
            {
                BenchError("il modale", "non leggo la finestra principale di SOLIDWORKS");
                return;
            }

            var wire = new System.Threading.Thread(() =>
            {
                try
                {
                    var its = (ISldWorks)System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application");
                    its.SendMsgToUser2("Banco, voce Z-C3: questa finestra si chiude da sola.",
                        (int)SolidWorks.Interop.swconst.swMessageBoxIcon_e.swMbInformation,
                        (int)SolidWorks.Interop.swconst.swMessageBoxBtn_e.swMbOk);
                }
                catch { }
            });
            wire.SetApartmentState(System.Threading.ApartmentState.STA);
            wire.IsBackground = true;
            wire.Start();

            IntPtr dialog = IntPtr.Zero;
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            while (dialog == IntPtr.Zero && t0.ElapsedMilliseconds < 8000)
            {
                System.Threading.Thread.Sleep(100);
                dialog = DialogOf(pid);
            }
            if (dialog == IntPtr.Zero)
            {
                BenchError("il modale", "la finestra di messaggio non si e' vista entro 8 s: la guardia non si puo' provare");
                return;
            }

            string withModal;
            double volumeBefore = TestVolume(doc), volumeAfter;
            try
            {
                withModal = ResponseText(Mcp.Ask(p, 30, Call(30, "build",
                    "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":" +
                    "{\"length_mm\":20,\"width_mm\":20,\"thickness_mm\":5}}]}")));
            }
            finally
            {
                // IL MODALE SI CHIUDE IN OGNI CASO: lasciato aperto, pianta il
                // resto del giro e quello dopo.
                PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
                wire.Join(8000);
                System.Threading.Thread.Sleep(300);
            }
            volumeAfter = TestVolume(doc);

            Check("modale: con una finestra modale aperta il piano e' rifiutato con la frase della guardia",
                  withModal.IndexOf("finestra aperta che aspetta una risposta", StringComparison.OrdinalIgnoreCase) >= 0,
                  Short(withModal));
            Check("modale: e il pezzo non e' cambiato",
                  Math.Abs(volumeAfter - volumeBefore) < 0.01,
                  volumeBefore.ToString("0.#") + " -> " + volumeAfter.ToString("0.#") + " mm3");

            // LA GEMELLA: modale chiuso, finestra abilitata, e lo stesso ponte
            // lavora. Un'operazione che legge soltanto, per non cambiare il pezzo
            // su cui il resto del gruppo conta.
            bool enabled = IsWindowEnabled(frameBorder);
            var without = ResponseText(Mcp.Ask(p, 31, Call(31, "build",
                "{\"steps\":[{\"operation\":\"mass_properties\",\"arguments\":{}}]}")));
            Check("modale: chiuso il modale, lo stesso ponte torna a lavorare",
                  enabled && without.IndexOf("finestra aperta", StringComparison.OrdinalIgnoreCase) < 0 &&
                  (without.IndexOf("\"succeeded\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   without.IndexOf("\"succeeded\": true", StringComparison.OrdinalIgnoreCase) >= 0),
                  "finestra abilitata " + enabled + " — " + Short(without));
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
        private static IntPtr DialogOf(uint pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                uint its;
                GetWindowThreadProcessId(h, out its);
                if (its != pid || !IsWindowVisible(h)) return true;
                var itemName = new System.Text.StringBuilder(64);
                GetClassName(h, itemName, itemName.Capacity);
                if (itemName.ToString() == "#32770") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>L'eseguibile del ponte, o `null`.</summary>
        private static string TheBridge()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var root = Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
                var p = Path.Combine(root, "bin", "oce-solid-agent-mcp.exe");
                return File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        /// <summary>Una riga `tools/call` bell'e pronta.</summary>
        private static string Call(int id, string itemName, string arguments)
        {
            return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":" +
                   "{\"name\":\"" + itemName + "\",\"arguments\":" + arguments + "}}";
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
        private static string ResponseText(string line)
        {
            if (line == null) return "(nessuna risposta)";
            var o = Mcp.Obj(line);
            var result = Mcp.Inside(o, "result") as Dictionary<string, object>;
            var content = Mcp.Inside(result, "content") as object[];
            if (content == null || content.Length == 0) return line;
            var t = Convert.ToString(Mcp.Inside(content[0], "text"));
            return string.IsNullOrEmpty(t) ? line : t;
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
        private static double TestVolume(IModelDoc2 doc)
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
