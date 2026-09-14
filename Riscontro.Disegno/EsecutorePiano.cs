using System;
using System.Collections.Generic;
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

            // Prima di toccare qualsiasi cosa: il documento e' nostro?
            var controllo = (string)await _sulThreadSw(() => ControlliPreliminari());
            if (controllo != null)
            {
                risultato.Errore = controllo;
                return risultato;
            }

            // Se il piano non dice su quale documento vale, si prende quello
            // attivo adesso — ma UNA volta sola, e da qui in poi e' quello.
            // Prima non si confrontava mai niente: fra la lettura dello stato e
            // l'esecuzione c'e' un giro di rete, e in quella finestra l'utente
            // puo' avere cambiato finestra. La lavorazione finiva nell'altro
            // documento, in check-out, dentro una sola voce di annullamento che
            // nessuno sa di dover premere.
            if (string.IsNullOrEmpty(percorsoAtteso))
            {
                percorsoAtteso = (string)await _sulThreadSw(
                    () => Identita(_sw.IActiveDoc2));
            }
            if (string.IsNullOrEmpty(percorsoAtteso))
            {
                risultato.Errore =
                    "Non so su quale documento dovrei lavorare: salvalo su disco e riprova.";
                return risultato;
            }

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
                        var qui = Identita(modello);
                        if (!string.Equals(qui, percorsoAtteso, StringComparison.OrdinalIgnoreCase))
                        {
                            return new OperazioniSw.Esito
                            {
                                Riuscito = false,
                                Errore =
                                    "Il documento attivo e' cambiato mentre eseguivo: " +
                                    "il piano era per «" + percorsoAtteso + "». " +
                                    "Non ho toccato niente.",
                            };
                        }

                        var lettura = StatoDocumento.Leggi(modello);
                        var operazioni = new OperazioniSw(_sw, _log);
                        var e = operazioni.Esegui(
                            modello, passo.Operazione, passo.Argomenti, lettura, passiFatti);
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
                    // Si chiude la voce di annullamento sul documento GIUSTO:
                    // se l'utente e' passato a un'altra finestra, chiuderla li'
                    // raggrupperebbe sotto il nostro nome modifiche che non
                    // sono nostre.
                    var m = _sw.IActiveDoc2;
                    var qui = Identita(m);
                    if (string.Equals(qui, percorsoAtteso, StringComparison.OrdinalIgnoreCase))
                    {
                        try { m.Extension.FinishRecordingUndoObject(_nomeAnnullamento); } catch { }
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
        /// </summary>
        private string ControlliPreliminari()
        {
            var modello = _sw.IActiveDoc2;
            if (modello == null) return "Non c'e' nessun documento aperto.";
            if (modello.GetType() != (int)swDocumentTypes_e.swDocPART)
                return "L'assistente lavora solo su una parte, non su assiemi o disegni.";

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

            try
            {
                if (_sw.CommandInProgress)
                    return "SOLIDWORKS e' occupato in un comando: chiudilo e riprova.";
            }
            catch { }

            return null;
        }
    }
}
