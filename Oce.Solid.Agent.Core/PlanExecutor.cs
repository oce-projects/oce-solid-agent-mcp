using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Esegue un piano confermato, un passo alla volta.
    ///
    /// COME SI PARLANO SERVER E ADD-IN
    ///   1. l'add-in chiede al server QUAL E' il prossimo passo, mandando lo
    ///      stato del documento in quel momento;
    ///   2. il server ricontrolla tutto (piano confermato, stessa macchina,
    ///      stesso documento) e risponde con UN passo, oppure "finito";
    ///   3. l'add-in lo esegue dentro SOLIDWORKS e racconta com'e' andata;
    ///   4. si ricomincia.
    ///
    /// PERCHE' SI CHIEDE OGNI VOLTA INVECE DI SCARICARE TUTTO IL PIANO
    /// Perche' il controllo va rifatto: fra il primo passo e l'ultimo possono
    /// passare minuti, e in mezzo l'utente puo' aver cambiato documento. Un
    /// piano scaricato in blocco e' un piano che si fida di un controllo fatto
    /// prima.
    ///
    /// PERCHE' NON SI CHIAMA IL MODELLO FRA UN PASSO E L'ALTRO
    /// Perche' l'utente ha confermato un ELENCO. Se il modello potesse
    /// aggiungere un passo durante l'esecuzione, verrebbe eseguita una cosa
    /// diversa da quella approvata. Il modello torna a parlare alla fine.
    ///
    /// COSA SUCCEDE SE UN PASSO FALLISCE
    /// Ci si ferma. Non si prosegue con i successivi "tanto sono indipendenti":
    /// quasi mai lo sono — il foro va sulla piastra che il passo prima non ha
    /// creato — e proseguire vuol dire costruire su un presupposto falso. Cio'
    /// che era gia' stato creato resta, ed e' scritto nel piano quale passo e'
    /// arrivato dove: chi guarda sa cosa c'e' nel documento e perche'.
    /// </summary>
    public class EsecutorePiano
    {
        private readonly ISldWorks _sw;
        private readonly IFontePiano _fonte;
        private readonly Action<string> _log;
        private readonly Func<Func<object>, Task<object>> _sulThreadSw;

        /// <summary>
        /// Come si chiama la voce di annullamento che raccoglie tutto il
        /// piano. Diceva «OpenPDM assistente»: e' il nome che l'utente legge
        /// nel menu di SOLIDWORKS, e in una libreria che non presuppone
        /// OpenPDM non puo' essere scritto qui dentro.
        /// </summary>
        public string NomeAnnullamento
        {
            get { return _nomeAnnullamento; }
            set { _nomeAnnullamento = string.IsNullOrWhiteSpace(value) ? "Assistente" : value; }
        }
        private string _nomeAnnullamento = "Assistente";

        /// <summary>
        /// Lascia lavorare su un documento MAI SALVATO su disco.
        ///
        /// **Va acceso solo dal ponte MCP, mai dall'add-in.** Sono due mestieri
        /// diversi che usano lo stesso esecutore:
        ///
        ///   * l'add-in lavora dentro un PDM. Un file non salvato non e' in
        ///     check-out, e scriverci dentro produce modifiche che al
        ///     salvataggio falliscono o finiscono in una copia. Li' il rifiuto
        ///     «salvalo su disco prima» e' la cosa giusta;
        ///   * il ponte MCP lavora apposta su `Parte1` appena aperta. Li' quel
        ///     rifiuto renderebbe il ponte inutile per il suo caso normale.
        ///
        /// PERCHE' NON E' UN BOOLEANO QUALUNQUE. Il pericolo scritto nella voce
        /// B8 e' che qualcuno lo metta a vero anche nell'add-in «perche' dava
        /// fastidio», e si torni a scrivere in file non in check-out senza che
        /// niente fallisca. Per questo:
        ///
        ///   * il nome dice cosa PERMETTE, non cosa disattiva;
        ///   * il valore predefinito e' falso, quindi chi non ne sa niente e'
        ///     protetto;
        ///   * e c'e' un controllo che lo sorveglia —
        ///     `Checks\check-documents-without-file.ps1` fallisce se questa
        ///     proprieta' compare nei sorgenti dell'add-in.
        ///
        /// Non tocca il controllo sulla SOLA LETTURA: quello vale in tutti e due
        /// i mestieri, e un file di sola lettura non si scrive comunque.
        /// </summary>
        public bool DocumentoSenzaFile { get; set; }

        /// <summary>
        /// Lascia lavorare anche su una TAVOLA, non solo su una parte.
        ///
        /// Stessa storia di <see cref="DocumentoSenzaFile"/>, altro mestiere:
        ///
        ///   * l'assistente dell'add-in disegna PEZZI. Un piano che arriva su
        ///     una tavola li' e' quasi sempre un errore di chi ha cambiato
        ///     finestra, e fermarsi e' giusto;
        ///   * il ponte MCP ha cinque operazioni di tavola in catalogo
        ///     (`create_drawing`, `standard_views`, `set_sheet`,
        ///     `model_dimensions`, `export_pdf`). Rifiutare le tavole
        ///     renderebbe inutili proprio quelle.
        ///
        /// Sorvegliata da `Checks\check-documents-without-file.ps1`
        /// insieme all'altra.
        ///
        /// QUI C'ERA SCRITTO CHE GLI ASSIEMI RESTAVANO FUORI «non per
        /// dimenticanza: nessuna operazione della libreria sa lavorare su un
        /// assieme». Era vero fino al 16/9/2026. Il 17 sono nate le voci
        /// GA0-GA10 e adesso di operazioni di assieme ce ne sono quattro:
        /// vedi <see cref="AncheSuAssiemi"/>, e la lezione scritta li'.
        /// </summary>
        public bool AncheSuTavole { get; set; }

        /// <summary>
        /// Lascia lavorare anche su un ASSIEME.
        ///
        /// Terzo interruttore, stessa forma dei due sopra: **acceso solo dal
        /// ponte MCP, spento nell'add-in**, dove l'assistente disegna pezzi e
        /// un assieme e' quasi sempre la finestra sbagliata.
        ///
        /// ── PERCHE' ESISTE, E PERCHE' NON C'ERA ──────────────────────────
        ///
        /// Fino al 18/9/2026 `ControlliPreliminari` rifiutava **ogni** assieme,
        /// e la riga di commento che difendeva quella scelta diceva: *«nessuna
        /// operazione della libreria sa lavorare su un assieme»*. Era vero
        /// quando fu scritta. Il 17/9 sono arrivate `insert_component`,
        /// `mate`, `replace_component` e `interference_check` — e
        /// **il catalogo servito ai client le elencava tutte e quattro**.
        ///
        /// Il ponte prometteva quattro operazioni che avrebbe rifiutato prima
        /// del primo passo. Nessuna delle 781 prove del banco poteva vederlo:
        /// il banco chiama la LIBRERIA, non il ponte.
        ///
        /// **Un commento che spiega una scelta e' anche cio' che impedisce di
        /// rivederla**: chi lo legge trova una ragione, e trovare una ragione
        /// e' come smettere di cercare. Per questo la difesa adesso non e' una
        /// frase ma un controllo —
        /// `Tests\Program.cs` → `OgniFamigliaDelCatalogoHaUnDocumentoAmmesso`
        /// confronta le famiglie del catalogo coi tipi ammessi qui sotto, gira
        /// senza SOLIDWORKS, ed e' nata rossa su questo difetto.
        /// </summary>
        public bool AncheSuAssiemi { get; set; }

        public EsecutorePiano(
            ISldWorks sw,
            IFontePiano fonte,
            Action<string> log,
            Func<Func<object>, Task<object>> sulThreadSw)
        {
            _sw = sw;
            _fonte = fonte;
            _log = log ?? (m => { });
            _sulThreadSw = sulThreadSw;
        }

        public class Risultato
        {
            public bool Completato;
            public int PassiEseguiti;
            public string Errore;

            /// <summary>
            /// Z-C21.14: the document the plan started on, held by reference
            /// (the documentoAtteso read once at the start), so that the
            /// bridge reads the state after the plan on THAT document and not
            /// on whatever window is in front at the end. Null only when
            /// SOLIDWORKS had no active document.
            /// </summary>
            public IModelDoc2 PlanDocument;

            /// <summary>
            /// Z-C21.14: the document the plan was working on when it ended:
            /// the plan document, or the new one a step declared
            /// (DocumentoNuovo, e.g. the drawing of create_drawing).
            /// </summary>
            public IModelDoc2 FinalDocument;

            /// <summary>
            /// Z-C21.14: what FinishRecordingUndoObject answered for the undo
            /// entry recorded on <see cref="PlanDocument"/>. True: the entry
            /// was closed with something in it, so it now sits on top of that
            /// document's undo stack. False: SOLIDWORKS answered false. The
            /// live probe M19 (1/10/2026, phases H4a and H4b) measured only the
            /// answer: false for an EMPTY recording, true for a full one.
            /// Whether an empty recording leaves an entry on the stack is
            /// UNDECIDED: the probe's control H3 failed, so the second undo and
            /// the comparison of those phases never ran. Null when it is not
            /// known: no recording was
            /// opened on the plan document (the plan stopped before, or
            /// StartRecordingUndoObject threw), the call threw, or a second
            /// recording was opened on it later in the same plan.
            /// </summary>
            public bool? UndoEntryOnPlanDocument;

            /// <summary>
            /// Z-C21.14: true when the plan stopped before opening its undo
            /// recording and before any step: the preliminary checks refused,
            /// or the document was not the one the caller named. Nothing was
            /// written and the undo stack was not touched.
            /// </summary>
            public bool StoppedBeforeWriting;
        }

        // `pianoId` non c'e' piu' nella firma: e' un numero del server, e
        // chi esegue non deve sapere che esista un server. Lo conosce
        // l'adattatore che implementa IFontePiano.
        public async Task<Risultato> EseguiAsync(
            string percorsoAtteso,
            CancellationToken annulla = default(CancellationToken))
        {
            var risultato = new Risultato();

            // Se il piano non dice su quale documento vale, si prende quello
            // attivo adesso — ma UNA volta sola, e da qui in poi e' quello.
            // Prima non si confrontava mai niente: fra la lettura dello stato e
            // l'esecuzione c'e' un giro di rete, e in quella finestra l'utente
            // puo' avere cambiato finestra. La lavorazione finiva nell'altro
            // documento, in check-out, dentro una sola voce di annullamento che
            // nessuno sa di dover premere.
            // IL DOCUMENTO SI TIENE PER MANO, NON PER NOME.
            //
            // Qui si prende UNA volta l'oggetto su cui si lavora, e da qui in
            // poi «e' ancora quello?» si chiede confrontando l'oggetto, non una
            // stringa.
            //
            // Le stringhe non reggono, ed e' stato misurato: un piano che
            // SALVA il pezzo a meta' strada cambia sia il percorso (da vuoto a
            // un file) sia il titolo (da «Parte131» a «z06»). Un confronto per
            // nome vedeva cambiare il nome e si fermava dicendo «il documento
            // attivo e' cambiato mentre eseguivo» — su un documento che non
            // era cambiato affatto, si era solo salvato. Rifiutare cio' che e'
            // corretto e' il peggiore dei due modi di sbagliare.
            //
            // Il nome resta, ma solo per i MESSAGGI: chi legge vuole sapere
            // come si chiama il pezzo, non il suo puntatore.
            var documentoAtteso = (IModelDoc2)await _sulThreadSw(() => _sw.IActiveDoc2);
            // Z-C21.14: the plan document, kept for whoever reads the result.
            var planDocument = documentoAtteso;
            risultato.PlanDocument = planDocument;
            risultato.FinalDocument = planDocument;

            // ── E I CONTROLLI GUARDANO **QUESTO**, NON QUELLO DI ADESSO ──
            //
            // Fino al 23/9/2026 `ControlliPreliminari()` girava PRIMA di
            // questa riga e si ripescava `_sw.IActiveDoc2` da solo: due
            // letture diverse dello stesso globale, con una finestra in
            // mezzo. I controlli potevano dire «va bene» del documento A —
            // e' una parte, e' salvato, non e' in sola lettura — e il piano
            // prendere il documento B, che magari e' un assieme in sola
            // lettura. **Nessun errore lo avrebbe detto**: tutt'e due le
            // letture erano legittime, ed e' il fatto che fossero DUE a
            // essere sbagliato (voce Z-C5).
            //
            // Il documento si tiene per mano, non per nome — e nemmeno
            // chiedendolo due volte.
            //
            // Il caso «nessun documento aperto» lo dice `ControlliPreliminari`
            // con la sua frase, come prima: il controllo del null che stava
            // qui era il SECONDO, e diceva la stessa cosa con parole
            // leggermente diverse a seconda di quale scattava per primo.
            var controllo = (string)await _sulThreadSw(
                () => ControlliPreliminari(documentoAtteso));
            if (controllo != null)
            {
                risultato.Errore = controllo;
                risultato.StoppedBeforeWriting = true;
                return risultato;
            }

            // Se il chiamante ha detto su quale documento vale il piano, lo si
            // verifica UNA volta, adesso. Dopo di che comanda l'oggetto.
            if (!string.IsNullOrEmpty(percorsoAtteso))
            {
                var qui = (string)await _sulThreadSw(() => Identita(documentoAtteso));
                if (!string.Equals(qui, percorsoAtteso, StringComparison.OrdinalIgnoreCase))
                {
                    risultato.Errore =
                        "Il piano era per «" + percorsoAtteso + "», ma davanti c'e' «" +
                        qui + "». Non ho toccato niente.";
                    risultato.StoppedBeforeWriting = true;
                    return risultato;
                }
            }
            else
            {
                percorsoAtteso = (string)await _sulThreadSw(() => Identita(documentoAtteso));
            }

            // LA FINESTRA DEGLI ERRORI SI SPEGNE, e si rimette com'era.
            //
            // Ogni passo ricostruisce, e una ricostruzione con un errore apre
            // «Cosa c'e' che non va»: un modale. La chiamata COM che l'ha
            // scatenato **non torna piu'**, e un piano lanciato da un banco non
            // sorvegliato resta li' tutta la notte. E' esattamente la lezione
            // gia' pagata con `swInputDimValOnCreate` sulla quotatura.
            //
            // Non si perde niente: gli errori si leggono dallo stato, che
            // adesso li riporta lavorazione per lavorazione.
            bool mostravaGliErrori = false;
            bool sapevaMostrarli = false;
            await _sulThreadSw(() =>
            {
                try
                {
                    mostravaGliErrori = _sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild);
                    sapevaMostrarli = true;
                    _sw.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild, false);
                }
                catch { }
                return null;
            });

            // Tutto il piano dentro UNA voce di annullamento: se l'utente
            // preme Ctrl+Z subito dopo, torna com'era prima, non a meta'.
            // (StartRecordingUndoObject/FinishRecordingUndoObject esistono
            // nella 2021: verificato per riflessione sull'interop 29.5.1.1.)
            //
            // ON THE DOCUMENT HELD, AND WRITTEN DOWN (finding M19, lot
            // Z-C21.5). Up to library 0.145.0 the recording was started on a
            // fresh read of _sw.IActiveDoc2 and finished on documentoAtteso:
            // two documents whenever the window changed in between. Now it is
            // started on documentoAtteso itself, and undoRecordedOn remembers
            // which document holds it: that is the one it is finished on, and
            // when a step moves the plan to a new document the recording is
            // closed there and a new one opened on the new document (see
            // DocumentoNuovo below), never retargeted in silence.
            IModelDoc2 undoRecordedOn = null;
            bool planDocumentReopened = false;
            await _sulThreadSw(() =>
            {
                undoRecordedOn = StartUndoRecording(documentoAtteso);
                return null;
            });

            // La geometria lasciata da ogni passo, per numero d'ordine. Vive
            // quanto l'esecuzione del piano e non un istante di piu': sono
            // oggetti di SolidWorks, validi solo finche' il documento resta
            // aperto e non viene ricostruito da capo.
            var passiFatti = new Dictionary<int, Riferimenti.Passo>();

            try
            {
                while (true)
                {
                    if (annulla.IsCancellationRequested)
                    {
                        risultato.Errore = "Fermato da te.";
                        break;
                    }

                    // The state of the document the plan works on (M19), not
                    // of whatever window is in front: a changed window is
                    // caught right before the step writes, below.
                    var stato = (Dictionary<string, object>)await _sulThreadSw(() =>
                        StatoDocumento.Leggi(documentoAtteso)?.Dati);

                    var prossimo = await _fonte.ProssimoAsync(stato);

                    // UNA FONTE CHE NON RISPONDE NON E' UN PIANO FINITO.
                    // Un null qui vorrebbe dire che l'adattatore ha
                    // inghiottito qualcosa: si dice, non si scambia per la
                    // fine. Vedi la nota su ProssimoPasso.
                    if (prossimo == null)
                    {
                        risultato.Errore =
                            "La fonte del piano non ha risposto niente: non so se il " +
                            "piano sia finito o se sia caduto qualcosa. Mi fermo qui, " +
                            "dopo " + risultato.PassiEseguiti + " passi.";
                        break;
                    }
                    if (prossimo.Finito)
                    {
                        risultato.Completato = true;
                        break;
                    }

                    var passo = prossimo.Passo;
                    if (passo == null)
                    {
                        risultato.Errore =
                            "La fonte dice che il piano non e' finito ma non mi da' " +
                            "nessun passo da fare.";
                        break;
                    }
                    _log($"[agente] passo {passo.Ordine}: {passo.Riassunto}");
                    if (annulla.IsCancellationRequested)
                    {
                        risultato.Errore = "Fermato da te.";
                        break;
                    }

                    var esito = (OperazioniSw.Esito)await _sulThreadSw(() =>
                    {
                        var modello = _sw.IActiveDoc2;

                        // IL CONTROLLO CHE MANCAVA, ED E' QUI CHE VA FATTO.
                        // Lo stato mandato al server e' stato letto PRIMA della
                        // chiamata di rete; questo e' il documento su cui si
                        // scrive DOPO. Fra i due c'e' tutto il giro HTTP.
                        if (!StessoOggetto(modello, documentoAtteso))
                        {
                            return new OperazioniSw.Esito
                            {
                                Riuscito = false,
                                Errore =
                                    "Il documento attivo e' cambiato mentre eseguivo: " +
                                    "il piano era per «" + percorsoAtteso + "», adesso " +
                                    "davanti c'e' «" + Identita(modello) + "». " +
                                    "Non ho toccato niente.",
                            };
                        }

                        var lettura = StatoDocumento.Leggi(modello);
                        var operazioni = new OperazioniSw(_sw, _log);

                        // Voce Z-C4: si annuncia cosa si sta per fare. Il
                        // `finally` e' obbligatorio -- senza, un passo che
                        // alza lascerebbe il guinzaglio teso e il guardiano
                        // segnalerebbe appesa una chiamata gia' finita.
                        AccendiIlGuardiano();
                        _guinzaglio.Comincia(passo.Operazione, DateTime.Now);
                        // `OperazioniSw.Esito` col nome intero: `Esito` e' una
                        // classe annidata, e senza il prefisso il compilatore
                        // non la trova.
                        OperazioniSw.Esito e;
                        try
                        {
                            e = operazioni.Esegui(
                                modello, passo.Operazione, passo.Argomenti, lettura, passiFatti);
                        }
                        finally { _guinzaglio.Finita(); }
                        if (e.Riuscito)
                        {
                            // Cio' che questo passo ha creato resta a
                            // disposizione dei successivi: e' l'unico modo per
                            // cui «fai un foro sulla piastra del passo 1»
                            // possa trovare quella piastra. Sta in memoria e
                            // non nel piano perche' una faccia di SolidWorks
                            // non si serializza: esiste solo qui, solo adesso,
                            // e solo dentro questa sessione.
                            passiFatti[passo.Ordine] = new Riferimenti.Passo
                            {
                                Feature = e.Feature,
                                Direzione = e.Direzione,
                                Operazione = passo.Operazione,
                            };

                            // Un rebuild dopo ogni passo: cosi' un errore si
                            // vede subito e non alla fine, quando non si sa
                            // piu' quale passo lo ha causato.
                            try { modello.EditRebuild3(); } catch { }

                            // SE IL PASSO HA DICHIARATO DI AVER CAMBIATO
                            // DOCUMENTO, si va dietro. Solo se lo dichiara:
                            // un documento che cambia senza che nessuno lo dica
                            // resta un motivo per fermarsi.
                            if (e.DocumentoNuovo != null)
                            {
                                // THE UNDO RECORDING FOLLOWS, EXPLICITLY (M19):
                                // the steps so far are closed as one entry on
                                // the document they changed, and the steps to
                                // come are recorded on the new one.
                                if (!StessoOggetto(undoRecordedOn, e.DocumentoNuovo))
                                {
                                    NoteUndoAnswer(risultato, undoRecordedOn, planDocument,
                                                   FinishUndoRecording(undoRecordedOn));
                                    undoRecordedOn = StartUndoRecording(e.DocumentoNuovo);
                                    // Back on the plan document: a second entry
                                    // there, and one answer no longer says what
                                    // is on top of its stack.
                                    if (undoRecordedOn != null && StessoOggetto(undoRecordedOn, planDocument))
                                        planDocumentReopened = true;
                                }
                                documentoAtteso = e.DocumentoNuovo;
                                risultato.FinalDocument = documentoAtteso;
                                _log("[agente] from here on the plan works on «" +
                                     Identita(documentoAtteso) + "», with its own undo entry");
                            }
                        }
                        return e;
                    });

                    await _fonte.EsitoAsync(
                        passo.Ordine,
                        esito.Riuscito,
                        esito.Riuscito
                            ? new Dictionary<string, object>
                            {
                                ["feature"] = esito.FeatureCreata,
                                ["measurements"] = esito.Misure,
                            }
                            : null,
                        esito.Errore);

                    if (!esito.Riuscito)
                    {
                        risultato.Errore = esito.Errore;
                        break;
                    }
                    risultato.PassiEseguiti++;
                }
            }
            catch (Exception ex)
            {
                risultato.Errore = ex.Message;
                _log("[agente] esecuzione interrotta: " + ex.Message);
            }
            finally
            {
                await _sulThreadSw(() =>
                {
                    // La finestra degli errori torna com'era: e'
                    // un'impostazione di chi lavora, non nostra. Si rimette
                    // solo se si e' riusciti a leggerla — scriverci sopra un
                    // valore inventato sarebbe peggio che lasciarla spenta.
                    if (sapevaMostrarli)
                    {
                        try
                        {
                            _sw.SetUserPreferenceToggle(
                                (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild,
                                mostravaGliErrori);
                        }
                        catch { }
                    }

                    // THE RECORDING IS CLOSED ON THE DOCUMENT IT WAS OPENED ON
                    // (M19): the object written down at the start (or at the
                    // last change of document), not the one with the same name
                    // (it may have been saved and renamed in between) and not
                    // the active one. Up to library 0.145.0 the closing waited
                    // for documentoAtteso to be the active document, so a plan
                    // stopped because the window changed left the recording
                    // open. The recording belongs to its document (it is a
                    // member of IModelDocExtension), so another window being in
                    // front does not move it; what FinishRecordingUndoObject
                    // answers is read, not assumed.
                    NoteUndoAnswer(risultato, undoRecordedOn, planDocument,
                                   FinishUndoRecording(undoRecordedOn));
                    if (planDocumentReopened) risultato.UndoEntryOnPlanDocument = null;
                    return null;
                });
            }

            return risultato;
        }

        /// <summary>
        /// Z-C21.14: keeps the answer of FinishRecordingUndoObject when the
        /// recording just closed is the one on the plan document.
        /// </summary>
        private static void NoteUndoAnswer(
            Risultato result, IModelDoc2 recordedOn, IModelDoc2 planDocument, bool? answer)
        {
            if (recordedOn != null && StessoOggetto(recordedOn, planDocument))
                result.UndoEntryOnPlanDocument = answer;
        }

        /// <summary>
        /// M19: starts the undo recording on <paramref name="document"/> and
        /// returns it, the document that now holds the recording; null when
        /// there is no document or the call threw (then nothing is recorded,
        /// and nothing is finished later). StartRecordingUndoObject is void:
        /// there is no answer to read.
        /// </summary>
        private static IModelDoc2 StartUndoRecording(IModelDoc2 document)
        {
            if (document == null) return null;
            try
            {
                document.Extension.StartRecordingUndoObject();
                return document;
            }
            catch { return null; }
        }

        /// <summary>
        /// M19: finishes the undo recording on the document that holds it, the
        /// one <see cref="StartUndoRecording"/> returned; nothing when null.
        /// The only call to FinishRecordingUndoObject in this file (still the
        /// obsolete one, frozen for Z-C21.7 as L17-4). Its answer, «True if
        /// recording of the SOLIDWORKS Undo object ends, false if not»
        /// (IModelDocExtension~FinishRecordingUndoObject.html), is read and a
        /// false one is logged: a recording left open would take in whatever is
        /// done to that document next.
        ///
        /// Returns the answer (Z-C21.14): true or false as SOLIDWORKS gave it,
        /// null when there was nothing to finish or the call threw. The live
        /// probe M19 (1/10/2026, phases H4a and H4b) measured only the answer:
        /// an EMPTY recording answers false, a recording with something in it
        /// answers true. Whether the empty one leaves an entry on the undo
        /// stack is undecided (the probe's control H3 failed, so that part of
        /// H4 never ran).
        /// </summary>
        private bool? FinishUndoRecording(IModelDoc2 recordedOn)
        {
            if (recordedOn == null) return null;
            bool ended = false;
            string failure = null;
            try { ended = recordedOn.Extension.FinishRecordingUndoObject(_nomeAnnullamento); }
            catch (Exception ex) { failure = ex.Message; }
            if (!ended)
                _log("[agente] the undo entry «" + _nomeAnnullamento + "» on «" + Identita(recordedOn) +
                     "» did not close (FinishRecordingUndoObject " +
                     (failure == null ? "answered false" : "threw: " + failure) + ")");
            if (failure != null) return null;
            return ended;
        }

        /// <summary>
        /// COME SI CHIAMA UN DOCUMENTO, per sapere se e' ancora quello di prima.
        ///
        /// Il percorso su disco, quando c'e'. Quando non c'e' — un pezzo appena
        /// aperto e mai salvato — il titolo, con un prefisso che lo distingue:
        /// un file che si chiamasse per davvero «Parte1» non deve poter essere
        /// scambiato per la «Parte1» in memoria.
        ///
        /// Prima si confrontavano i percorsi nudi. Senza il titolo, DUE
        /// documenti mai salvati hanno tutti e due percorso vuoto, e si
        /// sarebbero detti lo stesso documento: il controllo che esiste per non
        /// scrivere nel pezzo di qualcun altro avrebbe detto di si' proprio nel
        /// caso in cui il ponte lavora sempre.
        /// </summary>
        /// <summary>
        /// DUE RIFERIMENTI SONO LO STESSO DOCUMENTO?
        ///
        /// Non si confrontano i nomi: un documento che si salva a meta' piano
        /// cambia percorso E titolo, e resta lo stesso documento. Si
        /// confrontano gli oggetti COM, cioe' i puntatori a `IUnknown`, che
        /// sono l'identita' vera e non cambiano finche' il documento e' aperto.
        ///
        /// `ReferenceEquals` prima, perche' quasi sempre .NET riusa lo stesso
        /// involucro per lo stesso oggetto COM e si risparmia il giro; ma non
        /// e' garantito, quindi quando non basta si chiede a `IUnknown`. Le due
        /// `Release` non sono opzionali: `GetIUnknownForObject` incrementa il
        /// conteggio, e un conteggio che non torna a zero e' un documento che
        /// SOLIDWORKS non chiude piu'.
        /// </summary>
        private static bool StessoOggetto(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.GetIUnknownForObject(a);
                pb = Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.Release(pa);
                if (pb != IntPtr.Zero) Marshal.Release(pb);
            }
        }

        private static string Identita(IModelDoc2 modello)
        {
            if (modello == null) return "";
            try
            {
                var percorso = modello.GetPathName();
                if (!string.IsNullOrEmpty(percorso)) return percorso;
                var titolo = modello.GetTitle();
                return string.IsNullOrEmpty(titolo) ? "" : "titolo:" + titolo;
            }
            catch { return ""; }
        }

        /// <summary>
        /// I controlli che vanno fatti PRIMA di scrivere nel documento.
        /// Ritorna null se e' tutto a posto, altrimenti il motivo del rifiuto.
        ///
        /// ── IL DOCUMENTO ARRIVA, NON SI RIPESCA (voce Z-C5) ─────────────
        ///
        /// Prima questo metodo leggeva `_sw.IActiveDoc2` per conto suo, e il
        /// piano lo rileggeva subito dopo: due letture dello stesso globale
        /// con una finestra in mezzo, cioe' la possibilita' di approvare un
        /// documento e lavorarne un altro **senza che niente lo dica**.
        ///
        /// Un metodo che si ripesca da solo cio' su cui deve decidere e' un
        /// metodo che decide su qualcos'altro, prima o poi. Adesso lo
        /// riceve: se e' `null`, il chiamante non aveva niente da darci.
        /// </summary>
        // ══════════════════════════════════════════════════════════════
        // IL GUINZAGLIO, PER CHI USA IL PONTE (voce Z-C4)
        //
        // Il gemello di K18: quella protegge il GIRO del banco, questa
        // protegge il CLIENTE. Stessa classe, `Shared/Leash.cs`,
        // perche' due meccanismi di scadenza che si comportano diversamente
        // sono peggio di uno solo.
        //
        // ── E LA PREMESSA DELLA VOCE ERA PIU' STRETTA DEL VERO ────────
        //
        // Z-C4 diceva «chiamate a rischio MODALE». Il 23/9/2026 la chiamata
        // che non e' tornata non aveva nessun modale: era `NewDocument`
        // ferma dentro SMB su un file tenuto da una presa. Una scadenza non
        // deve presumere che ci sia una finestra da chiudere: deve solo
        // **smettere di aspettare e dire su cosa**.
        //
        // ── DOVE SCRIVE, E PERCHE' NON E' UN DETTAGLIO ───────────────
        //
        // **MAI SU STDOUT.** Su stdio, stdout e' il canale JSON-RPC: una
        // riga che non sia JSON lo rompe, e il client risponde -32700 a una
        // richiesta che non c'entra niente. Il referto va su **stderr**,
        // che i client MCP raccolgono nei loro log.
        //
        // E NON UCCIDE IL PROCESSO, al contrario del banco: il banco e' un
        // giro che o finisce o non serve, il ponte e' un servizio di
        // qualcun altro. Quello che puo' fare e' **dire** che una chiamata
        // e' appesa e quale — cosi' chi legge i log sa cosa e' successo
        // invece di vedere un ponte che non risponde piu'.
        // ══════════════════════════════════════════════════════════════
        private static readonly Shared.Guinzaglio _guinzaglio =
            new Shared.Guinzaglio(120000);

        private static System.Threading.Thread _guardiano;
        private static readonly object _chiaveGuardiano = new object();

        private static void AccendiIlGuardiano()
        {
            lock (_chiaveGuardiano)
            {
                if (_guardiano != null) return;
                _guardiano = new System.Threading.Thread(() =>
                {
                    string detto = null;
                    while (true)
                    {
                        System.Threading.Thread.Sleep(10000);
                        var referto = _guinzaglio.Appesa(DateTime.Now);
                        if (referto == null) { detto = null; continue; }
                        // UNA VOLTA SOLA PER CHIAMATA: un guardiano che
                        // ripete la stessa riga ogni dieci secondi riempie
                        // il log di chi ci lavora, e un log pieno non si
                        // legge piu'.
                        if (referto == detto) continue;
                        detto = referto;
                        try { Console.Error.WriteLine("[ponte] " + referto); }
                        catch { }
                    }
                });
                _guardiano.IsBackground = true;
                _guardiano.Start();
            }
        }
        private string ControlliPreliminari(IModelDoc2 modello)
        {
            if (modello == null) return "Non c'e' nessun documento aperto.";
            int tipo = modello.GetType();
            bool ammesso = tipo == (int)swDocumentTypes_e.swDocPART
                || (AncheSuTavole && tipo == (int)swDocumentTypes_e.swDocDRAWING)
                || (AncheSuAssiemi && tipo == (int)swDocumentTypes_e.swDocASSEMBLY);
            if (!ammesso)
            {
                // IL RIFIUTO DICE COSA E' AMMESSO ADESSO, non una frase fissa.
                // La vecchia diceva «una parte o una tavola, non un assieme»
                // anche quando gli assiemi erano ammessi e il documento era
                // qualcos'altro: un messaggio che elenca il mondo di ieri manda
                // chi legge a cercare il problema dove non e'.
                var vanno = new System.Collections.Generic.List<string> { "una parte" };
                if (AncheSuTavole) vanno.Add("una tavola");
                if (AncheSuAssiemi) vanno.Add("un assieme");
                return "Si lavora su " + string.Join(" o ", vanno) +
                       ": questo documento non e' niente di tutto cio'.";
            }

            var percorso = modello.GetPathName();
            if (string.IsNullOrEmpty(percorso) && !DocumentoSenzaFile)
                return "Il documento non e' mai stato salvato: salvalo su disco prima.";

            // UN FILE IN SOLA LETTURA NON SI TOCCA, e non e' un dettaglio.
            // Le API di SOLIDWORKS lo modificano in memoria lo stesso: il
            // pezzo cambia sullo schermo e il salvataggio poi fallisce, o
            // peggio finisce in una copia. Chi usa un PDM ha in piu' la
            // ragione sua — il file non e' in check-out — ma il controllo
            // vale anche senza.
            try
            {
                var info = new System.IO.FileInfo(percorso);
                if (info.Exists && info.IsReadOnly)
                    return "Il file e' in sola lettura: toglila (o fai il check-out) prima di chiedere modifiche.";
            }
            catch { /* se non riusciamo a leggerlo, lo diranno le API dopo */ }

            // IL MODALE SI VEDE DALLA FINESTRA, NON DA SOLIDWORKS (voce Z-C3).
            //
            // Misurato il 24/9/2026 con `Checks/Modal`: con una finestra
            // modale di SOLIDWORKS aperta, `CommandInProgress` resta FALSO in
            // tre giri su tre. La guardia qui sotto non era mai scattata perche'
            // non poteva. La finestra principale invece e' DISABILITATA — un
            // proprietario con un modale aperto lo e' sempre — e
            // `IsWindowEnabled` lo dice in zero millisecondi, da User32, senza
            // passare da una chiamata COM che col modale aperto potrebbe anche
            // non tornare.
            //
            // `CommandInProgress` stays next to it, but it is NOT a guard
            // against an interactive command (audit finding L17-5, real; the
            // sentence here said it was, with no measure behind it). It is a
            // switch an out-of-process client sets for itself:
            // ISldWorks~CommandInProgress.html, «Improves performance of
            // out-of-process applications by informing SOLIDWORKS that a
            // sequence of API calls will be made by the out-of-process
            // application», and «Set to True before calling the sequence of
            // API calls, then set to false after». With a modal window open it
            // reads false (the Z-C3 measure above). So it fires only when some
            // other program driving SOLIDWORKS left it true, and the refusal
            // says exactly that.
            try
            {
                int cornice = (_sw.Frame() as IFrame)?.GetHWnd() ?? 0;
                if (cornice != 0 && !IsWindowEnabled(new IntPtr(cornice)))
                    return FraseDelModale;
            }
            catch { }

            try
            {
                if (_sw.CommandInProgress)
                    return "SOLIDWORKS says a sequence of API calls is in progress " +
                           "(ISldWorks.CommandInProgress is true): another program driving " +
                           "SOLIDWORKS set it and has not cleared it. Let that program finish, " +
                           "or close it, then ask again.";
            }
            catch { }

            return null;
        }

        /// <summary>
        /// La frase del rifiuto quando SOLIDWORKS ha una finestra aperta che
        /// aspetta una risposta. Pubblica perche' il banco la pretende parola
        /// per parola: una frase scritta in due posti diverge.
        /// </summary>
        public const string FraseDelModale =
            "SOLIDWORKS ha una finestra aperta che aspetta una risposta (la sua finestra " +
            "principale e' disabilitata): chiudila e riprova. Lavorarci sopra adesso " +
            "vorrebbe dire restare appesi finche' qualcuno non clicca.";

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr finestra);
    }
}
