using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
        ///     `Controlli\controlla-documenti-senza-file.ps1` fallisce se questa
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
        ///     (`crea_disegno`, `viste_standard`, `imposta_foglio`,
        ///     `quote_dal_modello`, `esporta_pdf`). Rifiutare le tavole
        ///     renderebbe inutili proprio quelle.
        ///
        /// Sorvegliata da `Controlli\controlla-documenti-senza-file.ps1`
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
        /// quando fu scritta. Il 17/9 sono arrivate `inserisci_componente`,
        /// `accoppia`, `sostituisci_componente` e `verifica_interferenze` — e
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
        /// `Prove\Programma.cs` → `OgniFamigliaDelCatalogoHaUnDocumentoAmmesso`
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
            await _sulThreadSw(() =>
            {
                var m = _sw.IActiveDoc2;
                try { m?.Extension.StartRecordingUndoObject(); } catch { }
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

                    var stato = (Dictionary<string, object>)await _sulThreadSw(() =>
                        StatoDocumento.Leggi(_sw.IActiveDoc2)?.Dati);

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
                                documentoAtteso = e.DocumentoNuovo;
                                _log("[agente] da qui in poi si lavora su «" +
                                     Identita(documentoAtteso) + "»");
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
                                ["misure"] = esito.Misure,
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

                    // Si chiude la voce di annullamento sul documento GIUSTO:
                    // se l'utente e' passato a un'altra finestra, chiuderla li'
                    // raggrupperebbe sotto il nostro nome modifiche che non
                    // sono nostre.
                    // Si chiude sull'OGGETTO che si era aperto, non su quello
                    // che ha lo stesso nome: fra l'apertura e la chiusura il
                    // pezzo puo' essersi salvato e aver cambiato nome.
                    if (StessoOggetto(_sw.IActiveDoc2, documentoAtteso))
                    {
                        try
                        {
                            documentoAtteso.Extension.FinishRecordingUndoObject(_nomeAnnullamento);
                        }
                        catch { }
                    }
                    return null;
                });
            }

            return risultato;
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
        // protegge il CLIENTE. Stessa classe, `Condiviso/Guinzaglio.cs`,
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
        private static readonly Condiviso.Guinzaglio _guinzaglio =
            new Condiviso.Guinzaglio(120000);

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
            // Misurato il 24/9/2026 con `Controlli/Modale`: con una finestra
            // modale di SOLIDWORKS aperta, `CommandInProgress` resta FALSO in
            // tre giri su tre. La guardia qui sotto non era mai scattata perche'
            // non poteva. La finestra principale invece e' DISABILITATA — un
            // proprietario con un modale aperto lo e' sempre — e
            // `IsWindowEnabled` lo dice in zero millisecondi, da User32, senza
            // passare da una chiamata COM che col modale aperto potrebbe anche
            // non tornare.
            //
            // `CommandInProgress` resta accanto: non costa niente, e copre un
            // comando interattivo che non disabilita la finestra.
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
                    return "SOLIDWORKS e' occupato in un comando: chiudilo e riprova.";
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
