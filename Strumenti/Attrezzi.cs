using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorksMcp.Mcp;
using SolidWorksMcp.Ponte;

namespace SolidWorksMcp.Strumenti
{
    /// <summary>
    /// I quattro strumenti che il ponte espone.
    ///
    /// PERCHE' QUATTRO E NON TRENTASEI.
    /// Le operazioni di disegno sono trentasei, e la tentazione e' di esporne
    /// una per strumento. Sarebbe sbagliato per due ragioni misurate nel
    /// progetto:
    ///
    ///   1. **il costo.** Gli schemi interi delle trentasei operazioni sono
    ///      decine di migliaia di caratteri in ogni singola richiesta. Il
    ///      catalogo a due livelli del server esiste esattamente per questo.
    ///   2. **l'ordine giusto.** Il difetto piu' caro di questo progetto non e'
    ///      un'operazione sbagliata: e' un'operazione giusta fatta ALLA CIECA.
    ///      Quattro strumenti in fila — guarda, cerca, approfondisci, fai —
    ///      rendono difficile saltare il primo.
    ///
    /// `guarda_il_pezzo` non e' un accessorio: e' il primo, e senza di lui gli
    /// identificativi che gli altri vogliono (`F1`, `E4`, `Corpo1`) non
    /// esistono.
    /// </summary>
    internal sealed class Attrezzi
    {
        private readonly Sessione _sessione;
        private readonly Catalogo _catalogo;

        public Attrezzi(Sessione sessione, Catalogo catalogo)
        {
            _sessione = sessione;
            _catalogo = catalogo;
        }

        public object Elenco()
        {
            return new object[]
            {
                Strumento(
                    "guarda_il_pezzo",
                    "GUARDA il documento aperto in SolidWorks prima di toccarlo: " +
                    "ingombro, volume, corpi, lavorazioni con le loro quote, facce " +
                    "e spigoli con il loro identificativo. Gli identificativi (F1, " +
                    "E4, Corpo1) che le altre operazioni vogliono nascono QUI e " +
                    "valgono per questa lettura: dopo ogni modifica si rilegge.",
                    Oggetto(new Dictionary<string, object>(), new string[0]),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "elenco_operazioni",
                    "L'elenco corto di cosa si puo' chiedere, per famiglia: un " +
                    "nome e una riga ciascuna. Per lo schema completo di una, usa " +
                    "«descrivi_operazione».",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["famiglia"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "solo una famiglia: estrusioni, lavorazioni, " +
                                    "riferimenti, corpi, saldature, proprieta', tavola",
                            },
                        },
                        new string[0]),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "descrivi_operazione",
                    "Lo schema completo di una operazione: cosa vuole, in che " +
                    "unita', cosa deve esserci gia' nel documento.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["nome"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "il nome esatto, da «elenco_operazioni»",
                            },
                        },
                        new[] { "nome" }),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "disegna",
                    "Esegue in SolidWorks una sequenza di operazioni e RIPORTA " +
                    "COS'E' VENUTO: le misure di ogni passo e lo stato del " +
                    "documento dopo.\n\n" +
                    "Il piano viene controllato TUTTO prima di cominciare: se un " +
                    "passo qualunque ha un nome sbagliato o argomenti che non " +
                    "rispettano lo schema, non viene eseguito niente e il " +
                    "documento resta com'era.\n\n" +
                    "Se invece un passo fallisce mentre viene eseguito, ci si " +
                    "ferma li': i passi seguenti quasi sempre nominano cio' che " +
                    "il passo fallito avrebbe dovuto creare. Quello che era gia' " +
                    "stato costruito resta, e un solo Ctrl+Z lo toglie tutto.\n\n" +
                    "Un passo puo' nominare la geometria creata da un passo " +
                    "PRECEDENTE dello stesso piano (per esempio faccia_rif: " +
                    "{passo: 1, genere: \"faccia_superiore\"}): e' l'unico modo, " +
                    "perche' quella faccia non ha ancora nessun identificativo.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["passi"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "le operazioni da fare, in ordine",
                                ["items"] = Oggetto(
                                    new Dictionary<string, object>
                                    {
                                        ["operazione"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] = "il nome dell'operazione",
                                        },
                                        ["argomenti"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "gli argomenti, come da «descrivi_operazione»",
                                        },
                                    },
                                    new[] { "operazione" }),
                            },
                            ["documento"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Su quale documento disegnare: il titolo o il " +
                                    "percorso, copiato da «guarda_il_pezzo». Serve " +
                                    "SOLO se il documento davanti e' cambiato da " +
                                    "quando l'hai guardato: normalmente si omette. " +
                                    "Nominarlo vuol dire «so quale pezzo sto " +
                                    "toccando», e un nome che non corrisponde viene " +
                                    "rifiutato.",
                            },
                        },
                        new[] { "passi" }),
                    // Rieseguire lo stesso piano NON rimette il pezzo
                    // com'era: fa un secondo foro. Dichiararlo idempotente
                    // inviterebbe un client a riprovare da solo dopo un
                    // errore di rete.
                    soloLettura: false, distruttivo: true, idempotente: false),
            };
        }

        public Dictionary<string, object> Chiama(
            string nome, Dictionary<string, object> argomenti)
        {
            switch (nome)
            {
                case "guarda_il_pezzo": return GuardaIlPezzo();
                case "elenco_operazioni": return ElencoOperazioni(argomenti);
                case "descrivi_operazione": return DescriviOperazione(argomenti);
                case "disegna": return Disegna(argomenti);
                default:
                    return Protocollo.Contenuto(
                        "Non conosco lo strumento «" + nome + "».", true);
            }
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// L'ultimo documento che «guarda_il_pezzo» ha descritto.
        ///
        /// Non e' un ricordo di comodo: e' il contratto fra i due strumenti.
        /// Un piano nasce guardando UN documento, e va eseguito su QUELLO.
        /// </summary>
        private Dictionary<string, object> _guardato;

        /// <summary>
        /// Com'erano fatte le entita' quando «guarda_il_pezzo» le ha
        /// descritte: uid -> punto, normale, area, lunghezza.
        ///
        /// GLI UID SONO POSIZIONALI. L'add-in li riassegna a ogni lettura
        /// ordinando le facce per area, quindi basta un passo che cambia
        /// il pezzo perche' «F1» indichi un'altra faccia. Un piano pero'
        /// nasce da UNA lettura: i suoi uid parlano di quel momento li'.
        ///
        /// MISURATO il 14 settembre 2026, tre passi in una sola chiamata a
        /// «disegna» su un blocco 200x100x20:
        ///   1. estrudi il blocco          -> F1 = faccia di sopra, 20.000 mm2
        ///   2. tasca Ø60 profonda 5 su F1 -> la faccia di sopra scende a
        ///      17.172,57 mm2 e passa DIETRO a quella di sotto nell'ordine
        ///   3. tasca Ø10 profonda 5 su F1 -> «F1» adesso e' quella di SOTTO
        /// Tutti e tre i passi hanno risposto «riuscita: true». La tasca
        /// piccola e' finita sulla faccia sbagliata, le profondita' erano
        /// quelle chieste, e il volume tornava: 385.470,1 mm3, esatto. Non
        /// c'era niente, in quella risposta, che facesse sospettare.
        ///
        /// L'add-in sa gia' difendersi: legge «_firme» dagli argomenti e
        /// confronta. Ma quella chiave la scriveva solo il server di
        /// OpenPDM, e qui non la scriveva nessuno — quindi il confronto
        /// non aveva termini e passava sempre. Questo campo e' il termine
        /// che mancava.
        /// </summary>
        private Dictionary<string, object> _firmeViste;

        private Dictionary<string, object> GuardaIlPezzo()
        {
            var stato = _sessione.Stato(out var errore);
            if (stato == null) return Protocollo.Contenuto(errore, true);
            _guardato = IdentitaDelloStato(stato);
            _firmeViste = FirmeDelloStato(stato);
            TogliLAncora(stato);
            return Protocollo.Contenuto(Protocollo.InJson(stato));
        }

        /// <summary>
        /// L'ANCORA NON SI MANDA A CHI LEGGE, e non e' per risparmiare posto —
        /// anche se ne risparmia.
        ///
        /// `punto_mm` e `ancora` fanno due mestieri diversi e hanno due
        /// destinatari diversi:
        ///
        ///   * `punto_mm` serve a **selezionare**: e' un punto garantito sulla
        ///     faccia, e chi legge lo usa per dire dove lavorare;
        ///   * `ancora` serve a **riconoscere**: e' il baricentro d'area, che
        ///     non sta necessariamente sulla faccia (quello di una faccia a
        ///     ferro di cavallo cade nel vuoto) e non si puo' usare per
        ///     selezionare niente.
        ///
        /// Mandarli tutti e due vorrebbe dire offrire due punti a chi ne deve
        /// usare uno, e la scelta sbagliata non darebbe errore: darebbe un foro
        /// fuori dal materiale.
        ///
        /// E il posto conta lo stesso: su un telaio da quattro barre l'ancora
        /// su sessanta facce sono **1.400 caratteri**, e lo stato passava da
        /// 15.700 a 17.171 — oltre il tetto dei 16 KB che il banco sorveglia.
        ///
        /// Si toglie DOPO aver costruito le firme, che se la portano dietro.
        /// </summary>
        private static void TogliLAncora(Dictionary<string, object> stato)
        {
            if (stato == null) return;
            var facce = (stato.TryGetValue("facce", out var g) ? g : null)
                        as System.Collections.IEnumerable;
            if (facce == null) return;
            foreach (var v in facce)
                (v as Dictionary<string, object>)?.Remove("ancora");
        }

        /// <summary>
        /// Cosa basta per riconoscere la stessa entita' fra due letture.
        ///
        /// Gli stessi quattro campi che usa il server di OpenPDM, cosi' le
        /// due strade dicono la stessa cosa all'add-in. Chi confronta
        /// davvero e' lui, e guarda punto e normale: l'area di una faccia
        /// cambia in modo legittimo — ci si fa un foro — mentre
        /// l'orientamento no.
        /// </summary>
        private static Dictionary<string, object> FirmeDelloStato(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            var fuori = new Dictionary<string, object>();
            // I CORPI SONO ENTITA' COME LE ALTRE: per un saldato sono LE
            // entita', e «tappa le estremita' di C1» nomina corpi.
            foreach (var famiglia in new[] { "facce", "spigoli", "feature", "assi", "corpi" })
            {
                // SI SCORRE COME IEnumerable, NON SI CASTA A object[].
                // Lo stato arriva dall'add-in con i suoi tipi veri, e la' le
                // famiglie sono List<Dictionary<string, object>>: un
                // «as object[]» torna null in silenzio, e il 14 settembre 2026
                // questo controllo e' rimasto spento per quello. Passando da
                // IEnumerable va bene tutte e due le forme, anche quella che
                // arriva da un JSON.
                var voci = (stato.TryGetValue(famiglia, out var g) ? g : null)
                           as System.Collections.IEnumerable;
                if (voci == null) continue;
                foreach (var v in voci)
                {
                    var voce = v as Dictionary<string, object>;
                    if (voce == null || !voce.TryGetValue("uid", out var uid) || uid == null)
                        continue;
                    var firma = new Dictionary<string, object>();
                    // `lavorazione` e `lavorazione_id` servono alla
                    // corroborazione del ritrovamento (voce E3): senza, una
                    // faccia di un'altra lavorazione finita nello stesso posto
                    // e' indistinguibile da quella che si cercava.
                    // `centro_mm` e `ingombro_mm` sono la firma dei CORPI, e
                    // fino alla 0.24.0 non venivano copiati: per «C1» la firma
                    // usciva vuota, `FirmaCombacia` non aveva niente da
                    // confrontare e rispondeva sempre di si'. I corpi erano
                    // controllati per ESISTENZA e non per IDENTITA'.
                    foreach (var campo in new[]
                             {
                                 "punto_mm", "normale", "area_mm2", "lunghezza_mm",
                                 "lavorazione_id", "centro_mm", "ingombro_mm", "ancora",
                                 "feature_id",
                             })
                        if (voce.TryGetValue(campo, out var x) && x != null)
                            firma[campo] = x;

                    // IL NOME DELLA LAVORAZIONE SI RISOLVE QUI.
                    //
                    // Lo stato lo scrive una volta sola, in una mappa in fondo,
                    // perche' ripeterlo su sessanta facce faceva sforare il
                    // tetto dei 16 KB. La firma invece se lo porta esteso: non
                    // va a nessun modello — e' un allegato interno al passo — e
                    // serve a scrivere un rifiuto che nomina la lavorazione
                    // attesa invece di darne il numero.
                    if (firma.TryGetValue("lavorazione_id", out var idLav) && idLav != null)
                    {
                        var nome = NomeLavorazione(stato, idLav);
                        if (nome != null) firma["lavorazione"] = nome;
                    }
                    if (firma.Count > 0) fuori[Convert.ToString(uid)] = firma;
                }
            }
            return fuori.Count > 0 ? fuori : null;
        }

        /// <summary>Titolo e percorso presi dallo stato, senza ripassare dal COM.</summary>
        private static Dictionary<string, object> IdentitaDelloStato(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return new Dictionary<string, object>
            {
                ["titolo"] = stato.TryGetValue("titolo", out var t) ? t : "",
                ["percorso"] = stato.TryGetValue("percorso", out var p) ? p : "",
            };
        }

        private static string NomeLeggibile(Dictionary<string, object> identita)
        {
            if (identita == null) return "(nessuno)";
            var p = Convert.ToString(identita.TryGetValue("percorso", out var x) ? x : "");
            if (!string.IsNullOrWhiteSpace(p)) return p;
            var t = Convert.ToString(identita.TryGetValue("titolo", out var y) ? y : "");
            return string.IsNullOrWhiteSpace(t) ? "(senza nome)" : t;
        }

        private Dictionary<string, object> ElencoOperazioni(
            Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);
            var famiglia = Testo(argomenti, "famiglia");
            return Protocollo.Contenuto(Protocollo.InJson(_catalogo.Elenco(famiglia)));
        }

        private Dictionary<string, object> DescriviOperazione(
            Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);

            var nome = Testo(argomenti, "nome");
            var descrizione = _catalogo.Descrizione(nome);
            if (descrizione == null)
            {
                // UN ERRORE CHE AIUTA invece di uno che constata: chi ha
                // sbagliato nome quasi sempre ne ha in mente uno vicino.
                var simili = _catalogo.Simili(nome);
                return Protocollo.Contenuto(
                    "Non c'e' nessuna operazione che si chiama «" + nome + "»." +
                    (simili.Count > 0
                        ? " Forse cercavi: " + string.Join(", ", simili) + "."
                        : " Chiedi «elenco_operazioni» per vedere quali ci sono."),
                    true);
            }
            return Protocollo.Contenuto(Protocollo.InJson(descrizione));
        }

        /// <summary>
        /// Il giro vero: si esegue, e SI GUARDA COS'E' VENUTO.
        ///
        /// Il risultato non e' «riuscito». Sono le misure di ogni passo — il
        /// volume tolto, il diametro vero del foro, quante pareti ha forato —
        /// e lo stato del documento DOPO. E' l'unica forma che permette a chi
        /// ha chiesto di accorgersi che il pezzo non e' quello che voleva.
        /// </summary>
        /// <summary>
        /// La frase che si aggiunge a ogni rifiuto di FORMA.
        ///
        /// Il piano si controlla tutto prima di cominciare, quindi un errore al
        /// passo 3 ferma anche i primi due. La risposta pero' elenca solo il
        /// passo che ha sbagliato, e senza questa frase chi legge puo' credere
        /// che i precedenti siano stati eseguiti — e mettersi a cercare nel
        /// documento una piastra che non c'e'.
        /// </summary>
        private const string NienteToccato =
            " Il piano non e' stato eseguito per niente: il documento e' rimasto " +
            "com'era. Correggi questo passo e rimanda il piano intero.";

        private Dictionary<string, object> Disegna(Dictionary<string, object> argomenti)
        {
            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            var passi = Prendi(argomenti, "passi") as object[];
            if (passi == null || passi.Length == 0)
                return Protocollo.Contenuto(
                    "Il piano e' vuoto: «passi» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            var prima = _sessione.Stato(out var erroreLettura);
            if (prima == null)
                return Protocollo.Contenuto(erroreLettura, true);

            // SI DISEGNA SUL DOCUMENTO CHE SI E' GUARDATO, NON SU QUELLO DAVANTI.
            //
            // Il ponte lavora sul documento ATTIVO, e il documento attivo e'
            // quello la cui finestra sta davanti: cambia se qualcuno clicca,
            // se un'altra operazione ne apre uno, se SolidWorks ne porta uno in
            // primo piano da solo. Il 14 settembre 2026 e' successo davvero —
            // un blocco 314x274x52 estruso dentro il pezzo di qualcun altro,
            // senza un errore, senza un avviso.
            //
            // Qui non si indovina e non si cambia finestra da soli: si
            // CONFRONTA con quello che «guarda_il_pezzo» aveva descritto, e se
            // non combacia ci si ferma. Chi sa cosa sta facendo puo' dire
            // esplicitamente «si', quello davanti» con `qualunque_documento`.
            var adesso = IdentitaDelloStato(prima);
            var nominato = Testo(argomenti, "documento");

            // SI NOMINA IL DOCUMENTO, NON SI SPEGNE IL CONTROLLO.
            //
            // La prima versione aveva un `qualunque_documento: true` per
            // scavalcare il rifiuto. Era un difetto: un argomento che
            // disattiva una difesa e' un invito a disattivarla, e chi lo
            // incontra ha tutti gli incentivi per riprovare con quello
            // acceso — cioe' la difesa sparisce proprio nel caso in cui
            // serviva.
            //
            // Nominare il documento chiede la stessa cosa in una forma che
            // non si puo' dare a caso: per scriverne il nome bisogna averlo
            // guardato. Un booleano lo mette chiunque; un nome sbagliato
            // viene rifiutato come tutto il resto.
            bool loHaNominato =
                !string.IsNullOrWhiteSpace(nominato) &&
                Ponte.Sessione.StessoDocumento(
                    adesso,
                    new Dictionary<string, object>
                    {
                        [nominato.IndexOfAny(new[] { '\\', '/' }) >= 0
                            ? "percorso" : "titolo"] = nominato,
                    });

            if (!loHaNominato)
            {
                if (_guardato == null)
                    return Protocollo.Contenuto(
                        "Non ho ancora guardato nessun pezzo, quindi non so su quale " +
                        "documento vuoi che disegni: adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Chiama prima «guarda_il_pezzo» — " +
                        "serve comunque, perche' gli identificativi delle facce " +
                        "nascono li'.", true);

                if (!Ponte.Sessione.StessoDocumento(_guardato, adesso))
                    return Protocollo.Contenuto(
                        "Mi fermo: il documento e' cambiato. Avevi guardato «" +
                        NomeLeggibile(_guardato) + "», adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Disegnare qui vorrebbe dire " +
                        "scrivere nel pezzo sbagliato, e gli identificativi del tuo " +
                        "piano non valgono su questo.\n\n" +
                        "Se e' cambiato per una ragione che conosci, rileggilo con " +
                        "«guarda_il_pezzo». Se sai gia' che vuoi lavorare proprio su " +
                        "quello che c'e' adesso, ripeti passando «documento»: \"" +
                        NomeLeggibile(adesso) + "\".", true);
            }
            // LE FIRME VALGONO SOLO SUL DOCUMENTO CHE LE HA PRODOTTE.
            // Se il piano nomina un altro documento, gli uid del piano non
            // parlano di questo e allegare le firme vecchie produrrebbe
            // rifiuti che non c'entrano niente. Meglio nessun confronto che
            // un confronto fra due pezzi diversi.
            var firme =
                _guardato != null && Ponte.Sessione.StessoDocumento(_guardato, adesso)
                    ? _firmeViste
                    : null;

            _guardato = adesso;

            // IL PIANO SI CONTROLLA TUTTO PRIMA DI TOCCARE IL DOCUMENTO.
            //
            // Nome dell'operazione, percorsi e forma degli argomenti si vedono
            // leggendo, senza aprire niente. Prima si controllavano a mano a
            // mano: un errore di forma al passo 3 fermava il piano con i primi
            // due GIA' COSTRUITI, e nel documento restava mezzo pezzo per una
            // cosa che si poteva dire prima di cominciare.
            //
            // Adesso o il piano e' scrivibile tutto, o non si tocca niente.
            var daFare = new List<Riscontro.Disegno.PassoDelPiano>();
            var esiti = new List<object>();
            bool tuttoBene = true;
            int ordine = 0;

            foreach (var p in passi)
            {
                var passo = p as Dictionary<string, object>;
                if (passo == null) continue;
                ordine++;

                var nome = Testo(passo, "operazione");
                var suoi = Prendi(passo, "argomenti") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();

                // SI CONTROLLA IL NOME PRIMA DI ESEGUIRE. L'add-in, davanti a
                // un nome che non conosce ma con dei «contorni» dentro, ricade
                // sull'estrusione di profilo: comodo per le ricette del server,
                // pericoloso qui, dove un nome storto diventerebbe un pezzo
                // storto senza che nessuno dica niente.
                if (!_catalogo.Conosce(nome))
                {
                    var simili = _catalogo.Simili(nome);
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] =
                            "Non esiste nessuna operazione «" + nome + "»." +
                            (simili.Count > 0
                                ? " Forse cercavi: " + string.Join(", ", simili) + "."
                                : "") + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // I PERCORSI SI CONTROLLANO PRIMA DI ESEGUIRE.
                // Sono l'unica cosa che questo ponte scrive fuori dal
                // documento aperto, e arrivano da un testo generato.
                var percorsoStorto = Ponte.Percorsi.Controlla(suoi);
                if (percorsoStorto != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = percorsoStorto + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // GLI ARGOMENTI RISPETTANO LO SCHEMA CHE ABBIAMO
                // DICHIARATO NOI? Il ponte offre trentasei operazioni con
                // schemi dettagliati: non guardarli vuol dire che lo
                // schema serve solo a suggerire, e che uno «spessore_mm»
                // dimenticato arriva a valle come zero — un'estrusione di
                // spessore nullo, rifiutata da SOLIDWORKS con un messaggio
                // suo, nel migliore dei casi.
                //
                // SOLO LA FORMA, MAI IL MERITO: vedi Ponte/Forma.cs.
                var formaStorta = Ponte.Forma.Controlla(
                    nome, _catalogo.Schema(nome), suoi);
                if (formaStorta != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = formaStorta + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // GLI IDENTIFICATIVI CITATI ESISTONO DAVVERO?
                //
                // E' l'altra meta' della difesa anti-deriva, e prende un caso
                // che le FIRME non possono prendere: un piano che crea il pezzo
                // da zero e poi cita «F1» non ha nessuna firma da confrontare,
                // perche' quando lo stato e' stato letto quella faccia non
                // c'era. Nessuna firma, nessun confronto, e il piano passa.
                //
                // Misurato il 14/9/2026: `[estrudi_rettangolo, taglio_cilindrico
                // su F1]` su un documento vuoto riusciva, volume 117.738,1. Il
                // foro e' finito sulla faccia piu' grande — che in quel caso era
                // anche quella giusta, ma **non l'aveva scelta nessuno**: «F1»
                // e' posizionale e si rinumera per area a ogni lettura.
                var uidStorto = UidCheNonEsistono(
                    _catalogo.Schema(nome), suoi, prima);
                if (uidStorto != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = uidStorto + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // LE FIRME DI QUANDO IL PIANO E' NATO, ALLEGATE AL PASSO.
                // Non si sovrascrive quello che il chiamante ha gia' messo:
                // un piano che porta le sue firme sa meglio di noi di cosa
                // sta parlando.
                if (firme != null && !suoi.ContainsKey("_firme"))
                    suoi["_firme"] = firme;

                daFare.Add(new Riscontro.Disegno.PassoDelPiano
                {
                    Ordine = ordine,
                    Operazione = nome,
                    Argomenti = suoi,
                    Riassunto = nome,
                });
            }

            // IL GIRO E' QUELLO DELLA LIBRERIA, NON UNO NOSTRO.
            // Da qui arrivano i riferimenti fra passi («la faccia in cima a
            // quello che hai fatto al passo 1») e la voce di annullamento
            // unica: un solo Ctrl+Z disfa il piano intero.
            if (tuttoBene)
            {
                var fatti = _sessione.EseguiPiano(daFare, out var erroreGiro);
                if (fatti != null) foreach (var e in fatti) esiti.Add(e);
                if (!string.IsNullOrEmpty(erroreGiro))
                {
                    tuttoBene = false;
                    // Un piano fermato PRIMA del primo passo non lascia nessun
                    // esito: senza questo, la risposta sarebbe un elenco vuoto
                    // e un «riuscita» implicito che nessuno ha detto.
                    if (fatti == null || fatti.Count == 0)
                        esiti.Add(new Dictionary<string, object>
                        {
                            ["operazione"] = daFare.Count > 0 ? daFare[0].Operazione : null,
                            ["riuscita"] = false,
                            ["errore"] = erroreGiro,
                        });
                }
            }

            var dopo = _sessione.Stato(out var erroreStato);

            // «stato_dopo» E' LA LETTURA SUCCESSIVA, E VA FIRMATA COME TALE.
            //
            // Questa risposta porta con se' lo stato del documento dopo i
            // passi, con gli identificativi nuovi: chi legge scrivera' il
            // piano seguente su QUESTI, non su quelli di «guarda_il_pezzo».
            // Se le firme restassero quelle di prima, il piano seguente —
            // scritto giusto — verrebbe rifiutato perche' confrontato con una
            // lettura che il suo autore non ha mai visto. Sarebbe un rifiuto
            // di cio' che e' corretto, che e' il difetto peggiore dei due.
            if (dopo != null) { _firmeViste = FirmeDelloStato(dopo); TogliLAncora(dopo); }

            var risultato = new Dictionary<string, object>
            {
                // SI DICE SEMPRE SU COSA SI E' LAVORATO. Anche quando e' ovvio:
                // e' l'unico modo perche' chi legge se ne accorga quando ovvio
                // non era.
                ["documento"] = NomeLeggibile(adesso),
                ["esiti"] = esiti,
                ["stato_dopo"] = dopo,
                ["nota_stato"] = dopo == null ? erroreStato : null,
                ["volume_prima_mm3"] = Volume(prima),
                ["volume_dopo_mm3"] = Volume(dopo),
            };

            // IL CONTEGGIO DEGLI ERRORI SI DA' COME DELTA, NON COME NUMERO.
            //
            // «lavorazioni_in_errore: 1» dopo un piano sembra dire «il piano ha
            // rotto qualcosa». Puo' voler dire l'opposto: il pezzo era gia'
            // rotto quando e' arrivato, e il piano non c'entra. Il numero
            // assoluto mente su chi sia stato, ed e' il rischio scritto nella
            // voce D5.
            //
            // Si dice quanti ce n'erano prima solo quando cambia qualcosa: su
            // un pezzo sano che resta sano non c'e' niente da dire, e ripeterlo
            // a ogni risposta e' posto tolto a cio' che conta.
            int erroriPrima = LavorazioniInErrore(prima);
            int erroriDopo = LavorazioniInErrore(dopo);
            if (erroriPrima > 0 || erroriDopo > 0)
            {
                risultato["lavorazioni_in_errore_prima"] = erroriPrima;
                risultato["nota_errori"] =
                    erroriDopo > erroriPrima
                        ? "Il pezzo ha " + erroriDopo + " lavorazioni in errore, e prima " +
                          "ne aveva " + erroriPrima + ": questo piano ne ha rotte " +
                          (erroriDopo - erroriPrima) + ". Guarda «stato_dopo» per sapere quali."
                        : erroriDopo < erroriPrima
                            ? "Le lavorazioni in errore sono passate da " + erroriPrima +
                              " a " + erroriDopo + ": questo piano ne ha riparate " +
                              (erroriPrima - erroriDopo) + "."
                            : "Il pezzo aveva gia' " + erroriPrima + " lavorazioni in errore " +
                              "prima di questo piano, e ne ha ancora " + erroriDopo + ": " +
                              "non le ha rotte questo piano.";
            }

            return Protocollo.Contenuto(Protocollo.InJson(risultato), !tuttoBene);
        }

        // ─────────────────────────────────────────────────────────────────

        private static object Volume(Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return stato.TryGetValue("volume_mm3", out var v) ? v : null;
        }

        /// <summary>
        /// GLI IDENTIFICATIVI CHE IL PASSO CITA E CHE NELLO STATO NON C'ERANO.
        /// Null se e' tutto a posto.
        ///
        /// Si guardano solo i campi che lo schema chiama `*_uid`: sono quelli
        /// che nominano geometria **che c'era gia'**. La geometria creata dal
        /// piano stesso si nomina con `*_rif`, che cita il PASSO e non
        /// l'identificativo — ed e' l'unico modo, perche' al momento in cui il
        /// piano si scrive quella faccia non esiste e nessuno le puo' dare un
        /// nome.
        ///
        /// PERCHE' NON BASTAVANO LE FIRME. La firma confronta un identificativo
        /// con la geometria che descriveva quando il piano e' nato. Se la
        /// faccia **non esisteva affatto**, non c'e' nessuna firma: niente da
        /// confrontare, nessun rifiuto, e il piano passa. La firma protegge
        /// dallo spostamento; questo protegge dall'invenzione.
        ///
        /// NON SI RIFIUTA IL LEGITTIMO. Un piano che crea geometria e poi ci
        /// lavora sopra e' normale, e passa: usa `*_rif`. Il messaggio lo dice,
        /// perche' un rifiuto che non indica la strada buona insegna soltanto a
        /// riprovare a caso.
        /// </summary>
        private static string UidCheNonEsistono(
            Dictionary<string, object> schema,
            Dictionary<string, object> argomenti,
            Dictionary<string, object> stato)
        {
            if (schema == null || argomenti == null || stato == null) return null;
            object grezzoProp;
            if (!schema.TryGetValue("properties", out grezzoProp)) return null;
            var proprieta = grezzoProp as Dictionary<string, object>;
            if (proprieta == null) return null;

            var esistenti = UidDelloStato(stato);

            foreach (var campo in proprieta.Keys)
            {
                if (!campo.EndsWith("_uid", StringComparison.Ordinal)) continue;
                object valore;
                if (!argomenti.TryGetValue(campo, out valore) || valore == null) continue;

                foreach (var uid in Stringhe(valore))
                {
                    if (esistenti.Contains(uid)) continue;

                    // SI DICE COSA C'E', non solo cosa manca. Su un documento
                    // vuoto l'elenco e' vuoto, ed e' proprio quello il caso in
                    // cui chi legge deve capire che deve cambiare strada.
                    string cosaCe = esistenti.Count == 0
                        ? "Nel pezzo che ho guardato non c'era nessun identificativo: " +
                          "era vuoto."
                        : "Quelli che c'erano sono: " + Elenca(esistenti) + ".";

                    return
                        "«" + campo + "» cita «" + uid + "», che nel pezzo che ho " +
                        "guardato non c'era. " + cosaCe + "\n\n" +
                        "Gli identificativi nascono da «guarda_il_pezzo» e valgono per " +
                        "QUELLA lettura. Se la geometria la crea un passo precedente di " +
                        "questo stesso piano, non ha ancora nessun identificativo e va " +
                        "nominata con il riferimento al passo — per esempio " +
                        "\"" + campo.Replace("_uid", "_rif") + "\": {\"passo\": 1, " +
                        "\"genere\": \"faccia_superiore\"}. Se invece c'era gia', " +
                        "rileggi il pezzo e copia l'identificativo da li'.";
                }
            }
            return null;
        }

        /// <summary>
        /// Tutti gli identificativi che quello stato descriveva: facce,
        /// spigoli, corpi, assi e lavorazioni.
        /// </summary>
        private static HashSet<string> UidDelloStato(Dictionary<string, object> stato)
        {
            var fuori = new HashSet<string>(StringComparer.Ordinal);
            foreach (var famiglia in new[] { "facce", "spigoli", "corpi", "assi", "feature" })
            {
                object grezzo;
                if (!stato.TryGetValue(famiglia, out grezzo) || grezzo == null) continue;
                // Le famiglie sono List<Dictionary<...>>, non object[]: un
                // `as object[]` qui tornerebbe null in silenzio e il controllo
                // direbbe «non esiste» su tutto. E' gia' successo una volta,
                // con le firme.
                var elenco = grezzo as System.Collections.IEnumerable;
                if (elenco == null) continue;
                foreach (var v in elenco)
                {
                    var voce = v as Dictionary<string, object>;
                    object uid;
                    if (voce != null && voce.TryGetValue("uid", out uid) && uid != null)
                        fuori.Add(Convert.ToString(uid));
                }
            }
            return fuori;
        }

        /// <summary>Un valore che puo' essere una stringa o un elenco di stringhe.</summary>
        private static IEnumerable<string> Stringhe(object valore)
        {
            if (valore is string s) { if (s.Length > 0) yield return s; yield break; }
            if (valore is System.Collections.IEnumerable elenco)
            {
                foreach (var v in elenco)
                {
                    var t = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(t)) yield return t;
                }
            }
        }

        /// <summary>
        /// L'elenco, ma corto. Su un pezzo con quattrocento facce scriverli
        /// tutti non aiuta nessuno: i primi dodici bastano a far capire che
        /// forma hanno i nomi.
        /// </summary>
        private static string Elenca(HashSet<string> uid)
        {
            var ordinati = uid.ToList();
            ordinati.Sort(StringComparer.Ordinal);
            if (ordinati.Count <= 12) return string.Join(", ", ordinati);
            return string.Join(", ", ordinati.Take(12)) +
                   " e altri " + (ordinati.Count - 12);
        }

        /// <summary>
        /// Il nome di una lavorazione, dalla mappa che lo stato scrive in
        /// fondo. Null se non c'e': meglio una firma senza nome che una firma
        /// con un nome inventato.
        /// </summary>
        private static string NomeLavorazione(Dictionary<string, object> stato, object id)
        {
            if (stato == null || id == null) return null;
            if (!stato.TryGetValue("lavorazioni", out var g) || g == null) return null;
            var mappa = g as Dictionary<string, object>;
            if (mappa == null) return null;
            var chiave = Convert.ToString(id);
            return mappa.TryGetValue(chiave, out var nome) && nome != null
                ? Convert.ToString(nome)
                : null;
        }

        /// <summary>
        /// Quante lavorazioni in errore aveva quello stato. Zero anche quando
        /// il campo non c'e' — ed e' giusto: lo stato lo scrive solo quando ce
        /// ne sono.
        /// </summary>
        private static int LavorazioniInErrore(Dictionary<string, object> stato)
        {
            if (stato == null) return 0;
            object v;
            if (!stato.TryGetValue("lavorazioni_in_errore", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static object Prendi(Dictionary<string, object> d, string chiave)
        {
            if (d == null) return null;
            return d.TryGetValue(chiave, out var v) ? v : null;
        }

        private static string Testo(Dictionary<string, object> d, string chiave)
        {
            var v = Prendi(d, chiave);
            return v == null ? null : Convert.ToString(v);
        }


        private static Dictionary<string, object> Oggetto(
            Dictionary<string, object> proprieta, string[] obbligatori)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = proprieta,
                ["required"] = obbligatori,
            };
        }

        /// <summary>
        /// Uno strumento, con le sue ANNOTAZIONI.
        ///
        /// PERCHE' LE ANNOTAZIONI NON SONO UN DETTAGLIO DI PROTOCOLLO.
        /// Il client MCP le usa per decidere se fermarsi a chiedere conferma a
        /// chi sta lavorando. Senza, `guarda_il_pezzo` — che non tocca niente —
        /// e `disegna` — che modifica il pezzo in modo che nessuno annulla per
        /// lui — arrivano al client identici, e il client tratta identiche due
        /// cose che non lo sono.
        ///
        /// Per un progetto la cui tesi e' «nessuna geometria esiste senza che
        /// qualcuno l'abbia confermata», dichiararle e' il minimo.
        ///
        /// `idempotente` merita una riga: `disegna` NON lo e'. Rieseguire lo
        /// stesso piano non rimette il pezzo com'era — fa un SECONDO foro.
        /// Dichiararlo idempotente inviterebbe un client a riprovare da solo
        /// dopo un errore di rete, e il pezzo uscirebbe con il doppio delle
        /// lavorazioni senza che nessuno l'abbia chiesto.
        /// </summary>
        private static Dictionary<string, object> Strumento(
            string nome,
            string descrizione,
            Dictionary<string, object> schema,
            bool soloLettura,
            bool distruttivo,
            bool idempotente)
        {
            return new Dictionary<string, object>
            {
                ["name"] = nome,
                ["description"] = descrizione,
                ["inputSchema"] = schema,
                ["annotations"] = new Dictionary<string, object>
                {
                    ["title"] = TitoloUmano(nome),
                    ["readOnlyHint"] = soloLettura,
                    ["destructiveHint"] = distruttivo,
                    ["idempotentHint"] = idempotente,
                    // Il mondo esterno qui e' SolidWorks: un programma che non
                    // controlliamo, con documenti che cambiano sotto i piedi.
                    ["openWorldHint"] = true,
                },
            };
        }

        private static string TitoloUmano(string nome)
        {
            switch (nome)
            {
                case "guarda_il_pezzo": return "Guarda il pezzo aperto";
                case "elenco_operazioni": return "Elenco delle operazioni";
                case "descrivi_operazione": return "Descrivi una operazione";
                case "disegna": return "Disegna nel pezzo";
                default: return nome;
            }
        }
    }
}
