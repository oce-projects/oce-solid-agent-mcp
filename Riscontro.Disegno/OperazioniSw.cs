using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// Le sei operazioni che l'agente sa costruire davvero.
    ///
    /// LA REGOLA IN TESTA A TUTTO
    /// Questo file scrive dentro il documento di una persona. Ogni metodo qui
    /// dentro fa una cosa sola, la fa con parametri gia' validati dal server, e
    /// se qualcosa non torna NON prova a rimediare: fallisce e lo dice. Un
    /// tentativo di rimedio automatico dentro il modello di qualcun altro e' il
    /// modo migliore per produrre un pezzo che sembra giusto.
    ///
    /// PARAMETRI NOMINATI, SEMPRE
    /// FeatureExtrusion3 ha 23 parametri, FeatureCut4 ne ha 27. Scritti
    /// posizionalmente, uno scambiato non da' errore di compilazione ne'
    /// eccezione: da' una feature diversa. I nomi qui sotto sono quelli veri,
    /// letti per riflessione dall'interop 29.5.1.1 (SOLIDWORKS 2021), non
    /// ricordati.
    ///
    /// UNITA'
    /// Tutti i valori in ingresso sono in MILLIMETRI e GRADI, come sul
    /// protocollo. La conversione la fa Unita, e solo Unita.
    ///
    /// IL LIMITE DICHIARATO DI QUESTA VERSIONE
    /// Le operazioni su faccia (fori e tasche) lavorano solo su facce PIANE e
    /// perpendicolari a uno degli assi principali. Su una faccia inclinata il
    /// sistema di coordinate dello schizzo non coincide con quello del modello,
    /// e le coordinate x/y che arrivano dal piano finirebbero altrove — senza
    /// nessun errore. Meglio rifiutare: chi legge un rifiuto capisce, chi
    /// guarda un foro spostato di 30 mm spesso non se ne accorge.
    /// </summary>
    public partial class OperazioniSw
    {
        private readonly ISldWorks _sw;
        private readonly Action<string> _log;

        /// <summary>Cosa hanno lasciato i passi precedenti di questo piano.</summary>
        private IReadOnlyDictionary<int, Riferimenti.Passo> _passiFatti =
            new Dictionary<int, Riferimenti.Passo>();

        /// <summary>
        /// Quante feature aveva l'albero quando questo passo e' cominciato.
        ///
        /// SERVE A UNA COSA SOLA, E SENZA DI ESSA LA CANCELLAZIONE E'
        /// PERICOLOSA: permette a <see cref="SenzaLoSchizzo"/> di distinguere
        /// «lo schizzo in fondo all'albero l'ho disegnato io adesso» da «lo
        /// schizzo in fondo all'albero c'era gia'». Senza questo numero, un
        /// passo che non riesce a creare nemmeno lo schizzo cancellerebbe
        /// l'orfano lasciato dal passo PRECEDENTE — o uno schizzo di chi sta
        /// lavorando al pezzo, che non ha chiesto niente a nessuno.
        ///
        /// Il valore di partenza e' int.MaxValue e NON zero: se nessuno l'ha
        /// scritto, l'albero non puo' risultare «cresciuto» e la cancellazione
        /// non parte. Un difetto nostro deve lasciare uno schizzo di troppo,
        /// non cancellare la feature di qualcun altro.
        /// </summary>
        private int _featureAllInizioDelPasso = int.MaxValue;

        public OperazioniSw(ISldWorks sw, Action<string> log)
        {
            _sw = sw;
            _log = log ?? (m => { });
        }

        /// <summary>
        /// QUANDO UN CONTROLLO BOCCIA UNA FORMA, LA FORMA E' GIA' NEL PEZZO.
        ///
        /// Le operazioni che si controllano da sole — l'asola conta il
        /// materiale tolto, il guscio il volume — decidono DOPO aver chiamato
        /// la feature, perche' prima non c'e' niente da misurare. Se la misura
        /// non torna, il taglio resta: disfare una feature che ha TOLTO
        /// materiale non e' la stessa cosa che togliere uno schizzo vuoto
        /// (vedi <see cref="SenzaLoSchizzo"/>), e non e' stato provato.
        ///
        /// Allora lo si DICE, sempre e con le stesse parole. Un rifiuto che
        /// tace lascia credere che il pezzo sia intatto.
        /// </summary>
        private const string RestaNelPezzo =
            " Il taglio pero' e' gia' stato fatto: ritiralo — " + Frasi.CosiSiRitira + ".";

        /// <summary>
        /// Coda dei messaggi di errore che lasciano uno schizzo nell'albero.
        ///
        /// NON E' PIU' LA STRADA NORMALE: dalla 0.43.0 lo schizzo lo togliamo
        /// noi, e questa frase resta per il caso in cui NON ci riusciamo. Il
        /// motivo del cambio e' misurato: l'11 settembre 2026, otto richieste
        /// oltre i limiti hanno prodotto sette rifiuti giusti e sette schizzi
        /// orfani. Il messaggio era onesto, ma «puoi toglierlo con Ctrl+Z»
        /// sette volte non e' una riparazione: e' un compito dato a chi legge.
        ///
        /// Chi la riceve adesso sa due cose vere insieme: che l'operazione non
        /// e' riuscita, e che nel pezzo e' rimasto qualcosa di nostro.
        /// </summary>
        private const string LasciatoSchizzo =
            " Nel pezzo e' rimasto lo schizzo che avevo disegnato: puoi toglierlo — " +
            Frasi.CosiSiRitira + ".";

        /// <summary>
        /// Il terzo esito di <see cref="SenzaLoSchizzo"/>, e il piu' importante
        /// dei tre: lo schizzo e' stato tolto, ma togliendolo il pezzo e'
        /// cambiato.
        ///
        /// Non dovrebbe succedere — si cancella solo una feature che e' uno
        /// schizzo, che sta in fondo all'albero e da cui non dipende niente —
        /// e proprio per questo va detto se succede. Rispondere
        /// <see cref="LasciatoSchizzo"/> qui sarebbe una bugia («lo schizzo e'
        /// rimasto» quando non e' rimasto) che coprirebbe un danno vero.
        ///
        /// NON SI E' MAI VISTA ACCADERE, e il 14/9/2026 si e' provato a farla
        /// accadere. Voce D7 del TODO.
        ///
        /// Perche' e' difficile da raggiungere, ragionando sulle condizioni che
        /// <see cref="SenzaLoSchizzo"/> impone prima di arrivare qui: lo schizzo
        /// e' l'ultima feature, e' nato in questo passo, e **non ha figli**. Uno
        /// schizzo senza figli non porta materiale, e `swDelete_Absorbed` toglie
        /// lui e le sue quote e basta. Quindi volume e conteggio corpi non
        /// possono cambiare, e il conteggio feature cala di esattamente uno.
        ///
        /// Provato anche il caso piu' cattivo che si e' saputo costruire: lo
        /// stesso giro su un pezzo **gia' instabile** — con una lavorazione in
        /// errore nell'albero — perche' la cancellazione fa ricostruire, e una
        /// ricostruzione su un pezzo rotto puo' dare un volume diverso. Non e'
        /// successo: lo schizzo si e' tolto pulito e il messaggio non e' uscito.
        ///
        /// COSA LA RENDEREBBE RAGGIUNGIBILE. Uno di questi:
        ///
        ///   * `IGetChildCount()` che risponde 0 per uno schizzo che invece un
        ///     figlio ce l'ha (un riferimento esterno non contato come figlio);
        ///   * un'operazione che lascia dietro di se' un CORPO oltre allo
        ///     schizzo, e poi fallisce: li' il conteggio corpi cambierebbe;
        ///   * `swDelete_Absorbed` che in una versione futura cancelli piu' di
        ///     quello che cancella oggi.
        ///
        /// **Il messaggio resta**, e non e' codice morto: e' la guardia che
        /// distingue «ho rimesso il pezzo com'era» da «credo di averlo rimesso
        /// com'era». Il giorno in cui una delle tre cose sopra diventa vera,
        /// questa riga e' la differenza fra accorgersene e non accorgersene.
        /// Ed e' scritta apposta come «controllalo» e non come «ho rotto il
        /// pezzo»: quello che si sa e' che i numeri non tornano, non chi sia
        /// stato.
        /// </summary>
        private const string SchizzoToltoMaNonSolo =
            " Ho tolto lo schizzo che avevo disegnato, ma togliendolo e' cambiato" +
            " anche il pezzo: controllalo, e se non e' come lo vuoi ritiralo — " +
            Frasi.CosiSiRitira + ".";

        /// <summary>
        /// Il tipo dell'origine del pezzo, l'unica cosa che <see cref="SenzaLoSchizzo"/>
        /// rifiuta per NOME di tipo.
        ///
        /// Misurato il 14 settembre 2026: l'origine risponde ISketch a
        /// `GetSpecificFeature2` esattamente come uno schizzo vero, e su un
        /// documento vuoto e' l'ultima feature dell'albero. Non e' un nome
        /// tradotto — quello e' `Name`, che in italiano dice «Origine» — e'
        /// il nome di TIPO dell'API, che non cambia con la lingua.
        /// </summary>
        private const string OrigineDelPezzo = "OriginProfileFeature";

        public class Esito
        {
            public bool Riuscito;
            public string Errore;
            /// <summary>Nome della feature creata: serve per tornare indietro.</summary>
            public string FeatureCreata;
            /// <summary>
            /// La feature vera, non il suo nome. La tiene l'esecutore del piano
            /// per poter risolvere i riferimenti dei passi successivi: il nome
            /// e' localizzato e si puo' rinominare, l'oggetto no.
            /// </summary>
            public IFeature Feature;
            /// <summary>
            /// La direzione lungo cui questo passo ha lavorato. Serve a dire
            /// quale faccia e' «in cima»; null per le operazioni che una
            /// direzione non ce l'hanno.
            /// </summary>
            public double[] Direzione;
            /// <summary>
            /// IL DOCUMENTO SU CUI SI LAVORA DA ADESSO IN POI, quando questa
            /// operazione lo ha cambiato APPOSTA.
            ///
            /// Ce n'e' una sola che lo fa — `crea_disegno` — e non e' un
            /// effetto collaterale: una tavola E' un documento nuovo, e i passi
            /// dopo (le viste, le quote) vanno fatti li'.
            ///
            /// Serve perche' chi esegue il piano sorveglia che il documento non
            /// cambi sotto i piedi: e' la difesa che impedisce di estrudere nel
            /// pezzo di qualcun altro. Senza un modo per DICHIARARE il cambio,
            /// quella difesa fermerebbe un piano corretto («fammi la tavola e
            /// mettici le viste») — e un rifiuto di cio' che e' giusto e' il
            /// peggiore dei due modi di sbagliare.
            ///
            /// Si segue solo cio' che e' dichiarato. Un documento che cambia
            /// senza che nessuno lo dica resta un motivo per fermarsi.
            /// </summary>
            public IModelDoc2 DocumentoNuovo;
            public Dictionary<string, object> Misure = new Dictionary<string, object>();
        }

        /// <summary>
        /// Esegue una operazione del piano. Ritorna sempre un esito, non solleva.
        /// </summary>
        /// <param name="passiFatti">
        /// Cosa hanno lasciato i passi gia' eseguiti di questo stesso piano,
        /// per numero d'ordine. E' cio' che permette al passo 2 di dire «la
        /// faccia in cima a quello che hai fatto al passo 1» invece di citare
        /// un identificativo che, quando il piano e' stato scritto, non
        /// esisteva ancora.
        /// </param>
        /// <summary>
        /// I nomi che lo switch qui sotto sa eseguire.
        ///
        /// SERVE A CONFRONTARE IL CATALOGO CON CIO' CHE ESISTE DAVVERO. Il
        /// catalogo dichiara 38 operazioni; qui ce ne sono 36, e le due che
        /// mancano — `estrudi_sezione` e `ruota_dentata` — esistono solo perche'
        /// il server le espande in `contorni` prima che arrivino qui, e il ramo
        /// `default` ricade sull'estrusione di profilo quando trova dei
        /// contorni. Chiamate con i loro argomenti veri rispondono «operazione
        /// sconosciuta». Il controllo che confronta i due insiemi lo dice; senza
        /// elenco, sarebbe una regex su 4.700 righe che si rompe alla prima
        /// riformattazione.
        ///
        /// **QUESTO ELENCO PUO' DERIVARE DALLO SWITCH, ed e' il suo difetto.**
        /// Ci si difende in due modi, tutti e due nel controllo e nessuno nella
        /// buona volonta': si verifica che ogni nome qui dentro sia davvero
        /// dispatchabile (chiamando `Esegui` e guardando che NON risponda
        /// «operazione sconosciuta»), e si confronta con i `case` letti dal
        /// sorgente — dove leggerne zero e' un fallimento, non un successo.
        /// </summary>
        public static readonly string[] NomiEseguibili =
        {
            "applica_materiale", "asola", "combina_corpi",
            "cordone_saldatura", "crea_asse",
            "crea_disegno", "crea_piano", "distinta_taglio",
            "distanza_minima", "elimina_lavorazione",
            "esporta_iges", "esporta_pdf", "esporta_step", "esporta_stl",
            "estrudi_cerchio", "estrudi_profilo", "estrudi_rettangolo",
            "estrudi_sezione", "ruota_dentata",
            "estrudi_su_faccia", "fazzoletto", "foro_guidato", "guscio",
            "imposta_foglio", "ingombro_corpo", "membro_strutturale",
            "misura_fra_entita", "modifica_quota", "proprieta_di_massa",
            "quote_dal_modello", "raccordo_spigoli", "ripeti_circolare",
            "ripeti_lineare", "rivoluzione", "salva_documento",
            "smusso_spigoli", "sopprimi_lavorazione", "specchia",
            "sposta_corpi", "taglia_membri",
            "taglio_cilindrico", "taglio_rettangolare", "tappo_estremita",
            "telaio_rettangolare", "verifica_geometria_corpo",
            "vista_modello", "viste_standard",
        };

        /// <summary>
        /// La frase esatta con cui lo switch rifiuta un nome che non conosce.
        /// Sta qui e non scritta due volte perche' il controllo la confronta:
        /// se cambia, il controllo deve accorgersene invece di credere che
        /// tutto vada bene.
        /// </summary>
        public const string OperazioneSconosciuta = "Operazione sconosciuta: ";

        public Esito Esegui(
            IModelDoc2 modello,
            string operazione,
            Dictionary<string, object> argomenti,
            StatoDocumento stato,
            IReadOnlyDictionary<int, Riferimenti.Passo> passiFatti = null)
        {
            _passiFatti = passiFatti ?? new Dictionary<int, Riferimenti.Passo>();
            // Si svuota all'INIZIO: svuotarlo alla fine lo lascerebbe pieno
            // quando l'operazione fallisce, e la successiva erediterebbe gli
            // spostamenti di quella prima.
            _riancoraggi.Clear();
            // La fotografia dell'albero PRIMA di toccare qualsiasi cosa. La
            // legge solo SenzaLoSchizzo, e solo per non cancellare roba che
            // c'era gia'. Sta qui e non dentro le singole operazioni perche'
            // questo e' l'unico punto da cui passano tutte e trentasei.
            _featureAllInizioDelPasso = ContaFeature(modello);
            try
            {
                switch (operazione)
                {
                    case "estrudi_rettangolo": return EstrudiRettangolo(modello, argomenti);
                    case "estrudi_cerchio": return EstrudiCerchio(modello, argomenti);
                    case "estrudi_sezione": return EstrudiSezione(modello, argomenti);
                    case "ruota_dentata": return RuotaDentataOp(modello, argomenti);
                    // La sagoma libera e le RICETTE finiscono nello stesso
                    // esecutore: una ruota dentata non e' un'altra operazione,
                    // e' un modo di scrivere dei contorni. Il server la espande
                    // e manda gli stessi `contorni`. Due esecutori per la stessa
                    // cosa vorrebbero dire due comportamenti che divergono alla
                    // prima modifica.
                    case "estrudi_profilo":
                        return EstrudiProfilo(modello, argomenti);
                    case "taglio_cilindrico": return TaglioCilindrico(modello, argomenti, stato);
                    // Il foro fatto come si fa un foro: creazione guidata,
                    // norma ISO, filetto, lamatura, svasatura. Sta in
                    // Agente/OperazioniFori.cs perche' la ricetta e' lunga e
                    // ogni riga di quella ricetta e' stata misurata.
                    case "foro_guidato": return ForoGuidato(modello, argomenti, stato);
                    // Il materiale: Agente/OperazioniMateriale.cs.
                    case "applica_materiale": return ApplicaMateriale(modello, argomenti);
                    case "taglio_rettangolare": return TaglioRettangolare(modello, argomenti, stato);
                    case "asola": return Asola(modello, argomenti, stato);
                    case "estrudi_su_faccia": return EstrudiSuFaccia(modello, argomenti, stato);
                    case "modifica_quota": return ModificaQuota(modello, argomenti, stato);
                    case "sopprimi_lavorazione": return SopprimiLavorazione(modello, argomenti, stato);
                    case "elimina_lavorazione": return EliminaLavorazione(modello, argomenti, stato);
                    case "sposta_corpi": return SpostaCorpi(modello, argomenti, stato);
                    // Le booleane fra corpi stanno in OperazioniCorpi.cs.
                    case "combina_corpi": return CombinaCorpi(modello, argomenti, stato);
                    case "raccordo_spigoli": return RaccordoSpigoli(modello, argomenti, stato);
                    case "smusso_spigoli": return SmussoSpigoli(modello, argomenti, stato);
                    case "guscio": return Guscio(modello, argomenti, stato);
                    case "crea_piano": return CreaPiano(modello, argomenti);
                    case "crea_asse": return CreaAsse(modello, argomenti, stato);
                    case "ripeti_lineare": return RipetiLineare(modello, argomenti, stato);
                    case "specchia": return Specchia(modello, argomenti);
                    case "rivoluzione": return Rivoluzione(modello, argomenti);
                    case "ripeti_circolare": return RipetiCircolare(modello, argomenti, stato);

                    // ── tavola ──────────────────────────
                    // Stanno in Agente/OperazioniTavola.cs. Sono le prime
                    // operazioni del catalogo che NON lavorano su un pezzo:
                    // creano un secondo documento e lo tengono aperto
                    // insieme al modello. Le tre trappole della famiglia
                    // (il diedro, la quota che mente, il PDF di un foglio
                    // solo) sono spiegate in testa a quel file.
                    case "crea_disegno": return CreaDisegno(modello, argomenti);
                    case "imposta_foglio": return ImpostaFoglio(modello, argomenti);
                    case "viste_standard": return VisteStandard(modello, argomenti);
                    case "vista_modello": return VistaModello(modello, argomenti);
                    case "quote_dal_modello": return QuoteDalModello(modello, argomenti);
                    case "esporta_pdf": return EsportaPdf(modello, argomenti);
                    case "salva_documento": return SalvaDocumento(modello, argomenti);

                    // ── saldature ───────────────────────
                    // Stanno in Agente/OperazioniSaldatura.cs: sono un
                    // sottosistema con le sue API e le sue trappole, e
                    // tenerle qui avrebbe fatto un file da duemila righe.
                    //
                    // «telaio_rettangolare» finisce nello stesso esecutore:
                    // il server lo espande in segmenti e manda gli stessi
                    // parametri di «membro_strutturale», come gia' succede
                    // per le sezioni normalizzate. Due esecutori per la
                    // stessa cosa vorrebbero dire due comportamenti che
                    // divergono alla prima modifica.
                    case "membro_strutturale":
                    case "telaio_rettangolare": return MembroStrutturale(modello, argomenti);
                    case "taglia_membri": return TagliaMembri(modello, argomenti, stato);
                    case "distinta_taglio": return DistintaTaglio(modello, argomenti);
                    case "tappo_estremita": return TappoEstremita(modello, argomenti, stato);
                    case "fazzoletto": return Fazzoletto(modello, argomenti, stato);
                    case "cordone_saldatura": return CordoneSaldatura(modello, argomenti, stato);

                    // ── verifiche ───────────────────────
                    // Stanno in OperazioniVerifica.cs. NON DISEGNANO: dicono
                    // com'e' venuto. `distinta_taglio` e' il precedente —
                    // un'operazione di catalogo che legge invece di costruire.
                    case "verifica_geometria_corpo":
                        return VerificaGeometriaCorpo(modello, argomenti, stato);
                    // Le due domande che si fanno prima di ordinare: quanto
                    // pesa, e in che scatola ci sta. Stanno in
                    // OperazioniMisura.cs.
                    case "proprieta_di_massa":
                        return ProprietaDiMassa(modello, argomenti, stato);
                    case "ingombro_corpo":
                        return IngombroCorpo(modello, argomenti, stato);
                    case "distanza_minima":
                        return DistanzaMinima(modello, argomenti, stato);
                    case "misura_fra_entita":
                        return MisuraFraEntita(modello, argomenti, stato);

                    // ── esportazioni ────────────────────
                    // Stanno in OperazioniEsporta.cs. `esporta_stl` chiama
                    // `verifica_geometria_corpo` PRIMA di scrivere, e rifiuta
                    // se il pezzo non e' un solido chiuso solo: un STL non
                    // porta con se' niente che dica che il pezzo era a posto.
                    case "esporta_stl":
                        return EsportaStl(modello, argomenti, stato);
                    // STEP e IGES finiscono nello stesso esecutore: cambiano
                    // le preferenze e il modo di rileggere il file, non la
                    // strada. Due esecutori per la stessa cosa vorrebbero dire
                    // due comportamenti che divergono alla prima modifica.
                    case "esporta_step":
                    case "esporta_iges":
                        return EsportaScambio(modello, argomenti, operazione, stato);
                    default:
                        // QUALUNQUE operazione che porta dei contorni si disegna
                        // con lo stesso esecutore. Cosi' una RICETTA nuova
                        // (una sezione normalizzata, una ruota, un profilo di
                        // domani) si aggiunge al server senza toccare l'add-in
                        // e senza che nessuno debba reinstallare niente.
                        if (argomenti != null && argomenti.ContainsKey("contorni"))
                            return EstrudiProfilo(modello, argomenti);
                        return Fallito(OperazioneSconosciuta + operazione);
                }
            }
            catch (Exception ex)
            {
                return Fallito($"{operazione}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Le sei operazioni
        // ─────────────────────────────────────────────────────────────────

        private Esito EstrudiRettangolo(IModelDoc2 modello, Dictionary<string, object> a)
        {
            double lunghezza = Num(a, "lunghezza_mm");
            double larghezza = Num(a, "larghezza_mm");
            double spessore = Num(a, "spessore_mm");
            bool centrato = Bool(a, "centrato", true);
            string piano = Str(a, "piano", "frontale");

            if (!SelezionaPiano(modello, piano))
                return Fallito($"Non ho trovato il piano «{piano}» nell'albero.");

            var schizzi = modello.SketchManager;
            schizzi.InsertSketch(true);

            double x1 = centrato ? Unita.M(-lunghezza / 2) : 0;
            double y1 = centrato ? Unita.M(-larghezza / 2) : 0;
            double x2 = centrato ? Unita.M(lunghezza / 2) : Unita.M(lunghezza);
            double y2 = centrato ? Unita.M(larghezza / 2) : Unita.M(larghezza);
            var lati = schizzi.CreateCornerRectangle(x1, y1, 0, x2, y2, 0) as object[];

            // LE DUE QUOTE CHE RENDONO IL PEZZO MODIFICABILE.
            //
            // Si mettono qui, dentro lo schizzo, sul lato orizzontale e su
            // quello verticale. Da questo momento «portami la vasca a 300»
            // e' una modifica di quota invece che un pezzo da rifare, e le
            // quote esistono anche per la tavola.
            int quoteMesse = QuotaIlRettangolo(modello, lati, x1, y1, x2, y2);

            schizzi.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Estrudi(modello, spessore, unisci: true);
            if (feature == null) return Fallito("L'estrusione non e' stata creata." + SenzaLoSchizzo(modello));
            return Riuscito(feature, new Dictionary<string, object>
            {
                ["lunghezza_mm"] = lunghezza,
                ["larghezza_mm"] = larghezza,
                ["spessore_mm"] = spessore,
                ["quote_sullo_schizzo"] = quoteMesse,
            }, Riferimenti.NormaleDelPiano(piano));
        }

        /// <summary>
        /// Mette una quota sul lato orizzontale e una sul lato verticale di
        /// un rettangolo appena disegnato. Ritorna quante ne ha messe.
        ///
        /// COME SI QUOTA VIA API. `IModelDocExtension.AddDimension(X, Y, Z,
        /// Direction)` non prende nessuna entita': lavora su cio' che e'
        /// SELEZIONATO, ed e' l'ennesima chiamata di questo interop fatta
        /// cosi'. Il punto passato non e' l'entita' da quotare — e' DOVE va
        /// scritto il numero. Quindi: si seleziona il lato, si dice dove
        /// mettere il testo, e la quota nasce.
        ///
        /// NON SI SOLLEVA MAI. Un pezzo senza quote e' peggiore di uno
        /// quotato, ma e' comunque un pezzo giusto: se la quotatura non
        /// riesce si va avanti e si scrive quante ne sono state messe, che
        /// e' un numero che chi legge puo' controllare.
        /// </summary>
        private int QuotaIlRettangolo(
            IModelDoc2 modello, object[] lati,
            double x1, double y1, double x2, double y2)
        {
            if (lati == null || lati.Length == 0) return 0;

            // Il testo va messo FUORI dal rettangolo, se no finisce sopra il
            // disegno. Un sesto del lato e' abbastanza su un pezzo da 10 mm
            // come su uno da 3 metri.
            double fuoriX = Math.Max(Math.Abs(x2 - x1) * 0.15, 0.005);
            double fuoriY = Math.Max(Math.Abs(y2 - y1) * 0.15, 0.005);

            var daQuotare = new List<Quota>();
            bool orizzontaleFatta = false, verticaleFatta = false;

            foreach (var o in lati)
            {
                var linea = o as ISketchLine;
                if (linea == null) continue;

                double[] p1, p2;
                if (!EstremiDi(linea, out p1, out p2)) continue;

                bool orizzontale = Math.Abs(p1[1] - p2[1]) < 1e-9;
                bool verticale = Math.Abs(p1[0] - p2[0]) < 1e-9;
                if (orizzontale && orizzontaleFatta) continue;
                if (verticale && verticaleFatta) continue;
                if (!orizzontale && !verticale) continue;

                double tx = (p1[0] + p2[0]) / 2;
                double ty = (p1[1] + p2[1]) / 2;
                if (orizzontale) ty = Math.Min(y1, y2) - fuoriY;
                else tx = Math.Min(x1, x2) - fuoriX;

                daQuotare.Add(Quota.Su(o, new[] { tx, ty, 0.0 }));
                if (orizzontale) orizzontaleFatta = true; else verticaleFatta = true;
                if (orizzontaleFatta && verticaleFatta) break;
            }

            return QuotaSegmenti(modello, daQuotare);
        }

        /// <summary>
        /// Mette una quota su ognuno dei segmenti dati, con il testo nel punto
        /// indicato (metri, coordinate di schizzo). Ritorna quante ne ha messe.
        ///
        /// COME SI QUOTA VIA API. `IModelDocExtension.AddDimension(X, Y, Z,
        /// Direction)` non prende nessuna entita': lavora su cio' che e'
        /// SELEZIONATO, come meta' di questo interop. Il punto passato non e'
        /// l'entita' da quotare — e' DOVE va scritto il numero.
        ///
        /// SI SPEGNE LA FINESTRELLA DEL VALORE, e questa e' la riga che conta.
        /// `AddDimension` mette la quota e poi apre «Modifica» per chiedere
        /// quanto vale, e resta li' ad aspettare una risposta che non
        /// arrivera' mai. Non fallisce, non solleva, non scade: si pianta. In
        /// produzione questo codice gira DENTRO SolidWorks — vorrebbe dire
        /// bloccare il programma di chi ci sta lavorando. Misurato l'11
        /// settembre 2026, dieci minuti di SolidWorks fermo.
        ///
        /// La preferenza si rimette com'era in OGNI caso: e' un'impostazione
        /// di chi usa il programma, non nostra. Stessa regola di `AddToDB`.
        ///
        /// NON SOLLEVA MAI. Un pezzo senza quote e' peggiore di uno quotato, ma
        /// e' comunque un pezzo giusto: se la quotatura non riesce si va avanti
        /// e si scrive quante ne sono state messe, che e' un numero che chi
        /// legge puo' controllare.
        /// </summary>
        /// <summary>
        /// Un segmento da quotare, e DOVE va scritto il numero (metri,
        /// coordinate del modello).
        ///
        /// La Z c'e' e non e' un di piu'. Prima il punto del testo era
        /// `(x, y, 0)`: va bene per uno schizzo su un piano di riferimento,
        /// dove Z e' zero per costruzione, e **non** per uno schizzo su una
        /// faccia qualunque — che e' dove stanno tutti i tagli. Con la Z
        /// inchiodata a zero la quota di un foro su un fianco nasce fuori dal
        /// piano dello schizzo, e SOLIDWORKS la rifiuta senza spiegare.
        /// </summary>
        private class Quota
        {
            public object Segmento;
            public double X, Y, Z;

            public static Quota Su(object segmento, double[] punto)
            {
                return new Quota
                {
                    Segmento = segmento,
                    X = punto[0],
                    Y = punto[1],
                    Z = punto.Length > 2 ? punto[2] : 0,
                };
            }
        }

        private int QuotaSegmenti(IModelDoc2 modello, List<Quota> daQuotare)
        {
            if (modello == null || daQuotare == null || daQuotare.Count == 0) return 0;

            bool chiedevaIlValore = true;
            try
            {
                chiedevaIlValore = _sw.GetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }

            int messe = 0;
            foreach (var voce in daQuotare)
            {
                try
                {
                    modello.ClearSelection2(true);
                    var segmento = voce.Segmento as ISketchSegment;
                    if (segmento == null || !segmento.Select4(false, null)) continue;
                    var nata = modello.Extension.AddDimension(voce.X, voce.Y, voce.Z, 0)
                        as IDisplayDimension;
                    if (nata == null) continue;

                    // MARCATA PER IL DISEGNO, ed e' la riga che fa la
                    // differenza fra una tavola quotata e una muta.
                    //
                    // `quote_dal_modello` importa di serie solo le quote
                    // MARCATE: sono quelle che un progettista ha scelto di
                    // mostrare, e una tavola con tutte le quote del modello e'
                    // illeggibile. Una quota nata da `AddDimension` non e'
                    // marcata, quindi le nostre non arrivavano mai — misurato
                    // il 14/9/2026: pezzo con 11 quote, tavola con 0, e
                    // nemmeno chiedendo «tutte_le_quote».
                    //
                    // Marcandole qui, la tavola prende ESATTAMENTE le quote che
                    // le operazioni hanno messo apposta, e non quelle di
                    // servizio.
                    try { nata.MarkedForDrawing = true; } catch { }
                }
                catch { continue; }
                messe++;
            }

            try { modello.ClearSelection2(true); } catch { }
            try
            {
                _sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedevaIlValore);
            }
            catch { }
            return messe;
        }

        /// <summary>Gli estremi di una retta di schizzo, in metri.</summary>
        private static bool EstremiDi(ISketchLine linea, out double[] p1, out double[] p2)
        {
            p1 = null; p2 = null;
            try
            {
                var a = linea.GetStartPoint2() as ISketchPoint;
                var b = linea.GetEndPoint2() as ISketchPoint;
                if (a == null || b == null) return false;
                p1 = new[] { a.X, a.Y, a.Z };
                p2 = new[] { b.X, b.Y, b.Z };
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// <summary>
        /// Una RUOTA DENTATA cilindrica a denti dritti.
        ///
        /// Come la sezione, non e' un'altra estrusione: e' un modo di
        /// scrivere dei contorni, e finisce nello stesso esecutore.
        ///
        /// La geometria sta in <see cref="RuotaDentata"/>, che e' il file
        /// piu' delicato della libreria: una traslitterazione sbagliata
        /// dell'evolvente non da' nessun errore, da' una ruota che ingrana
        /// male.
        /// </summary>
        private Esito RuotaDentataOp(IModelDoc2 modello, Dictionary<string, object> a)
        {
            double dentiGrezzo, modulo, spessore;
            string perche;
            if (!NumObbligatorio(a, "denti", out dentiGrezzo, out perche))
                return Fallito("Per una ruota dentata serve «denti»: " + perche);
            if (!NumObbligatorio(a, "modulo_mm", out modulo, out perche))
                return Fallito("Per una ruota dentata serve «modulo_mm»: " + perche);
            if (!NumObbligatorio(a, "spessore_mm", out spessore, out perche))
                return Fallito("Per una ruota dentata serve «spessore_mm»: " + perche);

            int denti = (int)Math.Round(dentiGrezzo);
            if (Math.Abs(dentiGrezzo - denti) > 1e-9)
                return Fallito(
                    "I denti sono " + dentiGrezzo + ": un numero di denti e' " +
                    "intero, e mezzo dente non si taglia.");
            if (denti < 3)
                return Fallito("Una ruota da " + denti + " denti non e' una ruota.");
            if (modulo <= 0 || spessore <= 0)
                return Fallito("Modulo e spessore devono essere maggiori di zero.");

            string motivo;
            var contorni = RuotaDentata.Contorni(
                denti, modulo,
                Num(a, "angolo_pressione_gradi", 20.0),
                Num(a, "foro_mm", 0),
                Bool(a, "cava_linguetta", false),
                8,
                out motivo);
            if (contorni == null) return Fallito(motivo);

            var suoi = new Dictionary<string, object>
            {
                ["contorni"] = contorni.ToArray(),
                ["spessore_mm"] = spessore,
            };
            if (a.ContainsKey("piano")) suoi["piano"] = a["piano"];

            // Il tetto largo: questi punti li ha calcolati una formula, non
            // scritti un modello. Una ruota da 127 denti sono milleventi
            // segmenti, e il tetto dei duecento la rifiuterebbe.
            var esito = EstrudiProfilo(modello, suoi, MaxVerticiGenerati);

            // I NUMERI DELLA RUOTA, ACCANTO A QUELLI DELL'ESTRUSIONE.
            // Chi ha chiesto z=20 m=2 vuole poter controllare che il
            // diametro esterno sia 44: e' il modo piu' rapido di accorgersi
            // che qualcosa nella matematica non torna, e non si legge dal
            // volume.
            if (esito != null && esito.Riuscito && esito.Misure != null)
            {
                esito.Misure["denti"] = denti;
                esito.Misure["modulo_mm"] = modulo;
                esito.Misure["diametro_primitivo_mm"] = Math.Round(modulo * denti, 4);
                esito.Misure["diametro_esterno_mm"] = Math.Round(modulo * (denti + 2), 4);
                esito.Misure["diametro_fondo_mm"] =
                    Math.Round(modulo * denti - 2.5 * modulo, 4);
                esito.Misure["passo_mm"] = Math.Round(Math.PI * modulo, 4);
            }
            return esito;
        }

        /// <summary>
        /// Estrude una SEZIONE NORMALIZZATA: due o tre quote invece di otto
        /// coordinate.
        ///
        /// Non e' un'altra estrusione: e' un modo di scrivere dei contorni.
        /// La ricetta costruisce i contorni e li passa allo stesso esecutore
        /// di `estrudi_profilo` — due esecutori per la stessa cosa
        /// divergerebbero alla prima modifica.
        ///
        /// Prima questa ricetta viveva solo nel server Python, quindi
        /// l'operazione era **dichiarata nel catalogo e non eseguibile**: dal
        /// ponte rispondeva «operazione sconosciuta» a chi la chiedeva con i
        /// suoi argomenti veri.
        /// </summary>
        private Esito EstrudiSezione(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string tipo = Str(a, "tipo", "");

            string[] volute;
            if (string.IsNullOrWhiteSpace(tipo) ||
                !Sezioni.Volute.TryGetValue(tipo.Trim(), out volute))
                return Fallito(
                    "Sezione «" + tipo + "» sconosciuta. Ci sono: " +
                    string.Join(", ", Sezioni.Volute.Keys) + ".");

            // LE QUOTE CHE SERVONO A QUESTA SEZIONE, e solo quelle.
            // Lo schema del catalogo le dichiara tutte insieme perche' e'
            // una sola operazione, ma un tubolare non ha un diametro e un
            // tondo non ha una parete: se una manca va detto QUALE, non
            // «parametri non validi».
            var m = new Dictionary<string, double>();
            var mancanti = new List<string>();
            foreach (var q in volute)
            {
                double v;
                string perche;
                if (!NumObbligatorio(a, q, out v, out perche)) { mancanti.Add(q); continue; }
                if (v <= 0) return Fallito(
                    "«" + q + "» e' " + v + ": una quota di una sezione non puo' " +
                    "essere zero o negativa.");
                m[q] = v;
            }
            if (mancanti.Count > 0)
                return Fallito(
                    "Per una sezione «" + tipo + "» " +
                    (mancanti.Count == 1 ? "serve " : "servono ") +
                    string.Join(", ", mancanti.ConvertAll(x => "«" + x + "»")) + ".");

            string motivo;
            var contorni = Sezioni.Contorni(tipo, m, out motivo);
            if (contorni == null) return Fallito(motivo);

            // Si riscrivono gli argomenti per l'esecutore dei profili,
            // lasciando fuori le quote della sezione: da qui in poi la
            // sagoma e' fatta, e le quote non servono piu' a nessuno.
            var suoi = new Dictionary<string, object>
            {
                ["contorni"] = contorni.ToArray(),
                ["spessore_mm"] = Num(a, "lunghezza_mm", 0),
            };
            if (a.ContainsKey("piano")) suoi["piano"] = a["piano"];
            if (a.ContainsKey("simmetrico")) suoi["simmetrico"] = a["simmetrico"];

            return EstrudiProfilo(modello, suoi, MaxVerticiGenerati);
        }

        /// <summary>
        /// Estrude una sagoma qualsiasi, data come contorni di vertici.
        ///
        /// E' l'operazione che toglie il tetto a tutto il resto: con una
        /// poligonale chiusa si fanno staffe sagomate, angolari, fazzoletti, e
        /// — con DUE contorni, uno esterno e uno interno — i profili cavi.
        /// Le ricette (la ruota dentata, domani il tubolare) non sono altre
        /// operazioni: il server le espande in questi stessi contorni.
        ///
        /// IL PRIMO CONTORNO E' IL BORDO, GLI ALTRI SONO I VUOTI.
        /// SolidWorks estrude la regione compresa: contorni annidati danno un
        /// pezzo forato senza bisogno di un secondo taglio. E' anche il motivo
        /// per cui un tubolare si fa cosi' invece che con l'estrusione sottile:
        /// l'ingombro esterno sta scritto nelle coordinate, non dipende da un
        /// verso di parete che si puo' sbagliare in silenzio.
        ///
        /// AddToDB durante il disegno: senza, SolidWorks prova a inferire una
        /// relazione a ogni segmento, e su cinquecento punti di una ruota
        /// dentata sono minuti invece di secondi. Si rimette com'era in ogni
        /// caso, anche se qualcosa va storto: e' un'impostazione dell'ambiente
        /// di chi lavora, non nostra.
        /// </summary>
        private Esito EstrudiProfilo(
            IModelDoc2 modello, Dictionary<string, object> a,
            int maxVertici = MaxVerticiModello)
        {
            var contorni = Contorni(a, out string motivo, out string notaContorni, maxVertici);
            if (contorni == null) return Fallito(motivo);

            double spessore = Num(a, "spessore_mm");
            bool simmetrico = Bool(a, "simmetrico", false);
            string piano = Str(a, "piano", "frontale");

            if (!SelezionaPiano(modello, piano))
                return Fallito($"Non ho trovato il piano «{piano}» nell'albero.");

            var schizzi = modello.SketchManager;
            bool addToDbPrima = false;
            bool displayPrima = false;
            try { addToDbPrima = schizzi.AddToDB; displayPrima = schizzi.DisplayWhenAdded; } catch { }

            int segmenti = 0;
            try
            {
                schizzi.InsertSketch(true);
                try { schizzi.AddToDB = true; schizzi.DisplayWhenAdded = false; } catch { }

                foreach (var contorno in contorni)
                {
                    segmenti += DisegnaContorno(schizzi, contorno);
                }
            }
            finally
            {
                try { schizzi.AddToDB = addToDbPrima; schizzi.DisplayWhenAdded = displayPrima; } catch { }
            }
            schizzi.InsertSketch(false);

            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Estrudi(modello, spessore, unisci: true, simmetrico: simmetrico);
            if (feature == null) return Fallito("L'estrusione non e' stata creata." + SenzaLoSchizzo(modello));

            // Lo schizzo si blocca DOPO l'estrusione: prima, una relazione
            // fissa su segmenti appena creati puo' interferire con la
            // selezione che serve alla feature.
            string notaBlocco;
            bool bloccato = BloccaSchizzo(modello, out notaBlocco);

            var misure = new Dictionary<string, object>
            {
                ["schizzo_bloccato"] = bloccato,
                ["contorni"] = contorni.Count,
                ["segmenti"] = segmenti,
                ["spessore_mm"] = spessore,
            };
            // I contorni staccati dal primo non si rifiutano, si DICONO: un
            // contorno fuori dal bordo esterno e' una regione a se', ed e'
            // legittimo (quattro alette su una faccia). Ma diventa un corpo in
            // piu', e chi credeva di fare un foro deve poterlo leggere.
            if (notaContorni != null) misure["nota_contorni"] = notaContorni;
            return Riuscito(feature, misure, Riferimenti.NormaleDelPiano(piano));
        }

        /// <summary>
        /// Disegna un contorno, qualunque forma abbia. Ritorna quante entita'
        /// ha creato.
        ///
        /// TRE FORME, E OGNUNA ESISTE PER UNA RAGIONE
        ///  * cerchio   — un cerchio VERO. Approssimarlo a 48 segmenti inscritti
        ///                mette il materiale dentro la circonferenza: su un foro
        ///                da 20 mm invade di 0,021 mm e l'albero non entra.
        ///  * composito — tratti diversi in fila: linee dove e' una retta,
        ///                SPLINE dove e' una curva. Il fianco a evolvente di un
        ///                dente e' una curva, e a segmenti restava sfaccettato
        ///                con 528 entita' nello schizzo.
        ///  * poligonale— la spezzata chiusa, che resta la forma naturale per
        ///                una staffa o un tubolare.
        /// </summary>
        private int DisegnaContorno(ISketchManager schizzi, Contorno contorno)
        {
            if (contorno.Cerchio)
            {
                schizzi.CreateCircleByRadius(
                    Unita.M(contorno.Centro[0]), Unita.M(contorno.Centro[1]), 0,
                    Unita.M(contorno.Diametro / 2.0));
                return 1;
            }

            int fatte = 0;
            foreach (var tratto in contorno.Tratti)
            {
                if (tratto.Spline)
                {
                    // CreateSpline vuole una terna x,y,z per punto, in un unico
                    // vettore piatto. Verificato per riflessione: firma
                    // CreateSpline(Object PointData) -> SketchSegment.
                    var dati = new double[tratto.Punti.Count * 3];
                    for (int i = 0; i < tratto.Punti.Count; i++)
                    {
                        dati[i * 3] = Unita.M(tratto.Punti[i][0]);
                        dati[i * 3 + 1] = Unita.M(tratto.Punti[i][1]);
                        dati[i * 3 + 2] = 0;
                    }
                    schizzi.CreateSpline(dati);
                    fatte++;
                }
                else
                {
                    for (int i = 0; i < tratto.Punti.Count - 1; i++)
                    {
                        var da = tratto.Punti[i];
                        var a2 = tratto.Punti[i + 1];
                        schizzi.CreateLine(
                            Unita.M(da[0]), Unita.M(da[1]), 0,
                            Unita.M(a2[0]), Unita.M(a2[1]), 0);
                        fatte++;
                    }
                }
            }
            return fatte;
        }

        /// <summary>
        /// BLOCCA LO SCHIZZO DOV'E'.
        ///
        /// Uno schizzo generato senza relazioni resta SOTTODEFINITO: un
        /// trascinamento distratto sposta un vertice e il pezzo cambia senza
        /// che nessuno lo sappia. Il committente l'ha notato guardando lo
        /// schizzo della prima ruota.
        ///
        /// Si usa una relazione FISSA (swConstraintType_FIXED = 17), NON
        /// FullyDefineSketch: quella non misura, aggiunge relazioni e SPOSTA la
        /// geometria per soddisfarle — due lati quasi paralleli diventano
        /// paralleli, e lo schizzo risulta verde e diverso di qualche decimo da
        /// quello che e' stato confermato.
        ///
        /// Se non riesce non si fallisce: il pezzo e' giusto lo stesso, e' solo
        /// modificabile per sbaglio. Si scrive nell'esito e si va avanti.
        /// </summary>
        private bool BloccaSchizzo(IModelDoc2 modello, out string nota)
        {
            nota = null;
            try
            {
                var schizzo = SchizzoAppenaCreato(modello)?.GetSpecificFeature2() as ISketch;
                if (schizzo == null) { nota = "schizzo non ritrovato"; return false; }
                var segmenti = schizzo.GetSketchSegments() as object[];
                if (segmenti == null || segmenti.Length == 0) { nota = "nessun segmento"; return false; }
                var relazioni = schizzo.RelationManager;
                if (relazioni == null) { nota = "gestore relazioni assente"; return false; }
                relazioni.AddRelation(segmenti, (int)swConstraintType_e.swConstraintType_FIXED);
                return true;
            }
            catch (Exception ex)
            {
                nota = ex.Message;
                return false;
            }
        }

        /// <summary>Un contorno letto dal piano: cerchio, composito o spezzata.</summary>
        /// <summary>
        /// Quanti contorni al massimo in una sagoma sola.
        ///
        /// I limiti qui sono di CONTEGGIO, non di quota: quante volte si puo'
        /// spezzare una linea, non quanto puo' essere lunga. Una piastra da tre
        /// metri e' legittima; una sagoma da cinquemila vertici scritta da un
        /// modello quasi mai.
        /// </summary>
        private const int MaxContorni = 20;

        /// <summary>Vertici massimi per contorno, quando li scrive un modello.</summary>
        private const int MaxVerticiModello = 200;

        /// <summary>
        /// Vertici massimi per contorno, quando li genera una ricetta.
        ///
        /// I contorni che escono da <see cref="RuotaDentata"/> o da
        /// <see cref="Sezioni"/> non passano dalla testa di nessuno e possono
        /// essere molto piu' fitti: una ruota da 127 denti sono milleventi
        /// segmenti, ed e' giusto cosi'.
        /// </summary>
        private const int MaxVerticiGenerati = 4000;

        private class Contorno
        {
            public bool Cerchio;
            public double[] Centro = new double[2];
            public double Diametro;
            public List<Tratto> Tratti = new List<Tratto>();
        }

        private class Tratto
        {
            public bool Spline;
            public List<double[]> Punti = new List<double[]>();
        }

        /// <summary>
        /// I contorni dal piano. Null con il motivo se il dato non e' quello che
        /// deve essere: qui si fallisce invece di tirare a indovinare, perche'
        /// un vertice letto male non da' errore — da' una sagoma diversa.
        /// </summary>
        private static List<Contorno> Contorni(
            Dictionary<string, object> a, out string motivo, out string nota,
            int maxVertici = MaxVerticiModello)
        {
            motivo = null;
            nota = null;
            object grezzo;
            if (a == null || !a.TryGetValue("contorni", out grezzo) || grezzo == null)
            {
                motivo = "Non mi e' arrivata nessuna sagoma da disegnare.";
                return null;
            }
            var elenco = grezzo as System.Collections.IEnumerable;
            if (elenco == null) { motivo = "La sagoma non e' un elenco di contorni."; return null; }

            var fuori = new List<Contorno>();
            foreach (var c in elenco)
            {
                var voce = c as Dictionary<string, object>;
                if (voce == null)
                {
                    // Forma vecchia: un contorno era una lista nuda di punti.
                    // Si continua ad accettarla, cosi' un piano proposto prima
                    // di questa versione non diventa illeggibile a meta' strada.
                    var punti = Punti(c, out motivo);
                    if (punti == null) return null;
                    var vecchio = new Contorno();
                    vecchio.Tratti.Add(new Tratto { Punti = Chiudi(punti) });
                    fuori.Add(vecchio);
                    continue;
                }

                // DUE MODI DI SCRIVERE UN CERCHIO, E IL CATALOGO NE
                // RACCOMANDA UNO CHE IL PARSER NON CAPIVA.
                //
                // La descrizione di `estrudi_profilo` porta questo esempio
                // per esteso — «TUBO diametro esterno 60 spessore 5»:
                //
                //   [{"cerchio": [0,0], "diametro_mm": 60},
                //    {"cerchio": [0,0], "diametro_mm": 50}]
                //
                // Il parser capiva solo la forma esplicita con `tipo` e
                // `centro`, quindi un modello che copiava l'esempio del
                // catalogo finiva nel ramo poligonale, cercava `vertici`,
                // non lo trovava e falliva. Una divergenza fra cio' che si
                // dichiara e cio' che si sa fare, dentro il file che esiste
                // per non averne — e proprio sulla forma che il catalogo
                // consiglia per fori e tondi, dove sbagliare costa un pezzo
                // in cui l'albero non entra.
                bool chiaveCerchio = voce.ContainsKey("cerchio");
                var tipo = Convert.ToString(
                    voce.TryGetValue("tipo", out var t) ? t
                    : chiaveCerchio ? "cerchio" : "poligonale");
                if (string.Equals(tipo, "cerchio", StringComparison.OrdinalIgnoreCase))
                {
                    object grezzoCentro = null;
                    if (voce.TryGetValue("centro", out var ce) && ce != null) grezzoCentro = ce;
                    else if (chiaveCerchio) grezzoCentro = voce["cerchio"];
                    var centro = Punti(
                        grezzoCentro != null ? new[] { grezzoCentro } : null, out motivo);
                    if (centro == null || centro.Count != 1)
                    {
                        motivo = "Il centro del cerchio non e' [x, y]. Si scrive \"cerchio\": [x, y] oppure \"tipo\": \"cerchio\" con \"centro\": [x, y].";
                        return null;
                    }

                    // MANCANTE E NON NUMERICO SONO DUE COSE DIVERSE.
                    // Prima finivano tutte e due in «non e' un numero»,
                    // perche' la chiave assente sollevava dentro lo stesso
                    // try. Chi ha scritto {"cerchio":[0,0]} e basta va
                    // mandato a mettere il diametro, non a controllare un
                    // numero che non ha scritto.
                    object grezzoDiametro;
                    if (!voce.TryGetValue("diametro_mm", out grezzoDiametro) || grezzoDiametro == null)
                    {
                        motivo = "Un cerchio senza \"diametro_mm\": un centro da solo non dice quanto e' grande.";
                        return null;
                    }
                    double diametro;
                    try
                    {
                        diametro = Convert.ToDouble(
                            grezzoDiametro, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch { motivo = "Il diametro del cerchio non e' un numero."; return null; }
                    if (diametro <= 0)
                    {
                        motivo = "Il diametro del cerchio e' " + diametro + ": un cerchio di diametro zero o negativo non si disegna.";
                        return null;
                    }

                    fuori.Add(new Contorno { Cerchio = true, Centro = centro[0], Diametro = diametro });
                    continue;
                }

                if (string.Equals(tipo, "composito", StringComparison.OrdinalIgnoreCase))
                {
                    var contorno = new Contorno();
                    var tratti = voce.TryGetValue("tratti", out var tr) ? tr as System.Collections.IEnumerable : null;
                    if (tratti == null) { motivo = "Un contorno composito non ha tratti."; return null; }
                    foreach (var x in tratti)
                    {
                        var tt = x as Dictionary<string, object>;
                        if (tt == null) { motivo = "Un tratto non e' leggibile."; return null; }
                        var punti = Punti(tt.TryGetValue("punti", out var pp) ? pp : null, out motivo);
                        if (punti == null) return null;
                        if (punti.Count < 2) { motivo = "Un tratto ha meno di due punti."; return null; }
                        var nome = Convert.ToString(tt.TryGetValue("tratto", out var nt) ? nt : "linee");
                        contorno.Tratti.Add(new Tratto
                        {
                            Spline = string.Equals(nome, "spline", StringComparison.OrdinalIgnoreCase),
                            Punti = punti,
                        });
                    }
                    if (contorno.Tratti.Count == 0) { motivo = "Un contorno composito e' vuoto."; return null; }
                    fuori.Add(contorno);
                    continue;
                }

                var vertici = Punti(voce.TryGetValue("vertici", out var vv) ? vv : null, out motivo);
                if (vertici == null) return null;
                if (vertici.Count < 3) { motivo = "Un contorno ha meno di tre punti: non si chiude."; return null; }
                var poligonale = new Contorno();
                poligonale.Tratti.Add(new Tratto { Punti = Chiudi(vertici) });
                fuori.Add(poligonale);
            }
            if (fuori.Count == 0) { motivo = "La sagoma non ha nessun contorno."; return null; }
            if (fuori.Count > MaxContorni)
            {
                motivo = fuori.Count + " contorni sono troppi (al massimo " + MaxContorni + ").";
                return null;
            }
            if (!SagomaSensata(fuori, maxVertici, out motivo, out nota)) return null;
            return fuori;
        }

        /// <summary>
        /// I PUNTI DI UN CONTORNO, per i controlli geometrici e basta.
        ///
        /// Un cerchio si campiona a 64 lati: serve solo a sapere se sta dentro
        /// il bordo esterno, non a disegnarlo. Disegnato resta un cerchio vero.
        /// </summary>
        private static List<double[]> PuntiDi(Contorno c)
        {
            if (c.Cerchio)
            {
                const int lati = 64;
                double r = c.Diametro / 2.0;
                var fuori = new List<double[]>(lati);
                for (int i = 0; i < lati; i++)
                {
                    double t = 2 * Math.PI * i / lati;
                    fuori.Add(new[] { c.Centro[0] + r * Math.Cos(t), c.Centro[1] + r * Math.Sin(t) });
                }
                return fuori;
            }
            var tutti = new List<double[]>();
            foreach (var tratto in c.Tratti) tutti.AddRange(tratto.Punti);
            return FormaChiusa.Anello(tutti);
        }

        /// <summary>
        /// I MODI IN CUI UNA SAGOMA SBAGLIATA NON DA' ERRORE.
        ///
        /// Nessuno di questi controlli protegge da un crash: proteggono da un
        /// pezzo che si costruisce benissimo e non e' quello chiesto. E' il
        /// motivo per cui stanno qui e non nello schema — lo schema guarda la
        /// FORMA del dato, questo guarda la geometria che ne esce.
        ///
        ///   1. un contorno composito i cui tratti non si attaccano: non da'
        ///      errore, non si estrude e basta;
        ///   2. un contorno che si incrocia da solo: SOLIDWORKS costruisce le
        ///      regioni che riesce e da' un pezzo plausibile e sbagliato;
        ///   3. un contorno di area nulla;
        ///   4. **un vuoto che esce dal bordo esterno**, che invece di forare
        ///      il pezzo lo TAGLIA. Il solido che ne esce e' valido, e nessuno
        ///      dice niente.
        ///
        /// Il quarto e' il piu' insidioso dei quattro, perche' il pezzo
        /// sbagliato ha l'aria di essere venuto bene.
        /// </summary>
        private static bool SagomaSensata(
            List<Contorno> contorni, int maxVertici, out string motivo, out string nota)
        {
            motivo = null;
            nota = null;
            for (int i = 0; i < contorni.Count; i++)
            {
                var voce = contorni[i];
                string etichetta = i == 0 ? "Il contorno esterno" : "Il vuoto n. " + i;

                // Un cerchio non si puo' incrociare da solo e non ha area
                // nulla: campionarlo per scoprirlo sarebbe lavoro sprecato.
                if (voce.Cerchio) continue;

                // 1. I TRATTI SI ATTACCANO?
                // La tolleranza e' un millesimo di millimetro e non un
                // micron: i punti dei contorni generati escono arrotondati al
                // decimillesimo, e pretendere di piu' rifiuterebbe geometria
                // giusta.
                for (int j = 0; j < voce.Tratti.Count; j++)
                {
                    var fine = voce.Tratti[j].Punti[voce.Tratti[j].Punti.Count - 1];
                    var dopo = voce.Tratti[(j + 1) % voce.Tratti.Count].Punti[0];
                    if (!FormaChiusa.Vicini(fine, dopo, 1e-3))
                    {
                        motivo = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}: il tratto {1} finisce in ({2:0.###}, {3:0.###}) e il " +
                            "seguente comincia in ({4:0.###}, {5:0.###}): il contorno non " +
                            "si chiude, e un contorno aperto non da' errore — non si " +
                            "estrude e basta.",
                            etichetta, j + 1, fine[0], fine[1], dopo[0], dopo[1]);
                        return false;
                    }
                }

                var anello = PuntiDi(voce);
                if (anello.Count > maxVertici)
                {
                    motivo = etichetta + " ha " + anello.Count + " vertici (al massimo " +
                             maxVertici + ").";
                    return false;
                }
                if (anello.Count < 3)
                {
                    motivo = etichetta + " ha " + anello.Count + " vertici distinti: con " +
                             "meno di tre non si chiude una sagoma.";
                    return false;
                }

                // 2. SI INCROCIA DA SOLO?
                int dove = FormaChiusa.DoveSiIncrocia(anello);
                if (dove >= 0)
                {
                    var p = anello[dove];
                    // MISURATO, e vale la pena dirlo com'e': su un papillon
                    // SOLIDWORKS si ferma, ma l'unica cosa che dice e'
                    // «L'estrusione non e' stata creata» — che non aiuta a
                    // capire dove sia il guaio. Questo messaggio esiste per
                    // dare il punto, non per aggiungere un rifiuto.
                    motivo = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} si incrocia da solo: il lato che parte da ({1:0.###}, " +
                        "{2:0.###}) ne attraversa un altro. SOLIDWORKS si fermerebbe " +
                        "comunque, ma direbbe soltanto «L'estrusione non e' stata " +
                        "creata». Di solito e' l'ordine dei vertici.",
                        etichetta, p[0], p[1]);
                    return false;
                }

                // 3. AREA NULLA?
                if (FormaChiusa.Area(anello) <= 0)
                {
                    motivo = etichetta + " ha area nulla: i suoi punti stanno tutti " +
                             "sulla stessa retta.";
                    return false;
                }
            }

            // 4. I CONTORNI DOPO IL PRIMO: DENTRO, A CAVALLO, O FUORI?
            //
            // TRE CASI, E SOLO UNO E' UN ERRORE. La prima stesura ne rifiutava
            // due, e cosi' rifiutava lavoro legittimo:
            //
            //   dentro     e' un vuoto. Normale.
            //   a cavallo  taglia il bordo esterno. Incoerente sempre: non e'
            //              ne' un vuoto ne' una regione, e SOLIDWORKS si ferma
            //              dicendo soltanto «L'estrusione non e' stata creata».
            //   fuori      e' una REGIONE A SE'. E' legittimo: quattro alette
            //              su una faccia sono quattro contorni staccati.
            //
            // Misurato il 14/9/2026: il piano delle quattro alette del coperchio
            // (`Banco/casi/pezzi/coperchio_5.json`) veniva rifiutato da questo
            // controllo, ed era un piano giusto. **Un rifiuto di cio' che e'
            // corretto e' il peggiore dei due modi di sbagliare** — e' la
            // lezione del guscio, e questo controllo l'aveva dimenticata.
            //
            // Del terzo caso si DICE, forte: un contorno staccato diventa un
            // corpo in piu', e chi credeva di fare un foro si trova un disco
            // (misurato: piastra 100x100 con un cerchio Ø30 a 200 mm, 2 corpi,
            // «riuscita»). Ma lo si dice invece di impedirlo, e chi legge ha in
            // mano anche `corpi_totali` per controllare.
            if (contorni.Count > 1)
            {
                var esterno = PuntiDi(contorni[0]);
                var staccati = new List<int>();
                for (int i = 1; i < contorni.Count; i++)
                {
                    // TRE CASELLE, NON DUE. Un vertice ESATTAMENTE sul bordo
                    // esterno non e' ne' dentro ne' fuori, e costringerlo a
                    // essere uno dei due faceva dipendere il verdetto dal LATO:
                    // misurato su una piastra 100x100, una cava a filo del
                    // bordo destro veniva rifiutata come «a cavallo» e la
                    // stessa cava specchiata a sinistra passava.
                    //
                    // I DUE LATI DEVONO DARE LA STESSA RISPOSTA, E LA RISPOSTA
                    // E' NO. Misurato: cava a destra, cava a sinistra e rombo
                    // che tocca in un solo vertice falliscono tutti e tre con
                    // «L'estrusione non e' stata creata». Due contorni che si
                    // toccano non delimitano una regione; un'asola aperta si
                    // disegna come UN contorno solo con otto punti.
                    // Il guadagno non e' far passare di piu': e' rispondere
                    // uguale da tutt'e due le parti, e dirlo meglio di come lo
                    // dice SOLIDWORKS.
                    int dentro = 0, fuori = 0, sulBordo = 0;
                    double[] primoFuori = null;
                    foreach (var punto in PuntiDi(contorni[i]))
                    {
                        switch (FormaChiusa.DoveSta(punto, esterno))
                        {
                            case DoveSiTrova.Dentro: dentro++; break;
                            case DoveSiTrova.SulBordo: sulBordo++; break;
                            default: fuori++; if (primoFuori == null) primoFuori = punto; break;
                        }
                    }

                    if (dentro > 0 && fuori > 0)
                    {
                        motivo = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Il contorno n. {0} sta A CAVALLO del bordo esterno: il punto " +
                            "({1:0.###}, {2:0.###}) e' fuori e altri sono dentro. Non e' " +
                            "ne' un vuoto ne' una regione a se', e SOLIDWORKS si ferma " +
                            "dicendo soltanto «L'estrusione non e' stata creata». " +
                            "Spostalo tutto dentro se e' un vuoto, tutto fuori se e' un " +
                            "pezzo a parte.",
                            i, primoFuori[0], primoFuori[1]);
                        return false;
                    }
                    // TOCCA IL BORDO: non si estrude, e si dice perche'.
                    // Non e' «a cavallo» — non c'e' niente fuori — ed e' un
                    // caso a se' che merita il suo messaggio: chi l'ha scritto
                    // voleva un'asola aperta, e la strada per averla c'e'.
                    if (sulBordo > 0)
                    {
                        motivo = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Il contorno n. {0} TOCCA il bordo esterno in {1} " +
                            (sulBordo == 1 ? "punto" : "punti") + ". Due contorni che si " +
                            "toccano non delimitano una regione, e SOLIDWORKS si ferma " +
                            "dicendo soltanto «L'estrusione non e' stata creata». " +
                            "Se volevi un'asola aperta sul bordo, disegnala come UN " +
                            "contorno solo: il bordo esterno che entra e rientra. Se " +
                            "volevi un foro, spostalo dentro di almeno un decimo.",
                            i, sulBordo);
                        return false;
                    }

                    // Staccato vuol dire che non tocca nemmeno.
                    if (dentro == 0) staccati.Add(i);
                }

                if (staccati.Count > 0)
                    nota =
                        (staccati.Count == 1
                            ? "Il contorno n. " + staccati[0] + " non sta dentro il primo"
                            : "I contorni n. " + string.Join(", ", staccati) +
                              " non stanno dentro il primo") +
                        ": non sono vuoti, sono regioni a se'. Vengono estrusi lo stesso, " +
                        "e il pezzo puo' avere piu' di un corpo — guarda «corpi_totali». " +
                        "Se volevi dei fori, vanno spostati dentro il contorno esterno.";
            }
            return true;
        }

        /// <summary>La spezzata chiusa: si ripete il primo punto in coda.</summary>
        private static List<double[]> Chiudi(List<double[]> punti)
        {
            var fuori = new List<double[]>(punti);
            fuori.Add(punti[0]);
            return fuori;
        }

        /// <summary>
        /// Una lista di punti [x, y] da un dato JSON. Null con il motivo se
        /// qualcosa non e' un numero: un vertice letto male non da' errore, da'
        /// una sagoma diversa.
        /// </summary>
        private static List<double[]> Punti(object grezzo, out string motivo)
        {
            motivo = null;
            if (grezzo == null) { motivo = "Mancano i punti."; return null; }
            var elenco = grezzo as System.Collections.IEnumerable;
            if (elenco == null) { motivo = "I punti non sono un elenco."; return null; }
            var punti = new List<double[]>();
            foreach (var p in elenco)
            {
                var coppia = p as System.Collections.IEnumerable;
                if (coppia == null) { motivo = "Un punto non e' una coppia [x, y]."; return null; }
                var valori = new List<double>();
                foreach (var v in coppia)
                {
                    try
                    {
                        valori.Add(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    catch { motivo = "Un punto contiene un valore che non e' un numero: " + v; return null; }
                }
                if (valori.Count != 2) { motivo = "Un punto non ha esattamente due coordinate."; return null; }
                if (double.IsNaN(valori[0]) || double.IsNaN(valori[1])
                    || double.IsInfinity(valori[0]) || double.IsInfinity(valori[1]))
                { motivo = "Un punto ha coordinate non utilizzabili."; return null; }
                punti.Add(new[] { valori[0], valori[1] });
            }
            if (punti.Count == 0) { motivo = "Un elenco di punti e' vuoto."; return null; }
            return punti;
        }

        private Esito EstrudiCerchio(IModelDoc2 modello, Dictionary<string, object> a)
        {
            double diametro = Num(a, "diametro_mm");
            double altezza = Num(a, "altezza_mm");
            string piano = Str(a, "piano", "frontale");

            if (!SelezionaPiano(modello, piano))
                return Fallito($"Non ho trovato il piano «{piano}» nell'albero.");

            var schizzi = modello.SketchManager;
            schizzi.InsertSketch(true);
            var cerchio = schizzi.CreateCircleByRadius(0, 0, 0, Unita.M(diametro / 2));

            // IL DIAMETRO, sullo schizzo. Un pezzo tondo senza la quota del
            // diametro e' modificabile quanto una vasca senza i lati: cioe'
            // per niente. Il testo va messo fuori dal cerchio, in diagonale,
            // se no finisce sopra il disegno.
            double lontano = Unita.M(diametro / 2) * 1.4 + 0.005;
            int quoteMesse = QuotaSegmenti(modello,
                new List<Quota> { Quota.Su(cerchio, new[] { lontano, lontano, 0.0 }) });

            schizzi.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Estrudi(modello, altezza, unisci: true);
            if (feature == null) return Fallito("L'estrusione non e' stata creata." + SenzaLoSchizzo(modello));
            return Riuscito(feature, new Dictionary<string, object>
            {
                ["diametro_mm"] = diametro,
                ["altezza_mm"] = altezza,
                ["quote_sullo_schizzo"] = quoteMesse,
            }, Riferimenti.NormaleDelPiano(piano));
        }

        // ─────────────────────────────────────────────────────────────────
        // DUE DIFETTI DIVERSI NON POSSONO DARE LA STESSA FRASE
        //
        // Misurato l'11 settembre 2026 (RAG 80u, casi 4 e 7): un foro da 500
        // mm su una faccia 314x274, e un foro chiesto a x = 5000 su un pezzo
        // che arriva a 157, finivano tutti e due con
        //
        //     «Il taglio non e' stato creato. Nel pezzo e' rimasto lo schizzo»
        //
        // che non e' una diagnosi: e' la constatazione che SolidWorks ha detto
        // di no. Chi legge non sa se deve rimpicciolire il foro, spostarlo, o
        // cambiare faccia — e le due cose da fare sono opposte.
        //
        // Le due cause pero' si distinguono PRIMA di disegnare, con numeri gia'
        // in mano: l'ingombro del pezzo e il profilo chiesto. Il modello da
        // imitare e' il raccordo, che nella stessa tornata non ha detto «non e'
        // riuscito» ma «200 mm e' troppo per lo spigolo «E1», lungo 314 mm: non
        // ci sta neanche meta'» — cioe' il CRITERIO, non il verdetto: chi legge
        // sa scegliere R150 senza doverlo indovinare.
        //
        // PERCHE' I CONTROLLI SONO QUESTI DUE E NON DUE PIU' STRETTI.
        // Nella stessa tornata il guscio da 30 su una scatola alta 52 sembrava
        // l'impossibile piu' evidente ed era solo brutto: un controllo scritto
        // sull'apparenza avrebbe rifiutato un pezzo legittimo, cioe' avrebbe
        // trasformato una disattenzione in una regola del prodotto. Quindi qui
        // si rifiuta SOLO cio' che non puo' produrre nessun pezzo:
        //
        //   1. il profilo cade tutto fuori dall'ingombro — sotto non c'e'
        //      materiale, non si toglie niente;
        //   2. il profilo COPRE tutto l'ingombro e il taglio va da parte a
        //      parte — non resta niente.
        //
        // Un foro che sborda dalla faccia, un foro centrato sul bordo, una
        // tasca che mangia meta' pezzo passano di qui e li giudica SolidWorks
        // come prima: sono insoliti, non impossibili.
        //
        // E il controllo sta PRIMA dello schizzo apposta. Le operazioni che si
        // controllano da sole devono disegnare per poter misurare, e quando
        // bocciano lo schizzo c'e' gia': tocca a `SenzaLoSchizzo` andarlo a
        // togliere, e se non ci riesce a dirlo (`LasciatoSchizzo`). Questi due
        // no: non c'e' niente da misurare che non si sappia gia', quindi il
        // pezzo resta come era e non c'e' niente da togliere — il rifiuto piu'
        // economico e' quello che non ha disegnato niente.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Il nome dell'asse del modello, per poterlo dire a chi legge.</summary>
        private static string NomeAsse(int indice)
        {
            return indice == 0 ? "X" : indice == 1 ? "Y" : "Z";
        }

        /// <summary>
        /// L'ingombro di TUTTO il pezzo in mm, nelle coordinate del modello:
        /// {minX, minY, minZ, maxX, maxY, maxZ}. Null se non c'e' nessun corpo.
        ///
        /// GetBodyBox, non i sei GetExtremePoint che usa lo stato. La scatola
        /// approssimata puo' essere PIU' GRANDE del pezzo vero, mai piu'
        /// piccola — e qui serve esattamente cosi': ogni suo errore rende i due
        /// controlli piu' PERMISSIVI. Un profilo che passa di qui e non doveva
        /// lo boccia SolidWorks un istante dopo; un profilo bocciato per
        /// sbaglio invece non lo rivede nessuno.
        /// </summary>
        private static double[] ScatolaDelPezzoMm(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            var corpi = parte?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null || corpi.Length == 0) return null;
            double[] fuori = null;
            foreach (var c in corpi)
            {
                var b = (c as IBody2)?.GetBodyBox() as double[];
                if (b == null || b.Length < 6) continue;
                if (fuori == null)
                {
                    fuori = new double[6];
                    for (int i = 0; i < 6; i++) fuori[i] = Unita.Mm(b[i]);
                    continue;
                }
                for (int i = 0; i < 3; i++) fuori[i] = Math.Min(fuori[i], Unita.Mm(b[i]));
                for (int i = 3; i < 6; i++) fuori[i] = Math.Max(fuori[i], Unita.Mm(b[i]));
            }
            return fuori;
        }

        /// <summary>
        /// Il taglio esce dall'altra parte del pezzo?
        ///
        /// Serve al secondo controllo e solo a quello: un taglio che NON passa
        /// puo' coprire tutta la faccia senza nessun problema — accorcia il
        /// pezzo, e accorciare un pezzo e' una cosa che si fa. Un cieco esce
        /// comunque quando la profondita' supera lo spessore misurato lungo la
        /// normale della faccia.
        /// </summary>
        private static bool DaParteAParte(
            IModelDoc2 modello, double[] assi, bool passante, double profonditaMm)
        {
            if (passante) return true;
            if (profonditaMm <= 0 || assi == null) return false;
            double spessore = EstensioneLungoLAsse(modello, (int)assi[2]);
            return spessore > 0 && profonditaMm >= spessore;
        }

        /// <summary>
        /// I due controlli, per tutte e tre le operazioni che tagliano una
        /// sagoma su una faccia. Ritorna null quando il taglio si puo' provare,
        /// altrimenti il motivo, scritto in modo che chi legge sappia quale
        /// numero cambiare.
        /// </summary>
        /// <param name="cosaE">
        /// La sagoma con le sue misure, gia' scritta: «Il foro da 500 mm».
        /// </param>
        /// <param name="semiU">Mezzo ingombro del profilo sul primo asse.</param>
        /// <param name="semiV">Mezzo ingombro del profilo sul secondo asse.</param>
        /// <param name="dentroIlProfilo">Questo punto della faccia sta dentro la sagoma?</param>
        /// <param name="consiglio">
        /// Date le due distanze dal centro al bordo piu' vicino del pezzo, la
        /// frase che dice cosa ci starebbe. La scrive ogni operazione, perche'
        /// «e' da 274 mm» e «e' 314 x 274 mm» non sono la stessa frase.
        /// </param>
        private static string TaglioSenzaSperanza(
            IModelDoc2 modello,
            double[] assi,
            string cosaE,
            double xMm,
            double yMm,
            double semiU,
            double semiV,
            Func<double, double, bool> dentroIlProfilo,
            bool daParteAParte,
            Func<double, double, string> consiglio)
        {
            if (assi == null) return null;
            var scatola = ScatolaDelPezzoMm(modello);
            if (scatola == null) return null;   // niente corpo: non c'e' niente da confrontare

            int iu = (int)assi[0], iv = (int)assi[1];
            double uMin = scatola[iu], uMax = scatola[iu + 3];
            double vMin = scatola[iv], vMax = scatola[iv + 3];
            string nomeU = NomeAsse(iu), nomeV = NomeAsse(iv);

            // ── 1. IL PROFILO CADE TUTTO FUORI DAL PEZZO ──────────────────
            // Si confrontano le due SCATOLE: se non si toccano nemmeno quelle,
            // le due forme non si toccano di sicuro. Il contrario non vale — due
            // scatole che si toccano non dicono niente sulle forme — e va bene
            // cosi': il dubbio lo scioglie SolidWorks, non questo controllo.
            if (xMm + semiU < uMin || xMm - semiU > uMax ||
                yMm + semiV < vMin || yMm - semiV > vMax)
            {
                return string.Format(
                    "{0} e' centrato a {1} = {2:0.##}, {3} = {4:0.##} mm, ma il pezzo su {1} " +
                    "va da {5:0.##} a {6:0.##} e su {3} da {7:0.##} a {8:0.##}: il profilo cade " +
                    "tutto fuori dal materiale, quindi non c'e' niente da togliere.",
                    cosaE, nomeU, xMm, nomeV, yMm, uMin, uMax, vMin, vMax);
            }

            // ── 2. IL PROFILO COPRE TUTTO IL PEZZO, E IL TAGLIO PASSA ─────
            // Bastano i quattro angoli: cerchio, rettangolo e stadio sono tutti
            // e tre CONVESSI, e una forma convessa che contiene i quattro
            // angoli contiene tutta la scatola.
            if (daParteAParte &&
                dentroIlProfilo(uMin, vMin) && dentroIlProfilo(uMin, vMax) &&
                dentroIlProfilo(uMax, vMin) && dentroIlProfilo(uMax, vMax))
            {
                double du = Math.Min(xMm - uMin, uMax - xMm);
                double dv = Math.Min(yMm - vMin, vMax - yMm);
                // Il consiglio ha senso solo se il centro sta DENTRO il pezzo:
                // da un centro fuori, «il piu' grande che ci sta» sarebbe un
                // numero negativo spacciato per una misura.
                string dritta = du > 0 && dv > 0 ? " " + consiglio(du, dv) : "";
                return string.Format(
                    "{0} centrato a {1} = {2:0.##}, {3} = {4:0.##} mm copre tutto il pezzo, che " +
                    "qui occupa {5:0.##} x {6:0.##} mm: un taglio da parte a parte cosi' non " +
                    "lascerebbe niente.{7}",
                    cosaE, nomeU, xMm, nomeV, yMm, uMax - uMin, vMax - vMin, dritta);
            }

            return null;
        }

        /// <summary>
        /// Il punto (u, v) sta dentro lo stadio dell'asola? E' la distanza dal
        /// SEGMENTO fra i due centri delle estremita': minore del raggio vuol
        /// dire dentro, ed e' vero sia nella parte dritta sia nei due
        /// semicerchi. Gli stessi corsa/raggio/cos/sin che disegnano la sagoma.
        /// </summary>
        private static bool DentroLoStadio(
            double u, double v,
            double centroU, double centroV,
            double cos, double sin, double corsa, double raggio)
        {
            double du = u - centroU, dv = v - centroV;
            double t = du * cos + dv * sin;
            if (t > corsa) t = corsa;
            else if (t < -corsa) t = -corsa;
            double pu = centroU + t * cos, pv = centroV + t * sin;
            return (u - pu) * (u - pu) + (v - pv) * (v - pv) <= raggio * raggio;
        }

        // ─────────────────────────────────────────────────────────────────
        // QUANDO NON E' NE' LA MISURA NE' LA POSIZIONE: SI MISURA LO STESSO,
        // MA PER SPIEGARE
        //
        // Misurato il 14 settembre 2026, subito dopo la 0.43.0. Blocco
        // 314x274x52, foro da 270 passante, poi un foro da 20 centrato DENTRO
        // quel buco. I due controlli della 0.42.0 lo lasciano passare — e
        // fanno bene, il profilo sta dentro l'ingombro e non lo copre —
        // SolidWorks non crea la feature, e la 0.43.0 si porta via lo schizzo
        // come deve. Quello che resta in mano a chi legge e':
        //
        //     «Il taglio non e' stato creato.»
        //
        // Quattro parole. E' di nuovo la malattia di 80u — una constatazione
        // invece di una diagnosi — anzi peggio di prima, perche' prima almeno
        // diceva dello schizzo rimasto.
        //
        // PERCHE' QUI NON SI PUO' AGGIUNGERE UN RIFIUTO. La tentazione e'
        // ovvia: «se sotto il centro non c'e' materiale, rifiuta». Ma allargare
        // un foro da 20 a 30 ha il centro ESATTAMENTE nel vuoto ed e'
        // un'operazione che si fa tutti i giorni. Sarebbe la trappola del
        // guscio di 80u presa per la terza volta.
        //
        // Quindi si misura per SPIEGARE. La differenza non e' una sfumatura:
        // un rifiuto sbagliato blocca un lavoro legittimo e nessuno lo rivede,
        // una spiegazione sbagliata la corregge chi legge in tre secondi
        // perche' ha il pezzo davanti. Il costo dell'errore non e' lo stesso,
        // e neanche il diritto di sbagliare.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Questo punto, dato nelle coordinate della faccia (le stesse x/y che
        /// arrivano dal piano), cade DAVVERO sulla faccia?
        ///
        /// FIRMA VERIFICATA per riflessione sull'interop 29.5.1.1:
        ///   IFace2.GetClosestPointOn(Double X, Double Y, Double Z) -> Object
        ///
        /// E LA DIFFERENZA CHE CONTA E' CON `ISurface.GetClosestPointOn`, che
        /// esiste con la stessa identica firma un livello piu' sotto. La
        /// SUPERFICIE non sa niente dei contorni: di un piano bucato conosce il
        /// piano, non il buco. La FACCIA i contorni li ha. Chiesto a una faccia
        /// forata quale sia il suo punto piu' vicino a un punto che sta nel
        /// foro, risponde un punto sul BORDO del foro — cioe' diverso da quello
        /// che le abbiamo dato; se il punto sta sul pieno, risponde il punto
        /// stesso. E' un «dentro o fuori» chiesto a SolidWorks invece che
        /// dedotto da noi, ed e' la stessa postura di `IGetChildCount` in
        /// <see cref="SenzaLoSchizzo"/>.
        ///
        /// Non serve nessuno schizzo aperto: qui si resta nel sistema del
        /// MODELLO e non si passa da `PuntoNelloSchizzo`.
        /// </summary>
        private static bool PuntoSullaFaccia(
            IFace2 faccia, double[] assi, double offsetM, double xMm, double yMm)
        {
            if (faccia == null || assi == null) return false;
            try
            {
                var p = new double[3];
                p[(int)assi[0]] = Unita.M(xMm);
                p[(int)assi[1]] = Unita.M(yMm);
                p[(int)assi[2]] = offsetM;
                var vicino = faccia.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (vicino == null || vicino.Length < 3) return false;
                double dx = vicino[0] - p[0], dy = vicino[1] - p[1], dz = vicino[2] - p[2];
                // Un micron, al quadrato. Non e' una tolleranza di lavorazione:
                // e' il margine per l'aritmetica in virgola mobile, perche' su
                // un punto che sta sulla faccia la risposta e' il punto stesso,
                // non un'approssimazione.
                return dx * dx + dy * dy + dz * dz <= 1e-12;
            }
            catch { return false; }
        }

        /// <summary>
        /// Cosa c'e' sotto il profilo, detto contando. Ritorna la frase da
        /// aggiungere al motivo del fallimento, vuota se non si e' potuto
        /// misurare niente.
        ///
        /// NON RIFIUTA NIENTE E NON DEVE MAI FARLO: si chiama solo quando
        /// SolidWorks ha gia' detto di no, e serve a dire a chi legge quale
        /// delle strade e' ancora aperta.
        /// </summary>
        private static string SottoIlProfilo(
            IFace2 faccia, string uidFaccia, double[] assi, double offsetM,
            List<double[]> campioni)
        {
            if (faccia == null || assi == null || campioni == null || campioni.Count == 0)
                return "";

            int sul = 0;
            foreach (var c in campioni)
                if (PuntoSullaFaccia(faccia, assi, offsetM, c[0], c[1])) sul++;

            // «sulla faccia», non «su la faccia»: la preposizione si articola
            // qui dentro, perche' le tre frasi la usano tutte e tre.
            string dove = string.IsNullOrEmpty(uidFaccia)
                ? "sulla faccia scelta"
                : "sulla faccia «" + uidFaccia + "»";
            int quanti = campioni.Count;

            if (sul == 0)
                return " Ho provato " + quanti + " punti sotto il profilo e nessuno cade " +
                       dove + ": li' il pezzo e' gia' aperto, e dove non c'e' materiale non " +
                       "c'e' niente da togliere. Sposta il profilo sul pieno, oppure scegli " +
                       "la faccia su cui il materiale c'e'.";

            if (sul == quanti)
                return " Sotto il profilo il materiale c'e': tutti e " + quanti + " i punti " +
                       "che ho provato cadono " + dove + ". Quindi non e' la posizione — " +
                       "restano la profondita' e il verso del taglio.";

            return " Sotto il profilo il materiale c'e' solo in parte: " + sul + " punti su " +
                   quanti + " cadono " + dove + ", gli altri no. Il profilo sta a cavallo " +
                   "di un bordo o di un'apertura.";
        }

        /// <summary>
        /// I punti da provare dentro un cerchio: il centro e due anelli. Due e
        /// non uno perche' un anello solo, preso vicino al bordo, racconta il
        /// bordo e non il profilo; preso vicino al centro, racconta il centro.
        /// </summary>
        private static List<double[]> CampioniCerchio(double x, double y, double raggio)
        {
            var fuori = new List<double[]> { new[] { x, y } };
            foreach (double frazione in new[] { 0.55, 0.93 })
                for (int i = 0; i < 8; i++)
                {
                    double t = i * Math.PI / 4;
                    fuori.Add(new[]
                    {
                        x + raggio * frazione * Math.Cos(t),
                        y + raggio * frazione * Math.Sin(t),
                    });
                }
            return fuori;
        }

        /// <summary>Il centro, i quattro mezzi lati e i quattro angoli, tirati dentro.</summary>
        private static List<double[]> CampioniRettangolo(
            double x, double y, double lunghezza, double larghezza)
        {
            double a = lunghezza / 2 * 0.93, b = larghezza / 2 * 0.93;
            return new List<double[]>
            {
                new[] { x, y },
                new[] { x - a, y }, new[] { x + a, y },
                new[] { x, y - b }, new[] { x, y + b },
                new[] { x - a, y - b }, new[] { x + a, y - b },
                new[] { x - a, y + b }, new[] { x + a, y + b },
            };
        }

        /// <summary>
        /// I punti da provare dentro lo stadio: tre stazioni lungo l'asse
        /// lungo, ognuna con il suo punto in mezzo e i due di fianco, piu' i
        /// due colmi. Gli stessi corsa/raggio/cos/sin che lo disegnano.
        /// </summary>
        private static List<double[]> CampioniStadio(
            double x, double y, double cos, double sin, double corsa, double raggio)
        {
            var fuori = new List<double[]>();
            Func<double, double, double[]> nelPiano = (u, v) => new[]
            {
                x + u * cos - v * sin,
                y + u * sin + v * cos,
            };
            foreach (double u in new[] { -corsa, 0.0, corsa })
                foreach (double v in new[] { 0.0, -raggio * 0.9, raggio * 0.9 })
                    fuori.Add(nelPiano(u, v));
            fuori.Add(nelPiano(-(corsa + raggio) * 0.93, 0));
            fuori.Add(nelPiano(+(corsa + raggio) * 0.93, 0));
            return fuori;
        }

        private Esito TaglioCilindrico(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string uidFaccia = Str(a, "faccia_uid", "");
            var faccia = RisolviFaccia(stato, a, uidFaccia, out string motivo);
            if (faccia == null) return Fallito(motivo);

            double[] assi = AssiDellaFaccia(faccia, out double offsetM, out string errore);
            if (assi == null) return Fallito(errore);

            double diametro = Num(a, "diametro_mm");
            bool passante = Bool(a, "passante", true);
            double profondita = Num(a, "profondita_mm", 0);
            double x, y;
            if (!NumObbligatorio(a, "x_mm", out x, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "y_mm", out y, out errore)) return Fallito(errore);

            // SI MISURA PRIMA, perche' dopo non si puo' piu'.
            // Serve al controllo sul volume in fondo: vedi `NotaSulVolume`.
            double volumePrima = VolumeMm3(modello);

            double raggioForo = diametro / 2;
            string senzaSperanza = TaglioSenzaSperanza(
                modello, assi,
                "Il foro da " + diametro.ToString("0.##") + " mm",
                x, y, raggioForo, raggioForo,
                (u, v) => (u - x) * (u - x) + (v - y) * (v - y) <= raggioForo * raggioForo,
                DaParteAParte(modello, assi, passante, profondita),
                (du, dv) => "Il piu' grande che ci sta, centrato li', e' da " +
                            (2 * Math.Min(du, dv)).ToString("0.##") + " mm.");
            if (senzaSperanza != null) return Fallito(senzaSperanza);

            if (!SelezionaFaccia(faccia)) return Fallito("Non sono riuscito a selezionare la faccia.");
            var schizzi = modello.SketchManager;
            schizzi.InsertSketch(true);
            var centro = PuntoNelloSchizzo(modello, assi, offsetM, x, y, out errore);
            if (centro == null)
            {
                // Lo schizzo aperto va chiuso comunque: lasciarlo aperto
                // bloccherebbe il documento in modalita' schizzo davanti a una
                // persona che non ha chiesto niente di tutto questo.
                try { schizzi.InsertSketch(false); } catch { }
                return Fallito(errore);
            }
            var cerchioTaglio = schizzi.CreateCircleByRadius(
                centro[0], centro[1], centro[2], Unita.M(diametro / 2));

            // IL DIAMETRO, SULLO SCHIZZO DEL FORO.
            //
            // Senza, il foro non e' modificabile con `modifica_quota` e la sua
            // tavola resta muta: `quote_dal_modello` non ha niente da
            // riportare. E' la lamentela piu' frequente dell'officina —
            // un pezzo che si puo' fare ma non si puo' correggere.
            //
            // SI QUOTA LA DIMENSIONE, NON LA POSIZIONE. La posizione vorrebbe
            // un riferimento da cui misurare, e sceglierlo per conto di chi
            // disegna vuol dire vincolare lo schizzo piu' di quanto abbia
            // chiesto: il `modifica_quota` dopo fallirebbe con un messaggio di
            // sovra-vincolo che non parla di niente.
            string erroreTesto;
            var testoQuota = PuntoNelloSchizzo(
                modello, assi, offsetM,
                x + raggioForo * 1.4 + 5, y + raggioForo * 1.4 + 5, out erroreTesto);
            int quoteMesse = cerchioTaglio != null && testoQuota != null
                ? QuotaSegmenti(modello,
                    new List<Quota> { Quota.Su(cerchioTaglio, testoQuota) })
                : 0;

            schizzi.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Taglia(modello, passante, profondita);
            if (feature == null)
            {
                // Si misura PRIMA di togliere lo schizzo: la faccia resta
                // valida comunque, ma l'ordine giusto e' leggere il pezzo
                // com'era quando SolidWorks ha detto di no.
                string sotto = SottoIlProfilo(
                    faccia, uidFaccia, assi, offsetM,
                    CampioniCerchio(x, y, raggioForo));
                return Fallito("Il taglio non e' stato creato." + sotto + SenzaLoSchizzo(modello));
            }

            // UN FORO PASSANTE SU UNA SCATOLA VUOTA NE FA DUE.
            //
            // Misurato il 10 settembre 2026 disegnando il contenitore per
            // l'antenna: chiesto il passaggio del cavo su un fianco, con
            // «passante». Tolti 3.922,2 mm3 contro i 1.961,1 di una parete
            // sola — esattamente il doppio, perche' il taglio e' uscito
            // anche dal retro. L'operazione ha detto «riuscito», il pezzo
            // aveva un buco in piu', e nessun controllo se n'e' accorto.
            //
            // Chi disegna una scatola e scrive «passante» intende quasi
            // sempre «attraversa QUESTA parete». Il numero di pareti
            // forate non si indovina, ma si CONTA: ogni parete attraversata
            // lascia una faccia cilindrica del diametro chiesto.
            int pareti = ParetiForate(feature, diametro);
            if (passante && pareti > 1)
                return Fallito(
                    "Il foro passante ha attraversato " + pareti + " pareti, " +
                    "non una: il pezzo e' cavo e il taglio e' uscito anche " +
                    "dall'altra parte. Se volevi bucare solo questa parete, " +
                    "chiedilo cieco con la profondita' dello spessore." +
                    RestaNelPezzo);

            var misure = new Dictionary<string, object>
            {
                ["diametro_mm"] = diametro,
                ["passante"] = passante,
                ["pareti_forate"] = pareti,
                ["quote_sullo_schizzo"] = quoteMesse,
            };

            // QUANTO SE N'E' ANDATO DAVVERO. Vedi `NotaSulVolume`.
            NotaSulVolume(
                misure, volumePrima, VolumeMm3(modello),
                passante ? 0 : Math.PI * raggioForo * raggioForo * profondita,
                "Il foro da " + diametro.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm");

            return Riuscito(feature, misure, NormaleDellaFaccia(faccia));
        }

        /// <summary>
        /// Quanto una lavorazione puo' discostarsi dal volume atteso prima che
        /// valga la pena dirlo. Un centesimo.
        ///
        /// Misurato il 14/9/2026 sui due casi che contano: un foro tutto sulla
        /// faccia toglie il volume esatto (192.000 - 191.607,3 = 392,7 contro
        /// i 392,699 di pi*5^2*5, cioe' meno di un millesimo di scostamento), e
        /// uno centrato sul bordo ne toglie **la meta'**. Fra i due c'e' un
        /// fattore cinquecento: qualunque soglia in mezzo funziona, e l'uno per
        /// cento lascia passare l'arrotondamento senza lasciar passare niente
        /// che abbia una forma.
        /// </summary>
        private const double ScostamentoVolume = 0.01;

        /// <summary>
        /// SI DICE QUANTO SE N'E' ANDATO, E QUANTO SE NE DOVEVA ANDARE.
        ///
        /// IL DIFETTO CHE QUESTO CHIUDE (voce E13). Un `taglio_cilindrico` con
        /// il profilo a cavallo del bordo della faccia toglie meta' materiale e
        /// risponde `riuscita: true`, `pareti_forate: 1`. Misurato: foro da 10
        /// profondo 5 centrato sul bordo inferiore di una faccia, **196,3 mm³
        /// invece di 392,7** — esattamente meta' — con il fondo del foro ridotto
        /// a un mezzo disco da 39,27 mm² invece di 78,54.
        ///
        /// IL GUARDIANO C'ERA E GUARDAVA DALL'ALTRA PARTE. `SottoIlProfilo` sa
        /// contare quanti punti del profilo cadono sulla faccia e lo dice bene,
        /// ma il suo commento dichiara: «NON RIFIUTA NIENTE E NON DEVE MAI
        /// FARLO: si chiama solo quando SOLIDWORKS ha gia' detto di no». Quando
        /// SOLIDWORKS dice di SI', non lo chiama nessuno.
        ///
        /// NON SI RIFIUTA, SI DICE. L'operazione e' riuscita e il pezzo esiste:
        /// rifiutare adesso vorrebbe dire buttare una lavorazione fatta per un
        /// sospetto. Si scrive quanto se n'e' andato, quanto ci si aspettava, e
        /// di quanto si discosta — poi decide chi legge. E' la stessa forma di
        /// `posizioni_non_verificate` e dell'ancora che si sposta.
        ///
        /// SOLO SUI CIECHI L'ATTESO E' UNA MOLTIPLICAZIONE. Per un passante il
        /// volume dipende da quanto materiale c'e' sotto, che non si sa senza
        /// misurarlo: li' si scrive `tolti_mm3` e basta, senza un atteso che
        /// sarebbe inventato. Un allarme sbagliato su un passante legittimo
        /// sarebbe peggio del difetto che si sta chiudendo.
        ///
        /// <param name="attesoMm3">Zero quando non si puo' sapere.</param>
        /// </summary>
        private static void NotaSulVolume(
            Dictionary<string, object> misure,
            double volumePrima, double volumeDopo, double attesoMm3, string cosa)
        {
            if (misure == null) return;
            double tolti = volumePrima - volumeDopo;

            // Un volume che non si e' potuto leggere non diventa uno zero: uno
            // zero vorrebbe dire «non ha tolto niente», che e' un'affermazione.
            //
            // `<= 0` SU TUTTI E DUE, e la prima scrittura aveva `volumeDopo < 0`.
            // `VolumeMm3` non torna MAI un negativo: risponde 0 quando il
            // documento non e' una parte, quando `GetBodies2` da' null e dal
            // `catch` che avvolge tutto — cioe' su ogni strada di errore.
            // Quindi quel ramo non poteva scattare, e uno zero di lettura
            // fallita passava come se fosse una misura: `tolti` diventava
            // l'INTERO volume del pezzo, e su un foro perfettamente riuscito
            // usciva una `nota_volume` che diceva «192.000 mm³ invece di 392,7».
            // L'allarme falso sul corretto, cioe' esattamente cio' che questa
            // voce dichiara peggiore del difetto che sta chiudendo.
            //
            // Il commento qui sopra diceva gia' la cosa giusta, ed e' stata la
            // riga a non farla: e' la forma di difetto piu' frequente trovata
            // in questo progetto, commessa mentre la si documentava.
            //
            // `Guscio` la scrive giusta quindici righe piu' in la'
            // (`volumePrima > 0 && volumeDopo > 0`): la stessa cosa, fatta bene,
            // nello stesso file.
            if (volumePrima <= 0 || volumeDopo <= 0) return;

            misure["tolti_mm3"] = Math.Round(tolti, 2);
            if (attesoMm3 <= 0) return;

            misure["attesi_mm3"] = Math.Round(attesoMm3, 2);
            double scostamento = Math.Abs(tolti - attesoMm3) / attesoMm3;
            if (scostamento <= ScostamentoVolume) return;

            misure["nota_volume"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} ha tolto {1:0.##} mm³ invece dei {2:0.##} che ci si aspettava " +
                "({3:0.#}% {4}). L'operazione e' riuscita, ma il pezzo non e' quello " +
                "che il piano descriveva: di solito vuol dire che il profilo cade in " +
                "parte fuori dalla faccia, o su materiale gia' tolto. Guarda il pezzo " +
                "prima di andare avanti.",
                cosa, tolti, attesoMm3, scostamento * 100,
                tolti < attesoMm3 ? "in meno" : "in piu'");
        }

        private Esito TaglioRettangolare(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string uidFaccia = Str(a, "faccia_uid", "");
            var faccia = RisolviFaccia(stato, a, uidFaccia, out string motivo);
            if (faccia == null) return Fallito(motivo);

            double[] assi = AssiDellaFaccia(faccia, out double offsetM, out string errore);
            if (assi == null) return Fallito(errore);

            double lunghezza = Num(a, "lunghezza_mm");
            double larghezza = Num(a, "larghezza_mm");
            bool passante = Bool(a, "passante", false);
            double profondita = Num(a, "profondita_mm", 0);
            if (!passante && profondita <= 0)
                return Fallito("Tasca non passante senza profondita'.");

            // SI MISURA PRIMA, perche' dopo non si puo' piu'.
            double volumePrima = VolumeMm3(modello);
            double x, y;
            if (!NumObbligatorio(a, "x_mm", out x, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "y_mm", out y, out errore)) return Fallito(errore);

            string senzaSperanza = TaglioSenzaSperanza(
                modello, assi,
                "La tasca " + lunghezza.ToString("0.##") + " x " + larghezza.ToString("0.##") + " mm",
                x, y, lunghezza / 2, larghezza / 2,
                (u, v) => Math.Abs(u - x) <= lunghezza / 2 && Math.Abs(v - y) <= larghezza / 2,
                DaParteAParte(modello, assi, passante, profondita),
                (du, dv) => "La piu' grande che ci sta, centrata li', e' " +
                            (2 * du).ToString("0.##") + " x " + (2 * dv).ToString("0.##") + " mm.");
            if (senzaSperanza != null) return Fallito(senzaSperanza);

            if (!SelezionaFaccia(faccia)) return Fallito("Non sono riuscito a selezionare la faccia.");
            var schizzi = modello.SketchManager;
            schizzi.InsertSketch(true);
            var p1 = PuntoNelloSchizzo(modello, assi, offsetM, x - lunghezza / 2, y - larghezza / 2, out errore);
            var p2 = p1 == null
                ? null
                : PuntoNelloSchizzo(modello, assi, offsetM, x + lunghezza / 2, y + larghezza / 2, out errore);
            if (p1 == null || p2 == null)
            {
                try { schizzi.InsertSketch(false); } catch { }
                return Fallito(errore);
            }
            var latiTaglio = schizzi.CreateCornerRectangle(
                p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]) as object[];

            // LE DUE QUOTE DELLA TASCA, per lo stesso motivo del foro tondo.
            //
            // Si prendono i primi due lati e basta: sono adiacenti, quindi
            // perpendicolari, quindi uno da' la lunghezza e l'altro la
            // larghezza. QUALE dei due sia l'uno e quale l'altro non si
            // stabilisce a priori — su una faccia qualunque «orizzontale» non
            // vuol dire niente — e non serve saperlo: le due quote nascono
            // comunque giuste, e il criterio della voce si verifica MISURANDO
            // i valori, non deducendoli dall'ordine dei lati.
            int quoteMesse = 0;
            if (latiTaglio != null && latiTaglio.Length >= 2)
            {
                string erroreTesto;
                var sotto = PuntoNelloSchizzo(
                    modello, assi, offsetM,
                    x, y - larghezza / 2 - 5, out erroreTesto);
                var aFianco = PuntoNelloSchizzo(
                    modello, assi, offsetM,
                    x + lunghezza / 2 + 5, y, out erroreTesto);
                var elenco = new List<Quota>();
                if (sotto != null) elenco.Add(Quota.Su(latiTaglio[0], sotto));
                if (aFianco != null) elenco.Add(Quota.Su(latiTaglio[1], aFianco));
                quoteMesse = QuotaSegmenti(modello, elenco);
            }

            schizzi.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Taglia(modello, passante, profondita);
            if (feature == null)
            {
                string sotto = SottoIlProfilo(
                    faccia, uidFaccia, assi, offsetM,
                    CampioniRettangolo(x, y, lunghezza, larghezza));
                return Fallito("Il taglio non e' stato creato." + sotto + SenzaLoSchizzo(modello));
            }
            var misureTasca = new Dictionary<string, object>
            {
                ["lunghezza_mm"] = lunghezza,
                ["larghezza_mm"] = larghezza,
                ["passante"] = passante,
                ["quote_sullo_schizzo"] = quoteMesse,
            };

            // LO STESSO DIFETTO DI `taglio_cilindrico`, e la stessa riparazione
            // (voce E13). Una tasca il cui profilo cade in parte fuori dalla
            // faccia toglie meno materiale di quanto il piano descriveva, e
            // risponde «riuscita» lo stesso.
            NotaSulVolume(
                misureTasca, volumePrima, VolumeMm3(modello),
                passante ? 0 : lunghezza * larghezza * profondita,
                "La tasca " + lunghezza.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "x" + larghezza.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            return Riuscito(feature, misureTasca, NormaleDellaFaccia(faccia));
        }

        /// <summary>
        /// L'ASOLA: un foro allungato, cioe' il fissaggio che si registra.
        ///
        /// PERCHE' SERVE UNA OPERAZIONE SUA. In carpenteria l'asola non e' un
        /// vezzo: e' quello che permette di montare. Una staffa con due asole
        /// si allinea in opera, una staffa con due fori tondi o entra o si
        /// rifa'. Con il catalogo di prima si poteva chiedere solo un foro
        /// tondo (`taglio_cilindrico`) o una tasca rettangolare
        /// (`taglio_rettangolare`): la seconda ci somiglia, ma ha gli spigoli
        /// vivi e la vite ci balla dentro invece di scorrere.
        ///
        /// LA FORMA. Uno "stadio": due segmenti dritti lunghi L−W e due
        /// semicerchi di raggio W/2 alle estremita'. L e' la lunghezza TOTALE
        /// da fuori a fuori — come la si misura sul pezzo e come la si scrive
        /// a disegno — e W e' la larghezza, cioe' il diametro delle
        /// estremita'.
        ///
        /// FIRME VERIFICATE per riflessione sull'interop 29.5.1.1:
        ///   ISketchManager.CreateLine(Double X1, Double Y1, Double Z1,
        ///       Double X2, Double Y2, Double Z2) -> SketchSegment
        ///   ISketchManager.CreateArc(Double XC, Double YC, Double Zc,
        ///       Double X1, Double Y1, Double Z1, Double X2, Double Y2,
        ///       Double Z2, Int16 Direction) -> SketchSegment
        ///
        /// IL VERSO DELL'ARCO E' IL DUBBIO, E NON SI SPERA: SI MISURA.
        /// `Direction` dice da che parte girare dal primo punto al secondo, e
        /// vive nel piano dello SCHIZZO — mentre i sei punti qui sotto sono
        /// costruiti nel sistema del MODELLO e ci arrivano attraverso
        /// `PuntoNelloSchizzo`. Quella trasformazione non conserva la mano: il
        /// suo stesso commento dice che «per due facce opposte l'asse Y
        /// risulta invertito». Quindi lo stesso `-1` significa una cosa su una
        /// faccia e l'opposto su quella dirimpetto.
        ///
        /// COSA SUCCEDE SE E' SBAGLIATO — e qui il commento diceva il falso
        /// fino al 10 settembre 2026, quando una review lo ha rifatto con i
        /// numeri in mano. Non esce un arco da 270 gradi e non si autointerseca
        /// niente: i due estremi di ogni arco sono diametralmente opposti
        /// rispetto al centro, quindi TUTTI E DUE gli archi candidati sono da
        /// 180 gradi. Il verso decide solo da che parte passa il semicerchio.
        /// Con il verso giusto sporge in fuori e l'asola e' lunga L; con quello
        /// sbagliato rientra, e l'asola esce lunga L−W con le estremita' MORSE
        /// a mezzaluna. Per una M10 51x11: 40 mm invece di 51, area 344,97
        /// invece di 535,03. Il contorno resta chiuso e semplice, il taglio
        /// riesce, la feature e' verde, e la vite scorre 29 mm invece di 40.
        ///
        /// PERCIO' SI CONTA IL MATERIALE TOLTO. Il caso cieco lo prende per
        /// intero, perche' li' il confronto e' un'uguaglianza. Il caso passante
        /// e' piu' scomodo — quanto materiale ci sia sotto non si sa in
        /// generale — e li' si fanno due cose: un tetto contro le forme molto
        /// piu' grandi, e un controllo che cerca proprio QUESTO errore, che e'
        /// piu' piccolo e sotto il tetto ci passerebbe. E' la stessa lezione di
        /// `HoleWizard5`, che faceva un foro da due pollici senza dare nessun
        /// errore: l'unica difesa contro un pezzo che sembra giusto e' una
        /// misura, ma la misura va scelta in modo che possa fallire.
        /// </summary>
        private Esito Asola(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string uidFaccia = Str(a, "faccia_uid", "");
            var faccia = RisolviFaccia(stato, a, uidFaccia, out string motivo);
            if (faccia == null) return Fallito(motivo);

            double[] assi = AssiDellaFaccia(faccia, out double offsetM, out string errore);
            if (assi == null) return Fallito(errore);

            double lunghezza = Num(a, "lunghezza_mm");
            double larghezza = Num(a, "larghezza_mm");
            double angolo = Num(a, "angolo_gradi", 0);
            bool passante = Bool(a, "passante", true);
            double profondita = Num(a, "profondita_mm", 0);
            double x, y;
            if (!NumObbligatorio(a, "x_mm", out x, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "y_mm", out y, out errore)) return Fallito(errore);

            // LA LUNGHEZZA E' DA FUORI A FUORI. Un'asola lunga quanto e' larga
            // e' un foro tondo, e chiederla cosi' quasi sempre vuol dire che
            // uno dei due numeri e' sbagliato.
            if (lunghezza <= larghezza)
                return Fallito(
                    "L'asola e' lunga " + lunghezza.ToString("0.##") + " e larga " +
                    larghezza.ToString("0.##") + " mm: la lunghezza si misura da " +
                    "fuori a fuori e deve essere maggiore della larghezza. Se " +
                    "volevi un foro tondo da " + larghezza.ToString("0.##") +
                    " mm usa il foro, non l'asola.");
            if (!passante && profondita <= 0)
                return Fallito("Asola non passante senza profondita'.");

            // Lo stadio, nel sistema della faccia e prima di ruotarlo: mezza
            // corsa fra i due centri, e il raggio delle estremita'.
            double corsa = (lunghezza - larghezza) / 2.0;
            double raggio = larghezza / 2.0;
            double rad = angolo * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            Func<double, double, double[]> nelPiano = (u, v) => new[]
            {
                x + u * cos - v * sin,
                y + u * sin + v * cos,
            };

            // I COLMI: il punto piu' esterno di ogni semicerchio, sull'asse
            // lungo dell'asola. Sono cio' che determina l'arco senza doverne
            // dichiarare il verso.
            var colmoA = nelPiano(-corsa - raggio, 0);
            var colmoB = nelPiano(+corsa + raggio, 0);
            var altoA = nelPiano(-corsa, +raggio);
            var altoB = nelPiano(+corsa, +raggio);
            var bassoA = nelPiano(-corsa, -raggio);
            var bassoB = nelPiano(+corsa, -raggio);

            // Mezzo ingombro dello stadio sui due assi della faccia: la corsa
            // proiettata piu' il raggio delle estremita'. Con angolo 0 fa
            // lunghezza/2 e larghezza/2, come deve.
            string senzaSperanza = TaglioSenzaSperanza(
                modello, assi,
                "L'asola " + lunghezza.ToString("0.##") + " x " + larghezza.ToString("0.##") + " mm",
                x, y,
                Math.Abs(corsa * cos) + raggio, Math.Abs(corsa * sin) + raggio,
                (u, v) => DentroLoStadio(u, v, x, y, cos, sin, corsa, raggio),
                DaParteAParte(modello, assi, passante, profondita),
                (du, dv) => "Centrata li', ci sta dentro una sagoma fino a " +
                            (2 * du).ToString("0.##") + " x " + (2 * dv).ToString("0.##") + " mm.");
            if (senzaSperanza != null) return Fallito(senzaSperanza);

            if (!SelezionaFaccia(faccia)) return Fallito("Non sono riuscito a selezionare la faccia.");

            double volumePrima = VolumeMm3(modello);
            var schizzi = modello.SketchManager;
            schizzi.InsertSketch(true);

            double[] pAltoA = null, pAltoB = null, pBassoA = null, pBassoB = null;
            double[] pColmoA = null, pColmoB = null;
            bool disegnato = false;
            int segmenti = 0;
            try
            {
                pAltoA = PuntoNelloSchizzo(modello, assi, offsetM, altoA[0], altoA[1], out errore);
                pAltoB = pAltoA == null ? null
                    : PuntoNelloSchizzo(modello, assi, offsetM, altoB[0], altoB[1], out errore);
                pBassoA = pAltoB == null ? null
                    : PuntoNelloSchizzo(modello, assi, offsetM, bassoA[0], bassoA[1], out errore);
                pBassoB = pBassoA == null ? null
                    : PuntoNelloSchizzo(modello, assi, offsetM, bassoB[0], bassoB[1], out errore);
                pColmoA = pBassoB == null ? null
                    : PuntoNelloSchizzo(modello, assi, offsetM, colmoA[0], colmoA[1], out errore);
                pColmoB = pColmoA == null ? null
                    : PuntoNelloSchizzo(modello, assi, offsetM, colmoB[0], colmoB[1], out errore);

                if (pColmoB != null)
                {
                    // ADD-TO-DB, COME PER «estrudi_profilo», E QUI NON E' PER
                    // ANDARE PIU' VELOCI. Senza, SolidWorks inferisce una
                    // relazione a ogni segmento e SPOSTA i punti per farla
                    // tornare. I quattro pezzi dello stadio si toccano per
                    // costruzione — l'estremo della linea e' lo STESSO vettore
                    // che fa da capo all'arco — ma se ognuno viene agganciato
                    // per conto suo, il contorno si apre di frazioni di
                    // millimetro. Un contorno aperto non si taglia, e infatti
                    // il 10 settembre 2026 dodici prove su dodici finivano con
                    // «l'asola non e' stata tagliata».
                    disegnato = DisegnaLoStadio(
                        schizzi, pAltoA, pAltoB, pBassoA, pBassoB, pColmoA, pColmoB,
                        out segmenti, out errore);
                }
            }
            finally
            {
                // Il `catch` vuoto non e' pigrizia: se uscire dallo schizzo
                // fallisse, l'eccezione sostituirebbe il motivo VERO del
                // fallimento — e chi legge si troverebbe l'errore sbagliato.
                try { schizzi.InsertSketch(false); } catch { }
            }
            if (!disegnato) return Fallito(errore + SenzaLoSchizzo(modello));

            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Taglia(modello, passante, profondita);
            if (feature == null)
            {
                // Con il contorno completo, «il problema e' altrove» era un
                // modo gentile di dire che non lo sapevamo. Adesso si guarda.
                string sotto = segmenti < 4
                    ? ""
                    : SottoIlProfilo(
                        faccia, uidFaccia, assi, offsetM,
                        CampioniStadio(x, y, cos, sin, corsa, raggio));
                return Fallito(
                    "L'asola non e' stata tagliata. Lo schizzo ha " + segmenti +
                    " segmenti sui quattro che servono (due linee e due archi): " +
                    (segmenti < 4
                        ? "il contorno non e' completo, e un contorno aperto non si taglia."
                        : "il contorno e' completo, quindi il problema non e' il disegno.") +
                    sotto + SenzaLoSchizzo(modello));
            }

            // ── E ADESSO SI CONTA ────────────────────────────────────────────
            // L'area dello stadio: il rettangolo centrale piu' i due
            // semicerchi, che insieme fanno un cerchio.
            double area = (lunghezza - larghezza) * larghezza + Math.PI * raggio * raggio;
            double tolto = volumePrima - VolumeMm3(modello);
            if (tolto <= 0)
                return Fallito(
                    "L'asola non ha tolto niente: il taglio e' riuscito ma il " +
                    "materiale e' rimasto tutto. Di solito vuol dire che il " +
                    "profilo era fuori dal pezzo." + RestaNelPezzo);

            if (!passante)
            {
                double atteso = area * profondita;
                if (Math.Abs(tolto - atteso) > Math.Max(1.0, atteso * 0.03))
                    return Fallito(
                        "L'asola e' stata tagliata ma ha tolto " + tolto.ToString("0.#") +
                        " mm3 invece di " + atteso.ToString("0.#") + ": la forma " +
                        "che e' uscita non e' quella chiesta. Non ti confermo un " +
                        "pezzo che non torna." + RestaNelPezzo);
            }
            else
            {
                // ── Passante: due controlli, e servono a cose diverse ────────
                //
                // Quanto materiale ci sia sotto non si sa in generale: un
                // taglio passante attraversa quello che trova. Per questo qui
                // non c'e' un'uguaglianza come nel caso cieco.
                double spessoreMassimo = EstensioneLungoLAsse(modello, (int)assi[2]);

                // 1. IL TETTO. Il pezzo non puo' aver perso piu' materiale di
                //    quanto ne stia nell'ingombro attraversato. Prende le forme
                //    molto piu' grandi del dovuto.
                double limite = area * spessoreMassimo * 1.02;
                if (spessoreMassimo > 0 && tolto > limite)
                    return Fallito(
                        "L'asola ha tolto " + tolto.ToString("0.#") + " mm3, piu' " +
                        "di quanto ne possa togliere un'asola di quelle misure (" +
                        limite.ToString("0.#") + " al massimo): la forma che e' " +
                        "uscita non e' quella chiesta." + RestaNelPezzo);

                // 2. IL CONTROLLO CHE CERCA L'ERRORE CHE CONOSCIAMO.
                //
                //    UN TETTO DA SOLO NON BASTA, E IL MOTIVO E' PRECISO: se il
                //    verso degli archi e' sbagliato i semicerchi RIENTRANO
                //    invece di sporgere, e l'asola toglie MENO materiale del
                //    dovuto — quindi passa sotto il tetto e non viene mai
                //    segnalata. Il tetto e' cieco proprio sull'unico errore per
                //    cui l'operazione non e' ancora dichiarata.
                //
                //    L'area sbagliata pero' si sa in anticipo: e' il rettangolo
                //    MENO il cerchio invece che piu'. Se il materiale tolto
                //    somiglia a quella, non e' una coincidenza.
                double areaSbagliata = (lunghezza - larghezza) * larghezza
                                       - Math.PI * raggio * raggio;
                if (spessoreMassimo > 0 && areaSbagliata > 0)
                {
                    double sospetto = areaSbagliata * spessoreMassimo;
                    if (Math.Abs(tolto - sospetto) <= Math.Max(1.0, sospetto * 0.03))
                        return Fallito(
                            "L'asola ha tolto " + tolto.ToString("0.#") + " mm3, che e' " +
                            "esattamente quanto ne toglie un'asola con i semicerchi " +
                            "girati in DENTRO invece che in fuori: sarebbe lunga " +
                            (lunghezza - larghezza).ToString("0.##") + " mm invece di " +
                            lunghezza.ToString("0.##") + ", con le estremita' morse a " +
                            "mezzaluna. Non ti confermo un pezzo cosi'." + RestaNelPezzo);
                }
            }

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["lunghezza_mm"] = lunghezza,
                ["larghezza_mm"] = larghezza,
                ["angolo_gradi"] = angolo,
                ["passante"] = passante,
                ["area_mm2"] = Math.Round(area, 2),
                ["materiale_tolto_mm3"] = Math.Round(tolto, 2),
            }, NormaleDellaFaccia(faccia));
        }

        /// <summary>
        /// Quanto e' spesso il pezzo lungo un asse del modello, in mm. Serve
        /// come limite superiore per un taglio passante: non e' una misura
        /// fine, e' un tetto.
        /// </summary>
        private static double EstensioneLungoLAsse(IModelDoc2 modello, int asse)
        {
            var parte = modello as IPartDoc;
            var corpi = parte?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null || corpi.Length == 0) return 0;
            double basso = double.MaxValue, alto = double.MinValue;
            foreach (var c in corpi)
            {
                var b = (c as IBody2)?.GetBodyBox() as double[];
                if (b == null || b.Length < 6) continue;
                basso = Math.Min(basso, b[asse]);
                alto = Math.Max(alto, b[asse + 3]);
            }
            if (alto <= basso) return 0;
            return Unita.Mm(alto - basso);
        }

        /// <summary>
        /// SPOSTA (o COPIA) DEI CORPI. Il muro contro cui il banco ha sbattuto.
        ///
        /// PERCHE' ESISTE. Il 9 settembre 2026, alla richiesta «fammi un
        /// castello: due telai 1000x800 distanti 600 mm, uniti da quattro
        /// montanti», il modello ha scritto da solo la diagnosi giusta:
        ///
        ///   «non posso sovrapporre lo stesso telaio due volte. Devo spostare
        ///    il secondo telaio di 600 mm lungo l'asse Z»
        ///
        /// e poi ha proposto lo stesso `telaio_rettangolare` due volte, che il
        /// validatore ha rifiutato. Non era il modello a sbagliare: non c'era
        /// nessuna operazione capace di spostare cio' che aveva gia' fatto.
        /// Con `copia = true` il castello si fa in un passo — il secondo telaio
        /// e' una COPIA del primo, spostata di 600.
        ///
        /// FIRME VERIFICATE per riflessione sull'interop 29.5.1.1:
        ///   IFeatureManager.InsertMoveCopyBody2(
        ///       Double TransX, Double TransY, Double TransZ, Double TransDist,
        ///       Double RotPointX, Double RotPointY, Double RotPointZ,
        ///       Double RotAngleX, Double RotAngleY, Double RotAngleZ,
        ///       Boolean BCopy, Int32 NumCopies) -> Feature
        ///   ISelectionMgr.CreateSelectData() -> SelectData   (con .Mark)
        ///   IMoveCopyBodyFeatureData ha TransformX/Y/Z E TransformValue,
        ///       cioe' DIREZIONE e MODULO tenuti separati.
        ///
        /// IL DUBBIO, ED E' GROSSO. Siccome direzione e modulo sono due campi
        /// distinti, non e' detto quale dei due comandi: se SolidWorks
        /// prendesse TransX/Y/Z come spostamento assoluto ignorando TransDist,
        /// oppure moltiplicasse i due, il pezzo si sposterebbe della distanza
        /// SBAGLIATA — e nessuno darebbe errore. Qui si passa una direzione
        /// UNITARIA e la distanza in TransDist, che e' la lettura piu'
        /// probabile; e poi si misura.
        ///
        /// COME SI MISURA UNO SPOSTAMENTO. Con il baricentro. Muovere un
        /// sottoinsieme di corpi sposta il baricentro di TUTTO il pezzo di una
        /// frazione nota — quella del volume mosso sul volume totale:
        ///
        ///     centro_dopo = centro_prima + (volume_mosso / volume_totale) * d
        ///
        /// E' un'uguaglianza esatta, non una stima, e cade subito se la
        /// distanza e' sbagliata anche di poco. `IBody2.GetMassProperties`
        /// da' baricentro e volume insieme: i primi tre numeri sono il centro
        /// di massa in metri, il quarto e' il volume.
        /// </summary>
        private Esito SpostaCorpi(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            int quanti;
            string motivo;
            var corpi = CorpiDalRiferimento(a, "corpi_rif", stato, out quanti, out motivo);
            if (corpi == null) return Fallito(motivo);

            double dx = Num(a, "dx_mm", 0);
            double dy = Num(a, "dy_mm", 0);
            double dz = Num(a, "dz_mm", 0);
            bool copia = Bool(a, "copia", false);
            int copie = (int)Num(a, "quante_copie", 1);
            if (!copia) copie = 0;
            else if (copie < 1) copie = 1;

            double distanza = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distanza < 1e-6)
                return Fallito(
                    "Spostamento nullo: dx, dy e dz sono tutti a zero. Se " +
                    "volevi lasciare i corpi dove sono, questa operazione non " +
                    "serve; se volevi copiarli sul posto, due corpi sovrapposti " +
                    "sono un pezzo sbagliato, non una copia.");

            // Lo stato di partenza: tutto il pezzo, e i soli corpi da muovere.
            double volumeTotale, volumeMosso;
            double[] centroTotale, centroMosso;
            if (!MassaDi(TuttiICorpiSolidi(modello), out volumeTotale, out centroTotale))
                return Fallito("Non riesco a misurare il pezzo prima di spostarlo.");
            if (!MassaDi(corpi, out volumeMosso, out centroMosso))
                return Fallito("Non riesco a misurare i corpi da spostare.");
            if (volumeTotale <= 0 || volumeMosso <= 0)
                return Fallito("I corpi indicati non hanno volume: non c'e' niente da spostare.");
            int corpiPrima = ContaCorpiSolidi(modello);

            // I corpi da muovere vanno selezionati con MARCA 1: e' la marca che
            // la feature legge. Con la marca a zero la selezione c'e' ma la
            // feature non la vede, e non lo dice.
            modello.ClearSelection2(true);
            var gestore = modello.SelectionManager as ISelectionMgr;
            var marca = gestore?.CreateSelectData();
            if (marca == null) return Fallito("SolidWorks non mi da' come marcare la selezione.");
            marca.Mark = 1;
            // UN CORPO NON E' UNA IEntity, E LA DIFFERENZA NON SI VEDE FINCHE'
            // NON SI PROVA. Misurato il 10 settembre 2026 dentro SolidWorks:
            // `corpo as IEntity` torna null nell'interop, il contatore restava a
            // zero e tutte e cinque le prove di spostamento dicevano «i corpi
            // indicati non si sono lasciati selezionare». Le facce e gli spigoli
            // sono IEntity; i corpi hanno il loro `IBody2.Select2`.
            int selezionati = 0;
            foreach (var c in corpi)
            {
                var corpo = c as IBody2;
                if (corpo != null && corpo.Select2(true, marca)) { selezionati++; continue; }
                var ent = c as IEntity;
                if (ent != null && ent.Select4(true, marca)) selezionati++;
            }
            if (selezionati == 0)
                return Fallito("I corpi indicati non si sono lasciati selezionare.");

            IFeature feature;
            try
            {
                // MISURATO DENTRO SOLIDWORKS IL 10 SETTEMBRE 2026, E IL DUBBIO
                // E' SCIOLTO: comandano TransX/Y/Z, che sono lo spostamento
                // ASSOLUTO in metri; TransDist viene IGNORATO.
                //
                // Prima qui passavo un versore unitario piu' la distanza in
                // TransDist — la lettura che sembrava piu' probabile. Il
                // risultato: il versore (1,0,0 o simili) veniva preso per
                // metri, e QUALUNQUE spostamento chiesto diventava 1000 mm.
                // Chiedendo 50 mm il pezzo ne faceva mille, senza nessun
                // errore. L'ha detto il baricentro: scarto di 475 mm su un
                // atteso di 25. Era esattamente la misura scritta per poter
                // fallire, e ha fallito nel punto giusto.
                //
                // TransDist resta valorizzato con la distanza vera: se una
                // versione futura decidesse di leggerlo, leggerebbe il numero
                // giusto invece di uno zero.
                feature = modello.FeatureManager.InsertMoveCopyBody2(
                    TransX: Unita.M(dx),
                    TransY: Unita.M(dy),
                    TransZ: Unita.M(dz),
                    TransDist: Unita.M(distanza),
                    RotPointX: 0, RotPointY: 0, RotPointZ: 0,
                    RotAngleX: 0, RotAngleY: 0, RotAngleZ: 0,
                    BCopy: copia,
                    NumCopies: copie);
            }
            catch (Exception ex)
            {
                return Fallito("Lo spostamento non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito("Lo spostamento non e' stato creato.");

            // ── E ADESSO SI MISURA ───────────────────────────────────────────
            int corpiDopo = ContaCorpiSolidi(modello);
            int corpiAttesi = corpiPrima + copie * selezionati;
            if (corpiDopo != corpiAttesi)
                return Fallito(
                    "Dopo lo spostamento il pezzo ha " + corpiDopo + " corpi invece di " +
                    corpiAttesi + ". Non ti confermo un pezzo diverso da quello " +
                    "che ti ho mostrato: ritiralo — " + Frasi.CosiSiRitira + ".");

            double volumeDopo;
            double[] centroDopo;
            if (!MassaDi(TuttiICorpiSolidi(modello), out volumeDopo, out centroDopo))
                return Fallito("Non riesco a rimisurare il pezzo dopo lo spostamento.");

            double volumeAtteso = volumeTotale + copie * volumeMosso;
            if (Math.Abs(volumeDopo - volumeAtteso) > Math.Max(1.0, volumeAtteso * 0.005))
                return Fallito(
                    "Dopo lo spostamento il pezzo ha un volume di " +
                    volumeDopo.ToString("0.#") + " mm3 invece di " +
                    volumeAtteso.ToString("0.#") + ": qualcosa si e' unito o " +
                    "tagliato. Ritiralo — " + Frasi.CosiSiRitira + ".");

            // Il baricentro atteso. Senza copia si sposta la frazione di volume
            // mossa; con le copie, la copia k-esima sta a k*d, e la somma dei k
            // da 1 a n fa n(n+1)/2.
            var d = new[] { dx, dy, dz };
            var atteso = new double[3];
            if (copie == 0)
            {
                for (int i = 0; i < 3; i++)
                    atteso[i] = centroTotale[i] + (volumeMosso / volumeTotale) * d[i];
            }
            else
            {
                double sommaK = copie * (copie + 1) / 2.0;
                for (int i = 0; i < 3; i++)
                    atteso[i] =
                        (volumeTotale * centroTotale[i]
                         + volumeMosso * (copie * centroMosso[i] + sommaK * d[i]))
                        / volumeAtteso;
            }

            double scarto = 0;
            for (int i = 0; i < 3; i++) scarto = Math.Max(scarto, Math.Abs(centroDopo[i] - atteso[i]));
            double tolleranza = Math.Max(0.05, distanza * 0.002);
            if (scarto > tolleranza)
                return Fallito(
                    "I corpi non sono finiti dove dovevano: il baricentro del " +
                    "pezzo e' fuori di " + scarto.ToString("0.###") + " mm da dove " +
                    "lo mette uno spostamento di " + distanza.ToString("0.##") +
                    " mm. Quasi sempre vuol dire che la distanza applicata non e' " +
                    "quella chiesta. Ritiralo — " + Frasi.CosiSiRitira + ".");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["corpi_mossi"] = selezionati,
                ["dx_mm"] = dx,
                ["dy_mm"] = dy,
                ["dz_mm"] = dz,
                ["distanza_mm"] = Math.Round(distanza, 3),
                ["copia"] = copia,
                ["quante_copie"] = copie,
                ["corpi_dopo"] = corpiDopo,
                ["scarto_baricentro_mm"] = Math.Round(scarto, 4),
            }, null);
        }

        /// <summary>
        /// I quattro segmenti dello stadio, con l'inferenza automatica spenta.
        ///
        /// PERCHE' SPEGNERLA. I quattro pezzi si toccano per costruzione: il
        /// capo di ogni linea e' lo STESSO vettore che fa da capo all'arco, non
        /// un numero ricalcolato. Ma se SolidWorks aggancia ogni segmento per
        /// conto suo — che e' quello che fa quando `AddToDB` e' falso — li
        /// sposta per far tornare le relazioni che inferisce, e il contorno si
        /// apre di frazioni di millimetro. Un contorno aperto non si taglia.
        ///
        /// Torna quanti segmenti sono nati davvero: e' il numero che il
        /// messaggio di errore usa per dire SE il contorno era completo, invece
        /// di lasciare indovinare.
        /// </summary>
        private static bool DisegnaLoStadio(
            ISketchManager schizzi,
            double[] altoA, double[] altoB, double[] bassoA, double[] bassoB,
            double[] colmoA, double[] colmoB,
            out int segmenti, out string errore)
        {
            segmenti = 0;
            errore = null;
            bool addToDbPrima = false, displayPrima = false;
            try { addToDbPrima = schizzi.AddToDB; displayPrima = schizzi.DisplayWhenAdded; } catch { }
            try
            {
                try { schizzi.AddToDB = true; schizzi.DisplayWhenAdded = false; } catch { }

                if (schizzi.CreateLine(altoA[0], altoA[1], altoA[2],
                                       altoB[0], altoB[1], altoB[2]) != null) segmenti++;
                if (schizzi.CreateLine(bassoB[0], bassoB[1], bassoB[2],
                                       bassoA[0], bassoA[1], bassoA[2]) != null) segmenti++;

                // GLI ARCHI SI DANNO PER TRE PUNTI, NON PER VERSO.
                //
                // Prima erano `CreateArc(centro, capo1, capo2, Direction)` con
                // `Direction = -1` scritto a mano. Un verso pero' vive nel
                // piano dello SCHIZZO, mentre i punti sono costruiti nel
                // sistema del MODELLO, e la trasformazione fra i due non
                // conserva la mano. Misurato il 10 settembre 2026 su una
                // piastra: funzionava su «davanti», «basso» e «destra» e si
                // ribaltava su «dietro», «alto» e «sinistra» — una faccia per
                // ogni coppia, cioe' meta' del parco facce.
                //
                // Ribaltato, il semicerchio RIENTRA invece di sporgere:
                // l'asola esce lunga L−W con le estremita' morse. Sulla prova
                // cieca il conto era 1724,8 mm3 invece di 2675,2, cioe'
                // esattamente (L−W)·W − pi·r^2 al posto di + pi·r^2.
                //
                // `Create3PointArc(capo1, capo2, puntoSull'arco)` toglie di
                // mezzo la domanda: il terzo punto e' il colmo del semicerchio,
                // passa per `PuntoNelloSchizzo` come tutti gli altri, e
                // l'arco resta determinato dalla GEOMETRIA invece che da una
                // convenzione. Non c'e' piu' niente da indovinare.
                if (schizzi.Create3PointArc(altoB[0], altoB[1], altoB[2],
                                            bassoB[0], bassoB[1], bassoB[2],
                                            colmoB[0], colmoB[1], colmoB[2]) != null) segmenti++;
                if (schizzi.Create3PointArc(bassoA[0], bassoA[1], bassoA[2],
                                            altoA[0], altoA[1], altoA[2],
                                            colmoA[0], colmoA[1], colmoA[2]) != null) segmenti++;
            }
            catch (Exception ex)
            {
                errore = "Lo schizzo dell'asola non e' riuscito: " + ex.Message;
                return false;
            }
            finally
            {
                try { schizzi.AddToDB = addToDbPrima; schizzi.DisplayWhenAdded = displayPrima; } catch { }
            }

            if (segmenti < 4)
            {
                errore =
                    "Lo schizzo dell'asola ha solo " + segmenti + " segmenti sui " +
                    "quattro che servono: SolidWorks non ha costruito " +
                    (segmenti < 2 ? "le linee" : "gli archi") + ".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Quante pareti ha attraversato un taglio cilindrico: si contano le
        /// facce cilindriche del diametro chiesto che la feature ha lasciato.
        ///
        /// Una parete forata lascia una faccia cilindrica. Due pareti, due
        /// facce. E' un conteggio, non una stima, e non dipende da quanto e'
        /// spesso il pezzo — che in generale non si sa.
        ///
        /// La ricetta per riconoscere una faccia cilindrica di un certo
        /// diametro e' quella gia' in produzione in `OperazioniFori`:
        /// `IFace2.IGetSurface().IsCylinder()` e `CylinderParams[6] * 2000`.
        /// </summary>
        private static int ParetiForate(IFeature feature, double diametroMm)
        {
            try
            {
                var facce = feature.GetFaces() as object[];
                if (facce == null) return 0;
                int quante = 0;
                foreach (var o in facce)
                {
                    var superficie = (o as IFace2)?.IGetSurface();
                    if (superficie == null || !superficie.IsCylinder()) continue;
                    var par = superficie.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    if (Math.Abs(par[6] * 2000.0 - diametroMm) > 0.05) continue;
                    quante++;
                }
                return quante;
            }
            catch { return 0; }
        }

        /// <summary>Tutti i corpi solidi del pezzo, come li vuole MassaDi.</summary>
        private static object[] TuttiICorpiSolidi(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return new object[0];
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            return corpi ?? new object[0];
        }

        /// <summary>
        /// Volume in mm3 e baricentro in mm di un gruppo di corpi.
        ///
        /// `IBody2.GetMassProperties(densita)` torna un vettore in cui i primi
        /// tre numeri sono il centro di massa in METRI e il quarto e' il volume
        /// in metri cubi. Il baricentro di piu' corpi e' la media dei loro
        /// baricentri PESATA SUL VOLUME: farne la media semplice darebbe un
        /// numero plausibile e sbagliato, e nessuno se ne accorgerebbe.
        /// </summary>
        private static bool MassaDi(object[] corpi, out double volumeMm3, out double[] centroMm)
        {
            volumeMm3 = 0;
            centroMm = new double[3];
            if (corpi == null || corpi.Length == 0) return false;
            var somma = new double[3];
            try
            {
                foreach (var c in corpi)
                {
                    var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                    if (mp == null || mp.Length < 4) continue;
                    double v = mp[3] * 1e9;
                    if (v <= 0) continue;
                    volumeMm3 += v;
                    for (int i = 0; i < 3; i++) somma[i] += Unita.Mm(mp[i]) * v;
                }
            }
            catch { return false; }
            if (volumeMm3 <= 0) return false;
            for (int i = 0; i < 3; i++) centroMm[i] = somma[i] / volumeMm3;
            return true;
        }

        /// <summary>
        /// ESTRUDE UN PROFILO SU UNA FACCIA CHE C'E' GIA'.
        ///
        /// PERCHE' MANCAVA, E PERCHE' E' IL BUCO PIU' GROSSO DEL CATALOGO.
        /// Misurato il 10 settembre 2026, elencando le precondizioni di tutte
        /// e ventisette le operazioni: su un pezzo normale — non una
        /// carpenteria — si poteva soltanto TOGLIERE. Taglio cilindrico,
        /// taglio rettangolare, asola, foro guidato: tolgono. Guscio: svuota.
        /// Specchia e sposta: muovono. Nessuna aggiungeva niente.
        ///
        /// Cioe': fai un corpo, e da li' in poi puoi solo scavarlo.
        ///
        /// E' la spiegazione migliore che abbiamo trovato della lamentela
        /// dell'officina — «una volta che disegna una cosa non riesce
        /// successivamente a modificarla». Senza questa operazione non si
        /// possono fare: un labbro di tenuta, un distanziale su cui appoggiare
        /// qualcosa, un'aletta di fissaggio, una torretta per una vite, un
        /// rialzo. Cioe' meta' di quello che c'e' dentro un contenitore.
        ///
        /// LA STRADA E' GIA' PROVATA, ED E' PER QUESTO CHE COSTA POCO.
        /// Disegnare uno schizzo su una faccia scelta e' esattamente cio' che
        /// fanno `taglio_rettangolare` e `asola`, misurate. Disegnare un
        /// contorno qualunque e' cio' che fa `estrudi_profilo`, misurato.
        /// Qui si mettono insieme i due pezzi e si chiama `FeatureExtrusion3`
        /// invece di `FeatureCut4`.
        ///
        /// IL DUBBIO: `Merge`. Con `unisci` vero il materiale nuovo diventa
        /// parte del corpo che c'era; con falso nasce un CORPO SEPARATO, che
        /// e' un pezzo diverso e non lo dice nessuno — l'albero cresce
        /// uguale, la forma sembra la stessa, e la differenza si vede solo
        /// contando i corpi. Percio' qui si contano.
        /// </summary>
        private Esito EstrudiSuFaccia(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var contorni = Contorni(a, out string motivo, out string _);
            if (contorni == null) return Fallito(motivo);

            var faccia = RisolviFaccia(stato, a, Str(a, "faccia_uid", ""), out motivo);
            if (faccia == null) return Fallito(motivo);

            double[] assi = AssiDellaFaccia(faccia, out double offsetM, out string errore);
            if (assi == null) return Fallito(errore);

            double altezza = Num(a, "altezza_mm");
            if (altezza <= 0)
                return Fallito("Un rialzo di altezza nulla o negativa non e' un rialzo.");
            bool unisci = Bool(a, "unisci", true);

            int corpiPrima = ContaCorpiSolidi(modello);
            double volumePrima = VolumeMm3(modello);

            if (!SelezionaFaccia(faccia)) return Fallito("Non sono riuscito a selezionare la faccia.");

            var schizzi = modello.SketchManager;
            bool addToDbPrima = false, displayPrima = false;
            try { addToDbPrima = schizzi.AddToDB; displayPrima = schizzi.DisplayWhenAdded; } catch { }

            int segmenti = 0;
            bool disegnato = false;
            try
            {
                schizzi.InsertSketch(true);
                try { schizzi.AddToDB = true; schizzi.DisplayWhenAdded = false; } catch { }
                // I contorni arrivano in coordinate della FACCIA, non del
                // piano: e' la stessa convenzione di `taglio_rettangolare`, e
                // il modello non deve sapere dove sta la faccia nello spazio.
                segmenti = DisegnaContorniSullaFaccia(
                    modello, schizzi, contorni, assi, offsetM, out errore);
                disegnato = segmenti > 0;
            }
            catch (Exception ex) { errore = ex.Message; }
            finally
            {
                try { schizzi.AddToDB = addToDbPrima; schizzi.DisplayWhenAdded = displayPrima; } catch { }
                try { schizzi.InsertSketch(false); } catch { }
            }
            if (!disegnato) return Fallito((errore ?? "schizzo non riuscito") + SenzaLoSchizzo(modello));

            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            var feature = Estrudi(modello, altezza, unisci: unisci);
            if (feature == null)
                return Fallito(
                    "Il rialzo non e' stato creato. Lo schizzo ha " + segmenti +
                    " segmenti: se il contorno non e' chiuso, l'estrusione non parte." +
                    SenzaLoSchizzo(modello));

            // ── e adesso si conta ────────────────────────────────────────
            int corpiDopo = ContaCorpiSolidi(modello);
            double aggiunto = VolumeMm3(modello) - volumePrima;

            if (unisci && corpiDopo != corpiPrima)
                return Fallito(
                    "Il rialzo doveva unirsi al pezzo e invece i corpi sono " +
                    "passati da " + corpiPrima + " a " + corpiDopo + ": quasi " +
                    "sempre vuol dire che il profilo non tocca il corpo. " +
                    "Ritiralo — " + Frasi.CosiSiRitira + ".");

            if (aggiunto <= 0)
                return Fallito(
                    "Il rialzo non ha aggiunto materiale: il volume e' " +
                    (aggiunto < 0 ? "calato" : "rimasto uguale") + ". Di solito " +
                    "vuol dire che l'estrusione e' andata DENTRO il pezzo " +
                    "invece che in fuori. Ritiralo — " + Frasi.CosiSiRitira + ".");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["altezza_mm"] = altezza,
                ["unisci"] = unisci,
                ["contorni"] = contorni.Count,
                ["segmenti"] = segmenti,
                ["corpi"] = corpiDopo,
                ["materiale_aggiunto_mm3"] = Math.Round(aggiunto, 2),
            }, NormaleDellaFaccia(faccia));
        }

        /// <summary>
        /// Disegna dei contorni sul piano di una FACCIA, non su un piano
        /// dell'albero: ogni punto passa per `PuntoNelloSchizzo`, che e' la
        /// stessa strada dei tagli su faccia gia' misurati.
        /// </summary>
        private int DisegnaContorniSullaFaccia(
            IModelDoc2 modello, ISketchManager schizzi, List<Contorno> contorni,
            double[] assi, double offsetM, out string errore)
        {
            errore = null;
            int fatti = 0;
            foreach (var contorno in contorni)
            {
                if (contorno.Cerchio)
                {
                    var c = PuntoNelloSchizzo(modello, assi, offsetM,
                                              contorno.Centro[0], contorno.Centro[1], out errore);
                    var bordo = PuntoNelloSchizzo(modello, assi, offsetM,
                                                  contorno.Centro[0] + contorno.Diametro / 2.0,
                                                  contorno.Centro[1], out errore);
                    if (c == null || bordo == null) return 0;
                    // CreateCircle vuole un punto sul bordo, non un raggio:
                    // cosi' il cerchio resta tondo anche se il piano dello
                    // schizzo e' girato rispetto al modello.
                    if (schizzi.CreateCircle(c[0], c[1], c[2], bordo[0], bordo[1], bordo[2]) != null)
                        fatti++;
                    continue;
                }
                foreach (var tratto in contorno.Tratti)
                {
                    for (int i = 0; i < tratto.Punti.Count - 1; i++)
                    {
                        var p1 = PuntoNelloSchizzo(modello, assi, offsetM,
                                                   tratto.Punti[i][0], tratto.Punti[i][1], out errore);
                        var p2 = PuntoNelloSchizzo(modello, assi, offsetM,
                                                   tratto.Punti[i + 1][0], tratto.Punti[i + 1][1], out errore);
                        if (p1 == null || p2 == null) return 0;
                        if (schizzi.CreateLine(p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]) != null)
                            fatti++;
                    }
                }
            }
            return fatti;
        }

        /// <summary>
        /// CAMBIA UNA QUOTA DI UNA LAVORAZIONE CHE C'E' GIA'.
        ///
        /// E' la richiesta piu' frequente in officina — «quel foro portalo da
        /// M8 a M10», «lo spessore fallo 3» — e fino al 10 settembre 2026 era
        /// impossibile: tutte e ventotto le operazioni aggiungevano o
        /// toglievano, nessuna MODIFICAVA. L'unico modo era cancellare e
        /// rifare, cioe' perdere tutto quello che veniva dopo: fori,
        /// raccordi, nervature costruite su quella geometria.
        ///
        /// Firme verificate per riflessione sull'interop 29.5.1.1:
        ///   IFeature.GetFirstDisplayDimension() / GetNextDisplayDimension(Object)
        ///   IDisplayDimension.GetDimension2(Int32 Index) -> Dimension
        ///   IDimension.SystemValue (in METRI) e
        ///   IDimension.SetSystemValue3(Double, Int32 WhichConfigurations, Object Config_names) -> Int32
        ///   IModelDoc2.EditRebuild3() -> Boolean
        ///
        /// LE CONFIGURAZIONI SONO UNA TRAPPOLA APERTA.
        /// `SetSystemValue3` vuole sapere SU QUALI configurazioni scrivere.
        /// Scegliere «questa» su un pezzo che ne ha tre vuol dire cambiarne
        /// una e lasciare le altre com'erano — e il pezzo sembra a posto
        /// finche' qualcuno non apre l'altra configurazione. Qui si scrive su
        /// TUTTE, che e' l'unica scelta che non lascia il pezzo per meta'.
        ///
        /// E SI RICOSTRUISCE, PERCHE' SENZA NON CAMBIA NIENTE.
        /// Cambiare il valore non muove la geometria: la muove la
        /// ricostruzione. Senza `EditRebuild3` il numero e' nuovo e il pezzo
        /// e' vecchio — che e' il modo perfetto di far confermare a qualcuno
        /// una modifica che non c'e'.
        /// </summary>
        private Esito ModificaQuota(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string nomeQuota = Str(a, "quota", "");
            if (string.IsNullOrWhiteSpace(nomeQuota))
                return Fallito(
                    "Non hai detto quale quota cambiare. I nomi delle quote " +
                    "stanno nello stato del documento, dentro ogni lavorazione " +
                    "(per esempio «D1@Schizzo1»).");

            double valore;
            string errore;
            if (!NumObbligatorio(a, "valore_mm", out valore, out errore)) return Fallito(errore);

            var quota = TrovaQuota(modello, nomeQuota, out string dove);
            if (quota == null)
                return Fallito(
                    "Non trovo la quota «" + nomeQuota + "» in questo documento." +
                    (dove == null ? "" : " Ci sono: " + dove + "."));

            double prima = Unita.Mm(quota.SystemValue);
            double volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);

            // swSetValueInConfiguration_e.swSetValue_InAllConfigurations = 2
            const int inTutteLeConfigurazioni = 2;
            int esito;
            var lavorazioniPrima = FaccePerLavorazione(modello);

            try
            {
                esito = quota.SetSystemValue3(Unita.M(valore), inTutteLeConfigurazioni, null);
            }
            catch (Exception ex)
            {
                return Fallito("La quota non si e' lasciata cambiare: " + ex.Message);
            }
            if (esito != 0)
                return Fallito(
                    "SolidWorks ha rifiutato il valore " + valore + " mm per «" +
                    nomeQuota + "» (codice " + esito + "). Di solito e' una " +
                    "quota governata da un'equazione o da una relazione.");

            bool ricostruito;
            try { ricostruito = modello.EditRebuild3(); }
            catch (Exception ex) { return Fallito("La ricostruzione e' fallita: " + ex.Message); }

            // ── e adesso si guarda se e' successo davvero ────────────────
            double dopo = Unita.Mm(quota.SystemValue);
            if (Math.Abs(dopo - valore) > 0.001)
                return Fallito(
                    "La quota «" + nomeQuota + "» dice ancora " +
                    dopo.ToString("0.###") + " mm invece di " + valore + ": il " +
                    "valore non ha preso. Il pezzo non e' cambiato.");

            double volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);

            // UNA MODIFICA CHE NON MUOVE NIENTE E' UN SOSPETTO, NON UN
            // SUCCESSO. Se la quota e' cambiata e il volume no, quasi sempre
            // vuol dire che quella quota non comanda la geometria che chi
            // chiede aveva in mente — e confermarla come «fatto» sarebbe una
            // bugia comoda.
            bool volumeFermo = Math.Abs(volumeDopo - volumePrima) < 0.01;
            if (!ricostruito)
                return Fallito(
                    "La quota e' cambiata ma la ricostruzione non e' riuscita: " +
                    "il pezzo e' in errore. Guardalo prima di andare avanti." +
                    RestaNelPezzo);

            if (corpiDopo != corpiPrima)
                return Fallito(
                    "Dopo la modifica il pezzo ha " + corpiDopo + " corpi invece " +
                    "di " + corpiPrima + ": la quota nuova ha spezzato o unito " +
                    "qualcosa." + RestaNelPezzo);

            var misure = new Dictionary<string, object>
            {
                ["quota"] = nomeQuota,
                ["prima_mm"] = Math.Round(prima, 3),
                ["adesso_mm"] = Math.Round(dopo, 3),
                ["volume_mm3"] = Math.Round(volumeDopo, 2),
                ["volume_cambiato_mm3"] = Math.Round(volumeDopo - volumePrima, 2),
            };

            // L'ALBERO PUO' ESSERE CAMBIATO, NON SOLO LE MISURE.
            //
            // Misurato: piastra 200x100x20 con una tasca cieca profonda 5,
            // portato lo spessore a 3. La tasca diventa PASSANTE — la
            // lavorazione di taglio passa da 2 facce a 1, perche' il fondo non
            // c'e' piu' — e nessuno lo dice: GetErrorCode2 risponde 0,
            // IsWarning e' falso, il volume torna, i corpi sono sempre uno.
            //
            // Una sede cieca e un passaggio sono due pezzi diversi. Chi ha
            // chiesto di assottigliare la piastra magari lo voleva, quindi non
            // si rifiuta; ma deve poterlo leggere qui invece di scoprirlo in
            // officina.
            var cambiate = LavorazioniCambiate(
                lavorazioniPrima, FaccePerLavorazione(modello), misure);

            misure["nota"] = volumeFermo
                ? "la quota e' cambiata ma il volume no: controlla che sia quella giusta"
                : cambiate;

            return Riuscito(null, misure);
        }

        /// <summary>
        /// Cerca una quota per nome in tutto l'albero.
        ///
        /// Il nome puo' essere quello pieno («D1@Schizzo1») o quello corto
        /// («D1»): il modello legge il pieno nello stato, ma una persona che
        /// scrive a mano usa il corto, e rifiutare per quello sarebbe pedante.
        /// Se non lo trova, elenca i nomi che ci sono: un «non trovato» senza
        /// l'elenco costringe a indovinare.
        /// </summary>
        private static IDimension TrovaQuota(IModelDoc2 modello, string nome, out string disponibili)
        {
            disponibili = null;
            var parte = modello as IPartDoc;
            if (parte == null) return null;
            // UNA VOLTA SOLA PER QUOTA. Uno schizzo assorbito compare due
            // volte nell'albero — come schizzo e dentro la lavorazione che
            // se l'e' preso — e la sua quota si incontra due volte.
            var nomi = new List<string>();
            var gia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IFeature f = parte.IFirstFeature();
            while (f != null)
            {
                object display = null;
                try { display = f.GetFirstDisplayDimension(); } catch { }
                while (display != null)
                {
                    IDimension d = null;
                    try { d = (display as IDisplayDimension)?.GetDimension2(0); } catch { }
                    if (d != null)
                    {
                        string pieno = null, corto = null;
                        try { pieno = d.FullName; corto = d.Name; } catch { }
                        if (!string.IsNullOrEmpty(pieno))
                        {
                            if (nomi.Count < 12 && gia.Add(pieno)) nomi.Add(pieno);

                            // TRE FORME, E TUTTE E TRE DEVONO ANDARE.
                            //
                            //   D1                          il nome corto
                            //   D1@Schizzo1                 nome e lavorazione
                            //   D1@Schizzo1@Parte286.Part   come lo scrive lo stato
                            //
                            // La seconda mancava, e il messaggio d'errore la
                            // dava per buona («per esempio «D1@Schizzo1»»):
                            // il messaggio diceva il falso.
                            //
                            // E non e' un dettaglio di comodita': la forma con
                            // il documento in coda contiene il TITOLO del
                            // documento, quindi un caso di banco scritto con
                            // quella non si puo' rilanciare su un altro pezzo.
                            // Senza la forma di mezzo, le prove che cambiano
                            // una quota non sono ripetibili.
                            string senzaDocumento = pieno;
                            int ultimaChiocciola = pieno.LastIndexOf('@');
                            if (ultimaChiocciola > 0)
                                senzaDocumento = pieno.Substring(0, ultimaChiocciola);

                            if (string.Equals(pieno, nome, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(corto, nome, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(senzaDocumento, nome, StringComparison.OrdinalIgnoreCase))
                            {
                                return d;
                            }
                        }
                    }
                    object prossimo = null;
                    try { prossimo = f.GetNextDisplayDimension(display); } catch { }
                    display = prossimo;
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { break; }
            }
            if (nomi.Count > 0) disponibili = string.Join(", ", nomi);
            return null;
        }

        private Esito RaccordoSpigoli(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double raggio = Num(a, "raggio_mm");
            int quanti;
            string perche;
            if (!SelezionaSpigoli(modello, a, stato, raggio, out quanti, out perche))
                return Fallito(perche);
            return EseguiRaccordo(modello, raggio, quanti);
        }

        /// <summary>
        /// Lo smusso: il gemello del raccordo, e la ragione per cui la
        /// selezione degli spigoli e' stata estratta in un metodo solo.
        ///
        /// In carpenteria lo smusso e' una voce di capitolato: si smussa per
        /// togliere la bava, per far entrare un pezzo, per preparare un
        /// cianfrino da saldare. Fino alla 0.30.2 l'assistente sapeva
        /// arrotondare e non sapeva smussare, e chi lo chiedeva si sentiva
        /// rispondere di no.
        ///
        /// FIRMA VERIFICATA per riflessione sull'interop 29.5.1.1:
        ///   IFeatureManager.InsertFeatureChamfer(Int32 Options, Int32
        ///       ChamferType, Double Width, Double Angle, Double OtherDist,
        ///       Double VertexChamDist1, Double VertexChamDist2,
        ///       Double VertexChamDist3) -> Feature
        ///   swChamferType_e: AngleDistance=1, DistanceDistance=2, Vertex=3,
        ///       EqualDistance=16
        ///   swFeatureChamferOption_e: FlipDirection=1, KeepFeature=2,
        ///       TangentPropagation=4, PropagateFeatToParts=8
        ///
        /// ATTENZIONE, ed e' scritto nel RAG fra «le sette API che sembravano
        /// giuste»: gli enum dello smusso sono DUE e hanno gli stessi numeri
        /// con significati diversi — `swSketchChamferType_e` vale per lo
        /// schizzo, `swChamferType_e` per la feature. Qui serve il secondo.
        /// </summary>
        private Esito SmussoSpigoli(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double lato = Num(a, "lato_mm");
            double angolo = Num(a, "angolo_gradi", 45);
            double secondoLato = Num(a, "secondo_lato_mm", 0);
            bool dueLati = secondoLato > 0;

            int quanti;
            string perche;
            // Il limite «meta' dello spigolo» vale anche per lo smusso: il lato
            // piu' lungo dei due e' quello che non ci puo' stare.
            if (!SelezionaSpigoli(modello, a, stato,
                                  Math.Max(lato, secondoLato), out quanti, out perche))
                return Fallito(perche);

            int tipo = dueLati
                ? (int)swChamferType_e.swChamferDistanceDistance
                : (int)swChamferType_e.swChamferAngleDistance;

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertFeatureChamfer(
                    Options: (int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation,
                    ChamferType: tipo,
                    Width: Unita.M(lato),
                    Angle: Unita.Rad(angolo),
                    OtherDist: dueLati ? Unita.M(secondoLato) : 0,
                    VertexChamDist1: 0,
                    VertexChamDist2: 0,
                    VertexChamDist3: 0) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Lo smusso non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "Lo smusso non e' stato creato. Di solito e' la misura: un " +
                    "lato piu' grande del materiale che c'e' li' intorno non si " +
                    "puo' togliere.");

            var misure = new Dictionary<string, object>
            {
                ["lato_mm"] = lato,
                ["spigoli"] = quanti,
            };
            if (dueLati) misure["secondo_lato_mm"] = secondoLato;
            else misure["angolo_gradi"] = angolo;
            return Riuscito(feature, misure);
        }

        /// <summary>
        /// Un piano di riferimento nuovo, parallelo a uno dei tre piani base.
        ///
        /// SOLO PIANI BASE, E IL MOTIVO STA NEL VALIDATORE DEL SERVER.
        /// `valida()` impone SEMPRE uno fra `faccia_uid` e `faccia_rif` appena
        /// quella proprieta' compare nello schema di un'operazione. Se qui si
        /// dichiarasse anche una faccia come riferimento alternativo, «piano
        /// superiore, 600 mm» da solo non sarebbe piu' accettato: servirebbe
        /// comunque un identificativo di faccia. La versione 1 fa il caso che
        /// il committente ha chiesto per nome.
        ///
        /// FIRME VERIFICATE sull'interop 29.5.1.1:
        ///   IFeatureManager.InsertRefPlane(Int32 FirstConstraint, Double
        ///       FirstConstraintAngleOrDistance, Int32 SecondConstraint,
        ///       Double SecondConstraintAngleOrDistance, Int32
        ///       ThirdConstraint, Double ThirdConstraintAngleOrDistance)
        ///   IFeature.SelectByMark(Boolean AppendFlag, Int32 Mark)
        ///   swRefPlaneReferenceConstraints_e: Distance = 8, OptionFlip = 256
        ///
        /// IL SEGNO DELLA DISTANZA NON E' VERIFICATO DENTRO SOLIDWORKS: si
        /// passa il valore assoluto e si aggiunge OptionFlip quando la
        /// distanza e' negativa. E' la lettura piu' letterale dell'enum, ma
        /// circolano macro che passano il segno direttamente. Da misurare:
        /// piano superiore, distanza -100, il piano nuovo deve finire SOTTO.
        /// </summary>
        private Esito CreaPiano(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string piano = Str(a, "piano", "frontale");
            double distanza;
            string errore;
            if (!NumObbligatorio(a, "distanza_mm", out distanza, out errore))
                return Fallito(errore);

            modello.ClearSelection2(true);
            IFeature riferimento = TrovaPianoBase(modello, piano);
            if (riferimento == null)
                return Fallito($"Non ho trovato il piano «{piano}» nell'albero.");
            // SELEZIONARE LA FEATURE NON E' SELEZIONARE IL PIANO.
            // `IFeature.Select2` mette in selezione la voce dell'albero;
            // `InsertRefPlane` vuole un riferimento di tipo PIANO. Con la
            // feature selezionata la selezione c'e' — misurato: «selezionati
            // 1, marca 1, piano Piano superiore» — e la chiamata torna null
            // lo stesso, senza dire niente.
            //
            // Il nome NON e' scritto a mano: si legge dalla feature che
            // abbiamo gia' in mano, cosi' la lingua dell'installazione non
            // c'entra (in italiano si chiama «Piano superiore»).
            string nomePiano = null;
            try { nomePiano = riferimento.Name; } catch { }
            bool selezionato = false;
            if (!string.IsNullOrEmpty(nomePiano))
            {
                try
                {
                    selezionato = modello.Extension.SelectByID2(
                        nomePiano, "PLANE", 0, 0, 0, false, 1, null, 0);
                }
                catch { }
            }
            if (!selezionato && !riferimento.Select2(false, 1))
                return Fallito($"Non sono riuscito a selezionare il piano «{piano}».");

            // COSA HA DAVVERO IN MANO SOLIDWORKS QUANDO CHIAMIAMO.
            // `InsertRefPlane` non lavora sui parametri: lavora su cio' che e'
            // selezionato CON LA MARCA GIUSTA, e se la marca non e' quella
            // torna null senza dire niente. Queste tre righe non costano nulla
            // e trasformano un «non e' stato creato» in una diagnosi.
            string comEra = "";
            try
            {
                var sel = modello.SelectionManager as ISelectionMgr;
                int quanti = sel == null ? -1 : sel.GetSelectedObjectCount2(-1);
                int marca = (sel != null && quanti > 0) ? sel.GetSelectedObjectMark(1) : -1;
                string nome = "";
                try { nome = riferimento.Name; } catch { }
                int tipo = (sel != null && quanti > 0) ? sel.GetSelectedObjectType3(1, -1) : -1;
                comEra = " [selezionati " + quanti + ", marca " + marca +
                         ", tipo " + tipo + ", piano «" + nome + "»]";
            }
            catch { }

            int vincolo = (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance;
            if (distanza < 0)
                vincolo |= (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip;

            // QUALE VALORE DI VINCOLO VUOLE DAVVERO. La firma e' giusta, la
            // selezione e' giusta (misurato: un oggetto, marca 1, tipo 4 =
            // swSelDATUMPLANES), e la chiamata torna null lo stesso. Resta il
            // numero. Si provano i candidati in ordine e si tiene quello che
            // riesce, riselezionando ogni volta perche' una chiamata fallita
            // puo' aver consumato la selezione.
            // LA CHIAMATA GIUSTA NON E' QUELLA GENERALE.
            //
            // `IFeatureManager.InsertRefPlane` e' l'API generale dei piani di
            // riferimento — vincoli, angoli, tre riferimenti — e per un piano
            // SFALSATO non funziona: misurato il 10 settembre 2026 con la
            // selezione verificata (un oggetto, marca 1, tipo 4 =
            // swSelDATUMPLANES) e provando tutti i vincoli plausibili (8, 9,
            // 1, 264, 265). Tutti null, e nessun piano in piu' nell'albero:
            // quindi non era il cast a mentire, il piano proprio non nasceva.
            //
            // Per lo sfalsamento c'e' un'API dedicata:
            //   IModelDoc2.CreatePlaneAtOffset3(Double Val, Boolean FlipDir,
            //       Boolean AutoSize) -> Object
            // verificata per riflessione sull'interop 29.5.1.1. E' la stessa
            // lezione del guscio, dove `InsertFeatureShell` sta su IModelDoc2
            // e non su IFeatureManager come tutto il resto: la famiglia a cui
            // una chiamata appartiene non si indovina dal nome.
            //
            // IL VERSO STA IN UN BOOLEANO, NON NEL SEGNO. `Val` e' una
            // distanza, e da che parte va lo dice `FlipDir`: passare un numero
            // negativo non sposterebbe dall'altra parte, sarebbe solo una
            // distanza che non vuol dire niente.
            int pianiPrima = ContaPiani(modello);
            IFeature feature = null;
            try
            {
                object grezzo = modello.CreatePlaneAtOffset3(
                    Val: Unita.M(Math.Abs(distanza)),
                    FlipDir: distanza < 0,
                    AutoSize: true);
                feature = grezzo as IFeature;
                // Il valore di ritorno e' un `RefPlane`, che NON e' una
                // `Feature`: il cast puo' dare null anche a piano creato. Chi
                // decide e' l'albero.
                if (feature == null && ContaPiani(modello) > pianiPrima)
                    feature = UltimaFeature(modello);
            }
            catch (Exception ex)
            {
                return Fallito("Il piano non e' stato creato: " + ex.Message + comEra);
            }
            if (feature == null)
                return Fallito(
                    "Il piano non e' stato creato, e nell'albero non ne e' " +
                    "comparso nessuno." + comEra);

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["piano_di_partenza"] = piano,
                ["piani_prima"] = pianiPrima,
                ["distanza_mm"] = distanza,
            });
        }

        /// <summary>
        /// Un asse nuovo, lungo il centro di una faccia cilindrica o conica.
        ///
        /// E' l'operazione che manca perche' «ripeti_circolare» smetta di
        /// essere un vicolo cieco: pretende un asse che esista gia', e fino
        /// alla 0.30.2 nessuna operazione del catalogo ne creava uno.
        ///
        /// FIRME VERIFICATE sull'interop 29.5.1.1:
        ///   IModelDoc2.InsertAxis2(Boolean AutoSize) -> Boolean
        ///   IModelDoc2.IFeatureByPositionReverse(Int32 Num) -> Feature
        ///   ISurface.IsCylinder(), ISurface.IsCone()
        ///
        /// RITORNA BOOLEAN, NON UNA FEATURE — come il guscio. L'asse si
        /// ritrova in coda all'albero, e siccome «in coda» e' una deduzione e
        /// non un fatto, si RICONTROLLA che quella feature sia davvero di tipo
        /// «RefAxis»: se non lo e', si fallisce invece di restituire un esito
        /// che sembra giusto. «RefAxis» e' la stessa stringa con cui
        /// StatoDocumento riconosce gli assi esistenti, quindi un asse che
        /// passa questo controllo comparira' nello stato al passo dopo.
        /// </summary>
        private Esito CreaAsse(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var faccia = RisolviFaccia(stato, a, Str(a, "faccia_uid", ""), out string motivo);
            if (faccia == null) return Fallito(motivo);

            bool tonda;
            try
            {
                var superficie = faccia.IGetSurface();
                tonda = superficie != null && (superficie.IsCylinder() || superficie.IsCone());
            }
            catch (Exception ex)
            {
                return Fallito("Non riesco a leggere la faccia: " + ex.Message);
            }
            if (!tonda)
                return Fallito(
                    "Questa faccia non e' cilindrica ne' conica: l'asse si fa " +
                    "dal centro di un foro o di un albero. Per l'asse " +
                    "all'intersezione di due piani questa versione non ha " +
                    "un'operazione.");

            modello.ClearSelection2(true);
            var entita = faccia as IEntity;
            if (entita == null || !entita.SelectByMark(false, 1))
                return Fallito("Non sono riuscito a selezionare la faccia.");

            bool creato;
            try { creato = modello.InsertAxis2(true); }
            catch (Exception ex) { return Fallito("L'asse non e' stato creato: " + ex.Message); }
            if (!creato)
                return Fallito(
                    "L'asse non e' stato creato. Di solito e' la faccia: non e' " +
                    "abbastanza cilindrica, o e' quella sbagliata.");

            IFeature feature = null;
            try { feature = modello.IFeatureByPositionReverse(0); } catch { }
            if (feature == null)
                return Fallito(
                    "SolidWorks dice di aver creato l'asse ma non lo ritrovo " +
                    "nell'albero per confermartelo. Guarda il pezzo prima di " +
                    "fidarti del resto del piano.");

            string tipoCreato = "";
            try { tipoCreato = feature.GetTypeName2(); } catch { }
            if (tipoCreato != "RefAxis")
                return Fallito(
                    "L'ultima feature dell'albero e' «" + tipoCreato + "», non un " +
                    "asse. Non ti confermo un asse che non trovo.");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["asse"] = feature.Name,
            });
        }

        /// <summary>
        /// Ripete una lavorazione in fila, o in griglia su due direzioni.
        ///
        /// PERCHE' CONTA. Otto fori in fila, senza questa, vogliono otto
        /// chiamate con otto coordinate calcolate a mano: lento, e ogni
        /// coordinata e' un'occasione di sbagliare. Peggio: il pezzo finito
        /// non DICHIARA che quei fori sono una serie, quindi cambiarne il
        /// passo vuol dire rifarli tutti.
        ///
        /// FIRMA VERIFICATA sull'interop 29.5.1.1 — e' gia' la piu' alta:
        ///   IFeatureManager.FeatureLinearPattern5(Num1, Spacing1, Num2,
        ///       Spacing2, FlipDir1, FlipDir2, DName1, DName2,
        ///       GeometryPattern, VaryInstance, HasOffset1, HasOffset2,
        ///       CtrlByNum1, CtrlByNum2, FromCentroid1, FromCentroid2,
        ///       RevOffset1, RevOffset2, Offset1, Offset2, D2PatternSeedOnly,
        ///       SyncSubAssemblies) -> Feature
        ///
        /// I «MARK» DELLA SELEZIONE NON SONO VERIFICABILI PER RIFLESSIONE:
        /// non stanno nei metadati, sono comportamento a runtime. Qui si usa
        /// Mark 1 per la prima direzione e Mark 4 per la lavorazione, che e'
        /// quello che fa gia' `RipetiCircolare` e funziona; Mark 2 per la
        /// seconda direzione e' un'analogia con i nomi dei parametri, NON una
        /// certezza. Una ripetizione nella direzione sbagliata e' un pezzo
        /// che sembra giusto: va misurata su una griglia 2x2 prima di
        /// fidarsene.
        ///
        /// GeometryPattern resta FALSE. Copiare solo la forma e' piu' veloce
        /// e piu' fragile: un foro filettato ripetuto cosi' perde il filetto
        /// sulle copie, e nessun errore lo dice.
        /// </summary>
        private Esito RipetiLineare(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double volumePrima; int corpiPrima, faccePrima;
            Fotografia(modello, out volumePrima, out corpiPrima, out faccePrima);

            double passo1, istanze1Grezzo;
            string errore;
            if (!NumObbligatorio(a, "passo1_mm", out passo1, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "numero_istanze1", out istanze1Grezzo, out errore))
                return Fallito(errore);
            int istanze1 = (int)istanze1Grezzo;


            string asse2 = Str(a, "asse2_uid", "");
            bool griglia = !string.IsNullOrEmpty(asse2);
            int istanze2 = 1;
            double passo2 = 0;
            if (griglia)
            {
                double n2;
                if (!NumObbligatorio(a, "numero_istanze2", out n2, out errore))
                    return Fallito("Hai dato «asse2_uid» ma non «numero_istanze2»: " + errore);
                istanze2 = (int)n2;
                if (istanze2 < 2)
                    return Fallito(
                        "«numero_istanze2» deve essere almeno 2. Per una fila sola " +
                        "ometti «asse2_uid», invece di mettere 1.");
                if (!NumObbligatorio(a, "passo2_mm", out passo2, out errore))
                    return Fallito("Hai dato «asse2_uid» ma non «passo2_mm»: " + errore);
            }

            modello.ClearSelection2(true);
            if (!SelezionaAsse(modello, a, "asse1", stato, out errore)) return Fallito(errore);
            // La seconda direzione passa dallo STESSO aiutante, con Mark 2:
            // prima aveva una riga sua che accettava solo un asse, e da quando
            // la direzione puo' essere uno spigolo le due strade divergevano —
            // una griglia con la direzione 1 su uno spigolo e la 2 no.
            if (griglia && !SelezionaAsse(modello, a, "asse2", stato, out errore, 2))
                return Fallito("Direzione 2: " + errore);

            if (!SelezionaLavorazione(modello, a, out errore)) return Fallito(errore);
            // Prima di costruire: creare la ripetizione azzera la selezione.
            var seme = SemeSelezionato(modello, 4);

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.FeatureLinearPattern5(
                    Num1: istanze1,
                    Spacing1: Unita.M(passo1),
                    Num2: istanze2,
                    Spacing2: Unita.M(passo2),
                    FlipDir1: Bool(a, "capovolgi_1", false),
                    FlipDir2: Bool(a, "capovolgi_2", false),
                    DName1: "",
                    DName2: "",
                    GeometryPattern: false,
                    VaryInstance: false,
                    HasOffset1: false,
                    HasOffset2: false,
                    CtrlByNum1: true,
                    CtrlByNum2: true,
                    FromCentroid1: false,
                    FromCentroid2: false,
                    RevOffset1: false,
                    RevOffset2: false,
                    Offset1: 0,
                    Offset2: 0,
                    // false = griglia PIENA: ogni istanza della direzione 1 si
                    // ripete anche sulla 2. true farebbe una croce, che ripete
                    // solo l'originale. Per una piastra forata a matrice serve
                    // la griglia.
                    D2PatternSeedOnly: false,
                    SyncSubAssemblies: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("La ripetizione lineare non e' riuscita: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "La ripetizione lineare non e' stata creata. Di solito e' il " +
                    "passo o il numero: una serie che esce dal materiale, o che " +
                    "si sovrappone a se stessa, non si costruisce.");

            var misure = new Dictionary<string, object>
            {
                ["numero_istanze1"] = istanze1,
                ["passo1_mm"] = passo1,
            };
            if (griglia)
            {
                misure["numero_istanze2"] = istanze2;
                misure["passo2_mm"] = passo2;
            }

            var fermo = ConfrontaCopia(
                modello, volumePrima, corpiPrima, faccePrima, misure,
                "La ripetizione lineare");
            if (fermo != null) return Fallito(fermo);

            var mancanti = CopieMancanti(
                misure, feature, seme, istanze1 * istanze2 - 1,
                "La ripetizione lineare",
                "Se la fila esce dal pezzo da una parte sola, inverti il verso con " +
                "«capovolgi_1» (o «capovolgi_2» per la seconda direzione); " +
                "altrimenti riduci il passo o il numero di istanze.");
            if (mancanti != null) return Fallito(mancanti);

            return Riuscito(feature, misure);
        }

        /// <summary>
        /// Specchia una lavorazione rispetto a uno dei tre piani base.
        ///
        /// FIRMA VERIFICATA sull'interop 29.5.1.1 — gia' la piu' alta:
        ///   IFeatureManager.InsertMirrorFeature2(Boolean BMirrorBody, Boolean
        ///       BGeometryPattern, Boolean BMerge, Boolean BKnit, Int32
        ///       ScopeOptions) -> Feature
        ///   swFeatureScope_e: AllBodies = 0
        ///
        /// I MARK, come per la ripetizione lineare, non sono verificabili per
        /// riflessione: Mark 1 al piano e Mark 4 alla lavorazione sono
        /// l'analogia con `RipetiCircolare`. Da misurare specchiando un foro
        /// NON centrato: deve comparire dall'altra parte del piano, non
        /// sovrapposto all'originale.
        ///
        /// Si specchia la LAVORAZIONE, non il corpo: `BMirrorBody` resta
        /// false. Specchiare un corpo intero e' il caso piu' comune in
        /// officina per un pezzo simmetrico, ma vuole capire prima come si
        /// seleziona «il corpo» — nessun aiutante di questo file lo fa — e
        /// riverificare gli stessi Mark su quel ramo.
        /// </summary>
        private Esito Specchia(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string piano = Str(a, "piano", "");
            if (string.IsNullOrEmpty(piano)) return Fallito("Manca il piano di simmetria.");

            double volumePrima; int corpiPrima, faccePrima;
            Fotografia(modello, out volumePrima, out corpiPrima, out faccePrima);

            modello.ClearSelection2(true);
            var pianoFeature = TrovaPianoBase(modello, piano);
            // `InsertMirrorFeature2` vuole il PIANO con la marca 2 e le
            // lavorazioni con la 1 — non le stesse marche della ripetizione.
            // Fino alla 0.35.0 qui c'erano 1 e 4, cioe' quelle del pattern, e
            // lo specchio non veniva creato.
            if (pianoFeature == null || !pianoFeature.Select2(false, 2))
                return Fallito($"Non ho trovato il piano «{piano}» nell'albero.");

            string errore;
            if (!SelezionaLavorazione(modello, a, out errore, marca: 1)) return Fallito(errore);
            var seme = SemeSelezionato(modello, 1);

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertMirrorFeature2(
                    BMirrorBody: false,
                    BGeometryPattern: false,
                    BMerge: true,
                    BKnit: false,
                    ScopeOptions: (int)swFeatureScope_e.swFeatureScope_AllBodies) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Lo specchio non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "Lo specchio non e' stato creato. Se il piano scelto taglia la " +
                    "lavorazione invece di lasciarla tutta da un lato, o se la " +
                    "copia uscirebbe fuori dal materiale, SolidWorks rifiuta.");

            var misure = new Dictionary<string, object> { ["piano"] = piano };
            var fermo = ConfrontaCopia(
                modello, volumePrima, corpiPrima, faccePrima, misure, "Lo specchio");
            if (fermo != null) return Fallito(fermo);

            // QUI IL RIFIUTO NON PUO' SCATTARE, ED E' GIUSTO COSI'.
            // Con una copia sola servirebbe «zero copie fatte», e zero non
            // esce mai: CopieMancanti si ferma prima se la feature non ha
            // facce. Quindi sullo specchio questa chiamata solo RIPORTA
            // «copie_fatte», e lo specchio che non ha specchiato niente resta
            // in carico a ConfrontaCopia, qui sopra, che lo prende dal pezzo
            // rimasto identico.
            //
            // Sta scritto perche' una guardia che sembra proteggere e non puo'
            // scattare e' peggio di una guardia assente: la prima volta si
            // legge come una difesa, la seconda ci si conta sopra.
            CopieMancanti(misure, feature, seme, 1, "Lo specchio", "");

            return Riuscito(feature, misure);
        }

        /// <summary>
        /// L'asse attorno a cui — o lungo cui — si ripete, con Mark 1.
        ///
        /// PER RIFERIMENTO O PER IDENTIFICATIVO, ED E' UNA CORREZIONE.
        /// Fino alla 0.30.2 `ripeti_circolare` accettava SOLO `asse_uid`, cioe'
        /// un asse che esistesse gia' nel documento quando il piano nasceva. E
        /// nessuna operazione del catalogo creava assi. Il risultato: la
        /// ripetizione circolare era un vicolo cieco, offerta e mai
        /// utilizzabile.
        ///
        /// Adesso «crea_asse» ne fa uno, e questo aiutante permette al passo
        /// dopo di citarlo — «fai l'asse del foro, poi ripeti la lavorazione
        /// sei volte attorno» in un piano solo. L'oggetto si seleziona
        /// direttamente: il nome che SolidWorks gli ha dato lo sapremmo solo
        /// rileggendo l'albero, e sarebbe un giro in piu' per arrivare alla
        /// stessa feature che gia' abbiamo in mano.
        /// </summary>
        /// <param name="prefisso">«asse», «asse1» oppure «asse2»: i campi si
        /// chiamano diversamente ma si risolvono identici.</param>
        /// <param name="mark">
        /// 1 per la direzione principale — che e' quella che tutte e tre le
        /// ripetizioni leggono — e 2 per la seconda direzione di una griglia.
        /// Con Mark diverso da 1 si ACCUMULA sulla selezione gia' fatta,
        /// altrimenti la seconda direzione cancellerebbe la prima.
        /// </param>
        private bool SelezionaAsse(
            IModelDoc2 modello, Dictionary<string, object> a, string prefisso,
            StatoDocumento stato, out string motivo, int mark = 1)
        {
            motivo = null;
            string campoRif = prefisso + "_rif";
            if (HaRiferimento(a, campoRif))
            {
                int numero;
                var passo = PassoCitato(a, campoRif, out numero, out motivo);
                if (passo == null)
                {
                    motivo = motivo ?? "Riferimento all'asse non risolto.";
                    return false;
                }
                if (passo.Feature == null)
                {
                    motivo = "Il passo " + numero + " non ha lasciato nessun asse.";
                    return false;
                }
                if (!passo.Feature.Select2(mark != 1, mark))
                {
                    motivo = "Non sono riuscito a selezionare l'asse del passo " + numero + ".";
                    return false;
                }
                _log("[agente] asse preso dal passo " + numero);
                return true;
            }

            string uid = Str(a, prefisso + "_uid", "");
            if (string.IsNullOrEmpty(uid))
            {
                motivo =
                    "Non hai detto lungo o attorno a quale direzione. Indica «" +
                    prefisso + "_uid» con un asse OPPURE con uno spigolo dritto " +
                    "dello stato del documento, oppure «" + campoRif + "» se " +
                    "l'asse lo crea un passo precedente di questo piano con " +
                    "«crea_asse».";
                return false;
            }

            // ── UNO SPIGOLO DRITTO VALE COME DIREZIONE, ED E' UNA CORREZIONE ──
            //
            // Fino alla 0.30.2 qui c'era solo `SelectByID2(uid, "AXIS", ...)`,
            // cioe' un asse di riferimento. Ma l'unica operazione del catalogo
            // che crea assi e' «crea_asse», che li prende dal centro di una
            // faccia CILINDRICA: l'asse che ne esce e' sempre quello di un
            // foro o di un albero, cioe' PERPENDICOLARE alla faccia su cui la
            // lavorazione sta. Ripetere lungo quello vuol dire ripetere dentro
            // lo spessore.
            //
            // Risultato: «otto fori in fila sulla piastra» — il caso per cui
            // esiste la ripetizione lineare — non aveva NESSUNA forma valida.
            // Era lo stesso vicolo cieco che la 0.30.2 ha chiuso per la
            // ripetizione circolare, un piano piu' in la'.
            //
            // Uno spigolo del pezzo la direzione ce l'ha, e lo stato del
            // documento gli spigoli li elenca gia' (E1, E2, ...). SolidWorks
            // accetta uno spigolo lineare come direzione di una ripetizione
            // lineare e come asse di una circolare.
            //
            // DEVE ESSERE DRITTO, E SI CONTROLLA. Uno spigolo curvo non da'
            // errore: da' la direzione della tangente in un punto che nessuno
            // ha scelto, cioe' un pezzo che sembra giusto.
            StatoDocumento.FirmaEntita firma;
            if (stato != null && stato.Firme.TryGetValue(uid, out firma) && firma.Tipo == "EDGE")
            {
                string scostamento;
                if (!FirmaCombacia(a, uid, firma, out scostamento)) { motivo = scostamento; return false; }

                var spigolo = firma.Entita as IEdge;
                bool dritto = false;
                try
                {
                    var curva = spigolo == null ? null : spigolo.IGetCurve();
                    dritto = curva != null && curva.IsLine();
                }
                catch { }
                if (!dritto)
                {
                    motivo =
                        "Lo spigolo «" + uid + "» non e' dritto: non da' una " +
                        "direzione. Per la ripetizione serve uno spigolo " +
                        "rettilineo, oppure un asse fatto con «crea_asse».";
                    return false;
                }

                var entita = spigolo as IEntity;
                if (entita == null || !entita.SelectByMark(mark != 1, mark))
                {
                    motivo = "Lo spigolo «" + uid + "» non si e' lasciato selezionare.";
                    return false;
                }
                _log("[agente] direzione presa dallo spigolo " + uid);
                return true;
            }

            if (!modello.Extension.SelectByID2(uid, "AXIS", 0, 0, 0, mark != 1, mark, null, 0))
            {
                motivo =
                    "Non ho trovato l'asse «" + uid + "». Gli assi si fanno con " +
                    "«crea_asse»; in alternativa indica uno spigolo dritto del " +
                    "pezzo, che nello stato ha un identificativo come «E1».";
                return false;
            }
            return true;
        }

        /// <summary>
        /// La lavorazione da ripetere o specchiare, con Mark 4: per
        /// riferimento a un passo precedente oppure per identificativo.
        ///
        /// In un metodo solo perche' ripetizione lineare, ripetizione
        /// circolare e specchio la nominano nello stesso identico modo, e tre
        /// copie della stessa logica divergono alla prima correzione.
        /// </summary>
        /// <param name="marca">
        /// LA MARCA NON E' LA STESSA PER TUTTE LE FEATURE, ed e' il genere di
        /// dettaglio che non da' errore: con la marca sbagliata la selezione
        /// c'e', la feature non la vede, e torna null senza dire niente.
        /// Le ripetizioni vogliono le lavorazioni con la marca 4 e l'asse con
        /// la 1; lo specchio le vuole con la 1 e il piano con la 2.
        /// </param>
        private bool SelezionaLavorazione(
            IModelDoc2 modello, Dictionary<string, object> a, out string motivo,
            int marca = 4)
        {
            motivo = null;
            if (HaRiferimento(a, "feature_rif"))
            {
                int numero;
                var passo = PassoCitato(a, "feature_rif", out numero, out motivo);
                if (passo == null)
                {
                    motivo = motivo ?? "Riferimento non risolto.";
                    return false;
                }
                if (passo.Feature == null)
                {
                    motivo = "Il passo " + numero + " non ha lasciato una lavorazione.";
                    return false;
                }
                if (!passo.Feature.Select2(true, marca))
                {
                    motivo = "Non sono riuscito a selezionare la lavorazione del passo " +
                             numero + ".";
                    return false;
                }
                _log("[agente] lavorazione presa dal passo " + numero);
                return true;
            }

            string uid = Str(a, "feature_uid", "");
            if (string.IsNullOrEmpty(uid))
            {
                motivo = "Non hai detto quale lavorazione ripetere o specchiare.";
                return false;
            }
            // E' ANCORA QUELLA? Vedi LavorazioneEQuella.
            if (!LavorazioneEQuella(modello, uid, a, out motivo)) return false;

            if (!modello.Extension.SelectByID2(uid, "BODYFEATURE", 0, 0, 0, true, marca, null, 0))
            {
                motivo = "Non ho trovato la lavorazione «" + uid + "».";
                return false;
            }
            return true;
        }

        /// <summary>
        /// La lavorazione che porta quel nome e' ancora quella che il piano
        /// aveva guardato?
        ///
        /// L'UID DI UNA LAVORAZIONE E' IL SUO NOME — non c'e' altro
        /// nell'albero — e un nome si rinomina. Chi rinomina «Taglio1» e poi
        /// chiama «Taglio1» un'altra lavorazione ha trasferito
        /// l'identificativo da una cosa a un'altra, e `SelectByID2` selezionera'
        /// obbedientemente quella nuova.
        ///
        /// `IFeature.GetID()` non si rinomina, e il piano se lo porta dietro
        /// nella firma. Qui si confrontano.
        ///
        /// SI CONTROLLA SOLO SE SI PUO'. Niente firma, o firma senza
        /// `feature_id`, vuol dire niente da confrontare: non si inventa un
        /// rifiuto. E' la stessa regola di `FirmaCombacia`.
        /// </summary>
        private static bool LavorazioneEQuella(
            IModelDoc2 modello, string uid,
            Dictionary<string, object> argomenti, out string motivo)
        {
            motivo = null;
            var attesa = FirmaAttesa(argomenti, uid);
            if (attesa == null) return true;
            object grezzo;
            if (!attesa.TryGetValue("feature_id", out grezzo) || grezzo == null) return true;
            int atteso;
            try { atteso = Convert.ToInt32(grezzo); } catch { return true; }

            var parte = modello as IPartDoc;
            if (parte == null) return true;
            IFeature f = parte.IFirstFeature();
            while (f != null)
            {
                string nome = null;
                try { nome = f.Name; } catch { }
                if (string.Equals(nome, uid, StringComparison.OrdinalIgnoreCase))
                {
                    int adesso;
                    try { adesso = f.GetID(); } catch { return true; }
                    if (adesso == atteso) return true;
                    motivo =
                        "La lavorazione «" + uid + "» non e' piu' quella che avevi " +
                        "guardato: qualcuno ha rinominato l'albero, e adesso quel nome " +
                        "sta su un'altra lavorazione. Rileggi lo stato del documento e " +
                        "nominala di nuovo.";
                    return false;
                }
                f = f.GetNextFeature() as IFeature;
            }
            // Non l'ho trovata scorrendo l'albero: lo dice SelectByID2 con il
            // suo messaggio, che e' piu' preciso di uno inventato qui.
            return true;
        }

        /// <summary>
        /// Il piano base indicato, SENZA selezionarlo.
        ///
        /// Non e' un doppione pigro di `SelezionaPiano`: quella seleziona
        /// sempre con Mark 0 — che va bene alle estrusioni, che i Mark non li
        /// leggono — e restituisce un bool. Qui serve la Feature, per poterla
        /// selezionare con il Mark che l'operazione vuole.
        /// </summary>
        private static IFeature TrovaPianoBase(IModelDoc2 modello, string quale)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return null;
            // TRE NOMI, E NIENT'ALTRO. Prima qui c'era un `: 1` finale che
            // faceva ripiegare sul frontale QUALUNQUE nome sconosciuto: chi
            // chiedeva di specchiare sul «piano medio» si prendeva il frontale
            // senza che nessuno lo dicesse. Misurato il 10 settembre 2026: la
            // prova «specchia su un piano che non esiste» non rifiutava.
            int voluto = quale == "frontale" ? 1
                       : quale == "superiore" ? 2
                       : quale == "destro" ? 3
                       : 0;
            if (voluto == 0) return null;
            int visti = 0;
            IFeature f = parte.IFirstFeature();
            while (f != null)
            {
                string tipo = "";
                try { tipo = f.GetTypeName2(); } catch { }
                if (tipo == "RefPlane")
                {
                    visti++;
                    if (visti == voluto) return f;
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { return null; }
            }
            return null;
        }

        /// <summary>
        /// Rivoluzione: ruota un profilo attorno a un asse. E' il modo generale
        /// di fare un corpo a simmetria di rotazione — boccole, distanziali,
        /// alberi a gradini, flange, pulegge, volantini — quando la forma non
        /// e' un cilindro costante (quello e' «estrudi_cerchio», piu' semplice).
        ///
        /// L'ASSE E' IL PUNTO DIFFICILE, E VALE LA PENA SPIEGARLO.
        /// `FeatureRevolve2` vuole, al momento della chiamata, sia il profilo
        /// sia l'asse. In un documento vuoto — il caso normale, perche' questa
        /// operazione fa il PRIMO corpo — non c'e' nessun asse di riferimento
        /// da citare e nessuno spigolo da usare come tale.
        ///
        /// La strada scelta: l'asse si disegna come CENTERLINE dentro lo stesso
        /// schizzo del profilo (`ISketchManager.CreateCenterLine`, verificata
        /// per riflessione: stessa firma di CreateLine), e poi si seleziona lo
        /// schizzo intero, come gia' si fa per estrusioni e tagli.
        /// `UseAutoSelect: true` legge dallo schizzo il profilo chiuso e
        /// l'unica linea di costruzione che contiene.
        ///
        /// FIRMA VERIFICATA sull'interop 29.5.1.1, usata COSI' e non
        /// «aggiornata» a un numero piu' alto:
        ///   IFeatureManager.FeatureRevolve2(SingleDir, IsSolid, IsThin,
        ///       IsCut, ReverseDir, BothDirectionUpToSameEntity, Dir1Type,
        ///       Dir2Type, Dir1Angle, Dir2Angle, OffsetReverse1,
        ///       OffsetReverse2, OffsetDistance1, OffsetDistance2, ThinType,
        ///       ThinThickness1, ThinThickness2, Merge, UseFeatScope,
        ///       UseAutoSelect) -> Feature
        ///
        /// SOLO IL CORPO PIENO: IsThin resta false. Per una parete sottile si
        /// disegna il vuoto come secondo contorno del profilo.
        ///
        /// E SI CONTA IL RISULTATO. Un profilo che ATTRAVERSA l'asse non da'
        /// errore: da' un solido che si autointerseca. E' la stessa famiglia
        /// del foro da 50,8 mm che sembrava giusto. Partendo da un documento
        /// senza corpi — che e' la precondizione — una rivoluzione riuscita ne
        /// lascia esattamente UNO: zero o piu' di uno vuol dire che il profilo
        /// e l'asse non erano quello che sembravano.
        /// </summary>
        private Esito Rivoluzione(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var contorni = Contorni(a, out string motivo, out string _);
            if (contorni == null) return Fallito(motivo);

            object asseGrezzo;
            if (!a.TryGetValue("asse", out asseGrezzo) || asseGrezzo == null)
                return Fallito("Manca l'asse di rivoluzione.");
            var puntiAsse = Punti(asseGrezzo, out motivo);
            if (puntiAsse == null) return Fallito("Asse di rivoluzione: " + motivo);
            if (puntiAsse.Count != 2)
                return Fallito(
                    "L'asse di rivoluzione va dato come due punti [x_mm, y_mm], non " +
                    puntiAsse.Count + ".");
            double dx = puntiAsse[1][0] - puntiAsse[0][0];
            double dy = puntiAsse[1][1] - puntiAsse[0][1];
            if (Math.Sqrt(dx * dx + dy * dy) < 1e-6)
                return Fallito("I due punti dell'asse coincidono: non definiscono una retta.");

            double angolo = Num(a, "angolo_gradi", 360);
            string piano = Str(a, "piano", "frontale");
            if (!SelezionaPiano(modello, piano))
                return Fallito($"Non ho trovato il piano «{piano}» nell'albero.");

            var schizzi = modello.SketchManager;
            bool addToDbPrima = false, displayPrima = false;
            try { addToDbPrima = schizzi.AddToDB; displayPrima = schizzi.DisplayWhenAdded; } catch { }

            int segmenti = 0;
            bool asseCreato = false;
            try
            {
                schizzi.InsertSketch(true);
                try { schizzi.AddToDB = true; schizzi.DisplayWhenAdded = false; } catch { }

                foreach (var contorno in contorni) segmenti += DisegnaContorno(schizzi, contorno);

                var segmentoAsse = schizzi.CreateCenterLine(
                    Unita.M(puntiAsse[0][0]), Unita.M(puntiAsse[0][1]), 0,
                    Unita.M(puntiAsse[1][0]), Unita.M(puntiAsse[1][1]), 0) as ISketchSegment;
                // Ridondante se, come atteso, CreateCenterLine nasce gia' di
                // costruzione: si scrive lo stesso perche' non costa niente ed
                // e' la differenza fra un asse riconosciuto e una linea
                // qualunque, se quell'attesa fosse sbagliata.
                try { if (segmentoAsse != null) segmentoAsse.ConstructionGeometry = true; } catch { }
                asseCreato = segmentoAsse != null;
            }
            finally
            {
                try { schizzi.AddToDB = addToDbPrima; schizzi.DisplayWhenAdded = displayPrima; } catch { }
            }
            schizzi.InsertSketch(false);

            if (!asseCreato)
                return Fallito("Non sono riuscito a disegnare l'asse di rivoluzione." + SenzaLoSchizzo(modello));
            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.FeatureRevolve2(
                    SingleDir: true,
                    IsSolid: true,
                    IsThin: false,
                    IsCut: false,
                    ReverseDir: false,
                    BothDirectionUpToSameEntity: false,
                    Dir1Type: (int)swEndConditions_e.swEndCondBlind,
                    Dir2Type: (int)swEndConditions_e.swEndCondBlind,
                    Dir1Angle: Unita.Rad(angolo),
                    Dir2Angle: 0,
                    OffsetReverse1: false,
                    OffsetReverse2: false,
                    OffsetDistance1: 0,
                    OffsetDistance2: 0,
                    ThinType: 0,
                    ThinThickness1: 0,
                    ThinThickness2: 0,
                    Merge: true,
                    UseFeatScope: true,
                    UseAutoSelect: true) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("La rivoluzione non e' riuscita: " + ex.Message + SenzaLoSchizzo(modello));
            }
            if (feature == null)
                return Fallito(
                    "La rivoluzione non e' stata creata. Di solito e' l'asse: deve " +
                    "stare sul piano del profilo, e il profilo non deve " +
                    "attraversarlo." + SenzaLoSchizzo(modello));

            int nCorpi = ContaCorpiSolidi(modello);
            if (nCorpi != 1)
                return Fallito(
                    "La rivoluzione ha creato la feature ma il documento ha " +
                    nCorpi + " corpi solidi invece di uno: molto probabilmente " +
                    "il profilo attraversa l'asse. Non ti confermo un pezzo " +
                    "diverso da quello che ti ho mostrato: ritiralo — " +
                    Frasi.CosiSiRitira + ".");

            string notaBlocco;
            bool bloccato = BloccaSchizzo(modello, out notaBlocco);
            return Riuscito(feature, new Dictionary<string, object>
            {
                ["schizzo_bloccato"] = bloccato,
                ["contorni"] = contorni.Count,
                ["segmenti"] = segmenti,
                ["angolo_gradi"] = angolo,
            }, Riferimenti.NormaleDelPiano(piano));
        }

        /// <summary>
        /// Il guscio: svuota un corpo pieno lasciando una parete di spessore
        /// costante. Le facce indicate vengono TOLTE, cioe' restano aperte.
        ///
        /// DUE COSE DA SAPERE PRIMA DI TOCCARE QUESTO METODO, tutte e due
        /// verificate per riflessione sull'interop 29.5.1.1:
        ///
        ///   IModelDoc2.InsertFeatureShell(Double Thickness, Boolean Outward)
        ///
        ///   1. sta su IModelDoc2, NON su IFeatureManager dove sta tutto il
        ///      resto di questo file. E' una delle sette API che nel RAG
        ///      «sembravano giuste e non lo erano»;
        ///   2. ritorna VOID. Non c'e' nessun InsertFeatureShell2 che
        ///      restituisca una Feature.
        ///
        /// Non prende le facce come parametro: lavora su quello che e' GIA'
        /// SELEZIONATO, come il raccordo e lo smusso fanno con gli spigoli.
        ///
        /// SICCOME NON TORNA UNA FEATURE, la si va a cercare in coda
        /// all'albero — e siccome «l'albero e' cresciuto» non basta a dire che
        /// e' cresciuto BENE, si misura il volume: un guscio TOGLIE materiale,
        /// e ne toglie meno del corpo intero. Il 9 settembre 2026 abbiamo
        /// misurato che in questo stesso agente una chiamata puo' riuscire e
        /// produrre geometria sbagliata senza dare errore (HoleWizard5, foro
        /// da due pollici). Qui il controllo costa due letture.
        /// </summary>
        private Esito Guscio(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double spessore = Num(a, "spessore_mm");
            bool versoEsterno = Bool(a, "verso_esterno", false);
            bool tuttoChiuso = Bool(a, "tutto_chiuso", false);
            var uid = Lista(a, "facce_uid");

            // LE FACCE POSSONO VENIRE DA UN PASSO PRECEDENTE (voce E15).
            //
            // Fino alla 0.30.0 `guscio` era l'UNICA operazione su faccia che
            // non accettava un riferimento a un passo: voleva `facce_uid`, che
            // nascono solo da una lettura. Quindi «fai un blocco e poi svuotalo»
            // non si poteva scrivere in un piano solo, mentre «fai un blocco e
            // poi foralo» si' — ed e' proprio la sequenza del contenitore per
            // l'antenna: blocco, guscio, alette.
            //
            // UN ELENCO DI RIFERIMENTI SINGOLI, non un genere plurale. Il
            // vocabolario di `Riferimenti` risolve una faccia per volta, e
            // nessuna operazione del catalogo usa oggi un genere che ne dia
            // piu' d'una: inventarne uno qui vorrebbe dire aprire superficie
            // nuova per un problema che si risolve ripetendo l'elemento.
            List<IFace2> daiPassi = null;
            if (uid.Count == 0 && HaElenco(a, "facce_rif"))
            {
                if (!FacceDaiRiferimenti(a, "facce_rif", out daiPassi, out string perche))
                    return Fallito(perche);
            }
            else if (uid.Count > 0 && HaElenco(a, "facce_rif"))
            {
                return Fallito(
                    "Il guscio ha sia «facce_uid» sia «facce_rif»: uno nomina facce " +
                    "che ci sono gia', l'altro facce che le crea un passo di questo " +
                    "piano. Scegline uno.");
            }

            if (uid.Count == 0 && daiPassi == null && !tuttoChiuso)
                return Fallito(
                    "Nessuna faccia indicata per il guscio. Se la faccia la crea un " +
                    "passo precedente di questo piano, usa «facce_rif»: " +
                    "[{\"passo\": 1, \"genere\": \"faccia_superiore\"}]. Se serve davvero " +
                    "un corpo cavo completamente chiuso, senza nessuna apertura, metti " +
                    "«tutto_chiuso: true» — di solito pero' e' una faccia dimenticata " +
                    "nella richiesta.");

            modello.ClearSelection2(true);

            if (daiPassi != null)
            {
                // Le facce arrivano FRESCHE dal passo appena eseguito, non da
                // una lettura precedente: non c'e' nessuna firma da
                // confrontare, ed e' lo stesso motivo per cui il ramo
                // `faccia_rif` di `RisolviFaccia` salta quel controllo.
                // TUTTE, NON ALMENO UNA. La prima scrittura contava quante si
                // erano lasciate selezionare e si fermava solo a zero: con tre
                // riferimenti risolti e due facce selezionate andava avanti, e
                // il guscio usciva aperto su DUE facce invece che su tre —
                // «riuscita: true», volume calato, tutti i controlli a valle
                // soddisfatti, e un contenitore chiuso su un lato che doveva
                // essere aperto.
                //
                // La regola di questa voce era gia' scritta e valeva sulla
                // RISOLUZIONE: «se uno solo non si risolve, fallisce tutto».
                // Valeva su meta' del percorso. Il ramo degli uid, quindici
                // righe piu' giu', rifiuta alla PRIMA faccia che non si
                // seleziona: una riparazione che copre un ramo e ne lascia
                // scoperto il gemello.
                int presi = 0;
                foreach (var f in daiPassi)
                {
                    if ((f as IEntity)?.Select4(true, null) == true) { presi++; continue; }
                    return Fallito(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "La faccia n. {0} di «facce_rif» non si e' lasciata selezionare: " +
                        "{1} su {2} erano pronte. Un guscio aperto su meno facce di quelle " +
                        "chieste non e' quasi giusto, e' un pezzo diverso.{3}",
                        presi + 1, presi, daiPassi.Count, RestaNelPezzo));
                }
                _log("[agente] " + presi + " facce prese dai passi precedenti");
            }
            else if (uid.Count == 0)
            {
                // Guscio chiuso: si seleziona il corpo. Con piu' corpi non si
                // sceglie: si chiede. Un guscio applicato al corpo sbagliato
                // e' esattamente il pezzo che sembra giusto.
                var parte = modello as IPartDoc;
                var corpi = parte?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null || corpi.Length == 0)
                    return Fallito("Non ho trovato nessun corpo solido da rendere cavo.");
                if (corpi.Length > 1)
                    return Fallito(
                        "In questo documento ci sono " + corpi.Length + " corpi " +
                        "solidi: un guscio chiuso senza facce indicate lavorerebbe " +
                        "su uno scelto a caso. Indica le facce da togliere.");
                // UN CORPO NON E' UNA IEntity, e questo ramo non l'ha mai
                // saputo. `corpi[0] as IEntity` torna **sempre** null
                // nell'interop, quindi «tutto_chiuso: true» rispondeva «Non
                // sono riuscito a selezionare il corpo solido» su QUALUNQUE
                // pezzo — non era un caso limite, era rotto sempre.
                //
                // La cosa che fa male: la lezione era gia' scritta, in questo
                // stesso file, quattromila righe piu' su. Il commento di
                // `SpostaCorpi` la racconta per esteso — misurata dentro
                // SOLIDWORKS il 10 settembre 2026, con cinque prove che
                // dicevano «i corpi indicati non si sono lasciati selezionare»
                // — e li' e' gia' riparata con `IBody2.Select2`. Qui no.
                //
                // Una lezione scritta e non applicata e' una lezione che il
                // codice non ha imparato: il commento la ricorda a chi legge
                // QUELLE righe, e chi scrive altrove non passa di li'.
                var corpo = corpi[0] as IBody2;
                bool preso = corpo != null && corpo.Select2(false, null);
                if (!preso)
                {
                    // La vecchia strada resta come ripiego, non come prima
                    // scelta: se un domani l'interop cambiasse idea, questo
                    // ramo continua a funzionare invece di rompersi.
                    var comeEntita = corpi[0] as IEntity;
                    preso = comeEntita != null && comeEntita.Select4(false, null);
                }
                if (!preso)
                    return Fallito("Non sono riuscito a selezionare il corpo solido.");
            }
            else
            {
                // Le facce si selezionano ACCUMULANDO, come fa il fazzoletto:
                // `SelezionaFaccia` seleziona con append=false ed e' pensato
                // per una faccia sola prima di uno schizzo. Qui servono tutte
                // insieme, e usare quell'aiutante lascerebbe selezionata solo
                // l'ultima — un guscio aperto da un lato invece che da tre,
                // senza nessun errore.
                foreach (var u in uid)
                {
                    StatoDocumento.FirmaEntita firma;
                    if (stato == null || !stato.Firme.TryGetValue(u, out firma) ||
                        firma.Tipo != "FACE")
                        return Fallito("La faccia «" + u + "» non c'e' piu' in questo documento.");
                    string scostamento;
                    if (!FirmaCombacia(a, u, firma, out scostamento)) return Fallito(scostamento);
                    var ent = firma.Entita as IEntity;
                    if (ent == null || !ent.Select4(true, null))
                        return Fallito("La faccia «" + u + "» non si e' lasciata selezionare.");
                }
            }

            int featurePrima = ContaFeature(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            double volumePrima = VolumeMm3(modello);

            try
            {
                modello.InsertFeatureShell(Unita.M(spessore), versoEsterno);
            }
            catch (Exception ex)
            {
                return Fallito("Il guscio non e' riuscito: " + ex.Message);
            }

            if (ContaFeature(modello) <= featurePrima)
                return Fallito(
                    "Il guscio non e' stato creato. Di solito e' lo spessore: " +
                    "piu' grande di meta' del corpo non ci sta, e vicino a uno " +
                    "spigolo lascerebbe una parete di spessore negativo.");

            int corpiDopo = ContaCorpiSolidi(modello);
            if (corpiDopo != corpiPrima)
                return Fallito(
                    "Il guscio ha cambiato il numero di corpi solidi (da " +
                    corpiPrima + " a " + corpiDopo + "): non e' il corpo svuotato " +
                    "che ci si aspettava. Non te lo lascio passare per buono.");

            // IL GUSCIO TOGLIE MATERIALE. Se il volume non e' calato, quella
            // feature non e' un guscio, qualunque cosa dica l'albero.
            double volumeDopo = VolumeMm3(modello);
            if (volumePrima > 0 && volumeDopo > 0 && volumeDopo >= volumePrima - 0.001)
                return Fallito(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Il guscio non ha tolto niente: il volume era {0:0.#} mm3 ed " +
                    "e' {1:0.#} mm3. Non e' il pezzo che ti ho promesso.",
                    volumePrima, volumeDopo));

            var feature = UltimaFeature(modello);
            if (feature == null)
                return Fallito("Il guscio sembra creato ma non ritrovo la feature nell'albero.");

            var misure = new Dictionary<string, object>
            {
                ["spessore_mm"] = spessore,
                ["verso"] = versoEsterno ? "esterno" : "interno",
                // NON `uid.Count`: le facce possono arrivare anche da
                // `facce_rif`, e contare solo gli uid faceva dire «facce_tolte:
                // 0» a un guscio che una faccia l'aveva tolta davvero. Una
                // misura che dice zero quando il numero e' uno e' il difetto
                // che questo progetto insegue, in miniatura.
                ["facce_tolte"] = daiPassi != null ? daiPassi.Count : uid.Count,
            };
            if (volumePrima > 0 && volumeDopo > 0)
                misure["materiale_tolto_mm3"] = Math.Round(volumePrima - volumeDopo, 1);
            return Riuscito(feature, misure);
        }

        /// <summary>
        /// Quante feature ci sono nell'albero.
        ///
        /// Serve alle operazioni la cui API ritorna void — il guscio, per
        /// adesso — per sapere se hanno creato qualcosa: l'unico segno di vita
        /// e' che l'albero e' cresciuto di una posizione.
        /// </summary>
        private static int ContaFeature(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return 0;
            int n = 0;
            try
            {
                var f = parte.IFirstFeature();
                while (f != null) { n++; f = f.IGetNextFeature(); }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// L'ultima feature dell'albero, qualunque tipo abbia.
        ///
        /// NON filtra per tipo, a differenza di SelezionaSchizzoAppenaCreato:
        /// li' si cerca uno schizzo e il tipo si conosce, qui si cerca «quello
        /// che l'API ha appena creato senza dirmelo».
        /// </summary>
        /// <summary>Quanti piani di riferimento ha il pezzo, adesso.</summary>
        private static int ContaPiani(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return 0;
            int n = 0;
            IFeature f = parte.IFirstFeature();
            while (f != null)
            {
                try { if (f.GetTypeName2() == "RefPlane") n++; } catch { }
                try { f = (IFeature)f.GetNextFeature(); } catch { break; }
            }
            return n;
        }

        private static IFeature UltimaFeature(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return null;
            IFeature ultima = null;
            try
            {
                var f = parte.IFirstFeature();
                while (f != null) { ultima = f; f = f.IGetNextFeature(); }
            }
            catch { return null; }
            return ultima;
        }

        /// <summary>
        /// Quante facce hanno in tutto i corpi solidi.
        ///
        /// Serve alle operazioni che COPIANO — specchio e ripetizioni —
        /// perche' il solo volume non basta a dire che e' successo
        /// qualcosa: specchiando un taglio simmetrico rispetto al piano
        /// scelto il volume cambia, ma ci sono casi in cui cambia poco e
        /// casi in cui la copia si fonde con cio' che c'era. Le facce
        /// invece si contano, e una copia in piu' ne porta sempre.
        /// </summary>
        private static int ContaFacce(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return 0;
            int n = 0;
            try
            {
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null) return 0;
                foreach (var c in corpi)
                {
                    var facce = (c as IBody2)?.GetFaces() as object[];
                    if (facce != null) n += facce.Length;
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Com'e' il pezzo adesso: volume, corpi, facce. E' la fotografia
        /// da confrontare prima e dopo un'operazione che copia.
        /// </summary>
        private static void Fotografia(
            IModelDoc2 modello, out double volume, out int corpi, out int facce)
        {
            volume = VolumeMm3(modello);
            corpi = ContaCorpiSolidi(modello);
            facce = ContaFacce(modello);
        }

        /// <summary>
        /// Confronta la fotografia prima e dopo, e riempie le misure.
        /// Ritorna il motivo del rifiuto se NON E' SUCCESSO NIENTE.
        ///
        /// PERCHE' IL CRITERIO E' «NIENTE E' CAMBIATO» E NON QUALCOSA DI
        /// PIU' PRECISO. Di una copia non si sa in anticipo quanto volume
        /// aggiunga o tolga: dipende da quanto la copia si sovrappone a cio'
        /// che c'era. Pretendere un numero esatto vorrebbe dire rifiutare
        /// casi legittimi — e rifiutare cio' che e' soltanto insolito e' un
        /// difetto peggiore di non controllare.
        ///
        /// «Niente e' cambiato», invece, e' inequivocabile: SolidWorks ha
        /// detto di aver creato la feature e il pezzo e' identico. Quello e'
        /// il falso successo, ed e' l'unica cosa che questo controllo
        /// rifiuta.
        /// </summary>
        private static string ConfrontaCopia(
            IModelDoc2 modello,
            double volumePrima, int corpiPrima, int faccePrima,
            Dictionary<string, object> misure,
            string cosaEra)
        {
            double volumeDopo; int corpiDopo, facceDopo;
            Fotografia(modello, out volumeDopo, out corpiDopo, out facceDopo);

            misure["volume_prima_mm3"] = Math.Round(volumePrima, 2);
            misure["volume_dopo_mm3"] = Math.Round(volumeDopo, 2);
            misure["volume_cambiato_mm3"] = Math.Round(volumeDopo - volumePrima, 2);
            misure["facce_prima"] = faccePrima;
            misure["facce_dopo"] = facceDopo;
            misure["corpi"] = corpiDopo;

            bool volumeFermo = Math.Abs(volumeDopo - volumePrima) < 0.01;
            if (volumeFermo && facceDopo == faccePrima && corpiDopo == corpiPrima)
                return
                    cosaEra + " dice di essere riuscito, ma il pezzo e' identico a prima: " +
                    "stesso volume (" + Math.Round(volumeDopo, 2) + " mm3), stesse " +
                    facceDopo + " facce, stessi " + corpiDopo + " corpi. Non e' stato copiato niente. " +
                    "Di solito vuol dire che la lavorazione indicata non e' quella che credi, " +
                    "oppure che la copia sarebbe caduta esattamente dove c'e' gia' l'originale.";

            return null;
        }

        /// <summary>
        /// Quante facce POSSIEDE una lavorazione.
        ///
        /// Non e' il conto delle facce del pezzo: GetFaces() su una feature
        /// da' solo quelle che quella feature ha creato. E' la differenza
        /// che permette di contare le copie senza che il materiale gia'
        /// presente c'entri niente.
        /// </summary>
        private static int FacceDellaFeature(IFeature f)
        {
            if (f == null) return 0;
            try
            {
                var facce = f.GetFaces() as object[];
                return facce == null ? 0 : facce.Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// La lavorazione appena selezionata con quel contrassegno.
        ///
        /// Serve PRIMA di creare la ripetizione, perche' costruirla
        /// azzera la selezione. Chi la chiama deve accettare un null: se
        /// il seme non si ritrova non si conta niente, e non si rifiuta.
        /// </summary>
        private static IFeature SemeSelezionato(IModelDoc2 modello, int marca)
        {
            try
            {
                var sel = modello.SelectionManager as ISelectionMgr;
                if (sel == null) return null;
                int quanti = sel.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= quanti; i++)
                    if (sel.GetSelectedObjectMark(i) == marca)
                        return sel.GetSelectedObject6(i, -1) as IFeature;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// LE COPIE CHE NON CI SONO.
        ///
        /// MISURATO il 14 settembre 2026 su due pezzi gemelli: blocco
        /// 200x100x20, foro Ø10 a x=-60, ripetizione di 4 lungo lo spigolo E1.
        ///
        ///   verso preso dallo spigolo -> la ripetizione possiede 1 faccia
        ///   con «capovolgi_1»         -> la ripetizione possiede 3 facce
        ///
        /// Il seme ne possiede 1. Quindi UNA copia contro TRE, a parita' di
        /// richiesta. E IN TUTT'E DUE I CASI SOLIDWORKS HA TACIUTO:
        /// GetErrorCode2 ha dato 0, IsWarning false, SkippedItemArray null.
        /// Due istanze cadevano fuori dal materiale, lui ha costruito quello
        /// che poteva e ha risposto «riuscito». Chi chiedeva quattro fori ne
        /// otteneva due, e nessuno glielo diceva: e' esattamente il difetto
        /// che non da' errore.
        ///
        /// QUANDO NON SI SA, NON SI RIFIUTA. Se il seme non si ritrova, se
        /// una delle due non ha facce, o se il conto non e' divisibile, il
        /// numero non si scrive e l'operazione passa. Copie che si fondono
        /// fra loro lasciano un resto, ed e' un caso legittimo: rifiutare
        /// cio' che e' soltanto insolito e' un difetto peggiore che non
        /// controllare affatto.
        ///
        /// Non si rifiuta nemmeno se le copie sono PIU' di quelle chieste:
        /// vorrebbe dire che questo modo di contare e' sbagliato, e su un
        /// conto che si e' appena rivelato sbagliato non si nega niente.
        /// </summary>
        private static string CopieMancanti(
            Dictionary<string, object> misure, IFeature ripetizione, IFeature seme,
            int copieChieste, string cosaEra, string comeSiRipara)
        {
            if (copieChieste < 1) return null;

            int facceRipetizione = FacceDellaFeature(ripetizione);
            int facceSeme = FacceDellaFeature(seme);
            if (facceRipetizione <= 0 || facceSeme <= 0) return null;
            if (facceRipetizione % facceSeme != 0) return null;

            int fatte = facceRipetizione / facceSeme;
            misure["copie_chieste"] = copieChieste;
            misure["copie_fatte"] = fatte;
            if (fatte >= copieChieste) return null;

            return
                cosaEra + " dice di essere riuscita, ma le copie non ci sono tutte: " +
                "ne ho contate " + fatte + " delle " + copieChieste + " chieste. " +
                "Le conto dalle facce che la ripetizione possiede (" + facceRipetizione +
                ") divise per quelle della lavorazione ripetuta (" + facceSeme + "). " +
                "SOLIDWORKS qui non segnala niente: costruisce quello che puo' e " +
                "dice riuscito. Di solito le copie mancanti cadono fuori dal " +
                "materiale" + (fatte > 1 ? ", oppure si sono fuse fra loro" : "") + ". " +
                comeSiRipara +
                " La ripetizione e' rimasta nel pezzo: ritirala — " +
                Frasi.CosiSiRitira + ".";
        }

        /// <summary>
        /// Quante facce possiede ogni lavorazione dell'albero, per nome.
        ///
        /// Serve a vedere se una modifica di quota ha cambiato la NATURA di
        /// una lavorazione, non solo le sue misure. Il conto delle facce del
        /// pezzo intero non basta: una tasca che diventa passante toglie una
        /// faccia al pezzo, ma cosi' fanno anche altre dieci cose.
        ///
        /// C'e' un tetto, e non e' prudenza generica: l'albero costa circa
        /// 200 ms a feature (misurato), quindi su un pezzo con centinaia di
        /// lavorazioni questa lettura da sola durerebbe piu' dell'operazione.
        /// Oltre il tetto si smette e si riporta quello che si e' visto.
        /// </summary>
        private static Dictionary<string, int> FaccePerLavorazione(IModelDoc2 modello)
        {
            var fuori = new Dictionary<string, int>();
            try
            {
                var f = modello.FirstFeature() as IFeature;
                int viste = 0;
                while (f != null && viste < 200)
                {
                    int quante = FacceDellaFeature(f);
                    if (quante > 0)
                    {
                        viste++;
                        try { fuori[f.Name] = quante; } catch { }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>
        /// Cosa e' cambiato nell'albero, in una frase per chi legge.
        /// Null se non e' cambiato niente di topologico.
        ///
        /// SI RIPORTA, NON SI RIFIUTA. Assottigliare una piastra finche' una
        /// tasca diventa passante puo' essere esattamente cio' che si vuole:
        /// rifiutarlo sarebbe rifiutare cio' che e' soltanto insolito. Ma una
        /// sede cieca e un passaggio sono due pezzi diversi, e chi ha chiesto
        /// la modifica deve poterlo leggere invece di scoprirlo in officina.
        /// </summary>
        private static string LavorazioniCambiate(
            Dictionary<string, int> prima, Dictionary<string, int> dopo,
            Dictionary<string, object> misure)
        {
            var perse = new List<string>();
            var prese = new List<string>();
            var sparite = new List<string>();

            foreach (var voce in prima)
            {
                int ora;
                if (!dopo.TryGetValue(voce.Key, out ora)) { sparite.Add(voce.Key); continue; }
                if (ora < voce.Value) perse.Add(voce.Key + " " + voce.Value + " -> " + ora);
                else if (ora > voce.Value) prese.Add(voce.Key + " " + voce.Value + " -> " + ora);
            }

            if (perse.Count == 0 && prese.Count == 0 && sparite.Count == 0) return null;

            if (perse.Count > 0) misure["lavorazioni_con_meno_facce"] = perse.ToArray();
            if (prese.Count > 0) misure["lavorazioni_con_piu_facce"] = prese.ToArray();
            if (sparite.Count > 0) misure["lavorazioni_sparite"] = sparite.ToArray();

            var righe = new List<string>();
            if (sparite.Count > 0)
                righe.Add("non hanno piu' nessuna faccia: " + string.Join(", ", sparite));
            if (perse.Count > 0)
                righe.Add("hanno perso facce: " + string.Join(", ", perse) +
                          ". Una lavorazione cieca che perde una faccia di solito e' " +
                          "diventata PASSANTE: il fondo non c'e' piu'");
            if (prese.Count > 0)
                righe.Add("hanno preso facce: " + string.Join(", ", prese));

            return "La quota nuova ha cambiato l'albero, non solo le misure: " +
                   string.Join("; ", righe) + ".";
        }

        private static int ContaCorpiSolidi(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return 0;
            try
            {
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                return corpi == null ? 0 : corpi.Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Il volume di tutti i corpi solidi, in mm3. Zero se non si legge.
        ///
        /// L'INDICE 3 E' MISURATO, NON RICORDATO. `IBody2.GetVolume` non
        /// esiste (e' fra le sette API che sembravano giuste); l'alternativa
        /// vera e' `GetMassProperties(densita)`, che restituisce un vettore.
        /// Il 9 settembre 2026, misurando i fori guidati, l'elemento 3 e'
        /// risultato il volume in metri cubi: un foro M8 su una piastra da 20
        /// toglieva 726,3 mm3, cioe' pi*3,4^2*20 al decimo di millimetro cubo,
        /// e cosi' per tutte e undici le taglie da M3 a M24. Su un vettore
        /// letto male quei numeri non tornerebbero.
        /// </summary>
        private static double VolumeMm3(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return 0;
            double v = 0;
            try
            {
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null) return 0;
                foreach (var c in corpi)
                {
                    var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                    if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
                }
            }
            catch { return 0; }
            return v;
        }

        /// <summary>
        /// Seleziona gli spigoli indicati dal piano, per riferimento a un passo
        /// oppure per identificativo dello stato.
        ///
        /// STA IN UN METODO SOLO PERCHE' DUE COPIE DIVERGONO.
        /// Raccordo e smusso nominano gli spigoli nello stesso identico modo, e
        /// tutti i controlli che contano stanno qui: che il passo citato esista,
        /// che l'identificativo sia ancora quello (gli uid sono posizionali e si
        /// riassegnano a ogni lettura), e che la misura chiesta ci stia sulla
        /// lunghezza dello spigolo. Scritti due volte, il giorno in cui si
        /// corregge uno l'altro resta indietro.
        ///
        /// E LE DUE STRADE DENTRO QUESTO METODO ERANO GIA' DIVERGENTI.
        /// Il metodo era uno, ma aveva due rami — per uid e per riferimento a
        /// un passo — e il secondo usciva con `return true` prima di arrivare
        /// al controllo sulla misura. Bastava un metodo solo per credersi al
        /// sicuro: il commento qui sopra prometteva un controllo che meta' del
        /// metodo non faceva. Misurato il 14/9/2026, riparato nella 0.25.2.
        /// Mettere due strade sotto lo stesso commento non e' meno pericoloso
        /// che mettere due copie sotto due nomi.
        /// </summary>
        /// <param name="misuraMassima">
        /// Raggio o lato: non puo' superare meta' dello spigolo. Zero = non
        /// controllare.
        /// </param>
        private bool SelezionaSpigoli(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato,
            double misuraMassima, out int quanti, out string motivo)
        {
            quanti = 0;
            motivo = null;
            var uid = Lista(a, "spigoli_uid");

            // ── Spigoli nominati per riferimento a un passo precedente ──
            //
            // «Arrotonda i quattro angoli della piastra che fai al passo 1»:
            // quando il piano e' stato scritto quegli spigoli non esistevano.
            // Adesso esistono, e il selettore li trova.
            if (HaRiferimento(a, "spigoli_rif"))
            {
                int numero;
                var passo = PassoCitato(a, "spigoli_rif", out numero, out motivo);
                if (passo == null)
                {
                    motivo = motivo ?? "Riferimento non risolto.";
                    return false;
                }

                List<IEntity> trovati;
                if (!Riferimenti.Spigoli(passo, GenereDi(a, "spigoli_rif"), out trovati, out motivo))
                    return false;

                // LA MISURA SI CONTROLLA ANCHE QUI, e fino alla 0.25.2 no.
                //
                // Il commento di questo metodo prometteva che «tutti i
                // controlli che contano stanno qui», e fra quelli elencava che
                // la misura chiesta ci stia sulla lunghezza dello spigolo. Ma
                // questo ramo usciva con `return true` PRIMA di arrivarci: lo
                // stesso raccordo da 30 mm su uno spigolo da 10 veniva
                // rifiutato con `spigoli_uid` e lasciato passare con
                // `spigoli_rif`, fino a FeatureFillet3 che fallisce dicendo
                // soltanto che non e' stato creato.
                //
                // Qui non c'e' una firma da cui leggere la misura — gli spigoli
                // arrivano dal passo, non dallo stato — quindi si misurano
                // adesso, che e' anche il momento giusto: fra la proposta e
                // l'esecuzione il pezzo puo' essere cambiato.
                if (misuraMassima > 0)
                {
                    double corto = 0;
                    foreach (var e in trovati)
                    {
                        double lung = StatoDocumento.LunghezzaMm(e as IEdge);
                        if (lung <= 0) continue;          // non misurabile: non si inventa un verdetto
                        if (misuraMassima <= lung / 2) continue;
                        if (corto == 0 || lung < corto) corto = lung;
                    }
                    if (corto > 0)
                    {
                        motivo = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0:0.##} mm e' troppo per gli spigoli del passo {1}: il piu' corto " +
                            "e' lungo {2:0.##} mm, e non ci sta neanche meta'. " +
                            "Il massimo che ci sta e' {3:0.##} mm.",
                            misuraMassima, numero, corto, corto / 2);
                        return false;
                    }
                }

                modello.ClearSelection2(true);
                foreach (var e in trovati) e.Select4(true, null);

                // IL NUMERO VERO LO DICE SOLIDWORKS, NON IL CONTEGGIO NOSTRO.
                // La stessa entita' incontrata su due facce si seleziona una
                // volta sola: `trovati.Count` sarebbe piu' alto del reale, e
                // finirebbe nel messaggio che l'utente legge dopo.
                try
                {
                    var selezione = modello.SelectionManager as ISelectionMgr;
                    if (selezione != null) quanti = selezione.GetSelectedObjectCount2(-1);
                }
                catch { }
                if (quanti == 0)
                {
                    motivo = "Gli spigoli del passo " + numero +
                             " non si sono lasciati selezionare.";
                    return false;
                }
                _log("[agente] " + quanti + " spigoli presi dal passo " + numero);
                return true;
            }

            if (uid.Count == 0)
            {
                motivo = "Nessuno spigolo indicato.";
                return false;
            }

            modello.ClearSelection2(true);
            int selezionati = 0;
            foreach (var u in uid)
            {
                if (stato == null || !stato.Firme.TryGetValue(u, out var firma) ||
                    firma.Tipo != "EDGE")
                {
                    motivo = "Lo spigolo «" + u + "» non c'e' piu' in questo documento.";
                    return false;
                }
                // Anche gli spigoli sono numerati per posizione, ordinati per
                // lunghezza: dopo un passo che cambia il corpo, «E7» e' un
                // altro spigolo. Stesso controllo delle facce.
                if (!FirmaCombacia(a, u, firma, out motivo)) return false;
                var spigolo = firma.Entita as IEntity;
                if (spigolo == null)
                {
                    motivo = "Lo spigolo «" + u + "» non e' piu' selezionabile.";
                    return false;
                }

                // La misura non puo' superare meta' dello spigolo: il controllo
                // si rifa' QUI, sulla lunghezza misurata adesso, perche' fra la
                // proposta e l'esecuzione il pezzo puo' essere cambiato.
                if (misuraMassima > 0 && firma.Misura > 0 && misuraMassima > firma.Misura / 2)
                {
                    motivo = string.Format(
                        "{0:0.##} mm e' troppo per lo spigolo «{1}», lungo {2:0.##} mm: " +
                        "non ci sta neanche meta'.",
                        misuraMassima, u, firma.Misura);
                    return false;
                }

                if (!spigolo.Select4(true, null))
                {
                    motivo = "Non sono riuscito a selezionare lo spigolo «" + u + "».";
                    return false;
                }
                selezionati++;
            }

            quanti = selezionati;
            return true;
        }

        /// <summary>Il raccordo vero e proprio, su cio' che e' gia' selezionato.</summary>
        private Esito EseguiRaccordo(IModelDoc2 modello, double raggio, int quantiSpigoli)
        {
            var opzioni = (int)(swFeatureFilletOptions_e.swFeatureFilletUniformRadius |
                                swFeatureFilletOptions_e.swFeatureFilletPropagate);
            var feature = modello.FeatureManager.FeatureFillet3(
                Options: opzioni,
                R1: Unita.M(raggio),
                R2: 0,
                Rho: 0,
                Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
                OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                Radii: null,
                Dist2Arr: null,
                RhoArr: null,
                SetBackDistances: null,
                PointRadiusArray: null,
                PointDist2Array: null,
                PointRhoArray: null) as IFeature;

            if (feature == null) return Fallito("Il raccordo non e' stato creato.");
            return Riuscito(feature, new Dictionary<string, object>
            {
                ["raggio_mm"] = raggio,
                ["spigoli"] = quantiSpigoli,
            });
        }

        private Esito RipetiCircolare(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double volumePrima; int corpiPrima, faccePrima;
            Fotografia(modello, out volumePrima, out corpiPrima, out faccePrima);

            int istanze = (int)Num(a, "numero_istanze");
            double angolo = Num(a, "angolo_totale_gradi", 360);
            string featureUid = Str(a, "feature_uid", "");

            modello.ClearSelection2(true);
            // L'asse va selezionato per PRIMO con Mark = 1: la ripetizione
            // circolare prende la direzione dalla selezione, non dai parametri.
            string perche;
            if (!SelezionaAsse(modello, a, "asse", stato, out perche)) return Fallito(perche);

            // La lavorazione da ripetere puo' essere quella fatta da un passo
            // precedente di questo stesso piano — «fai un foro e ripetilo 6
            // volte». In quel caso si seleziona l'OGGETTO, non il nome: il
            // nome che SolidWorks le ha dato lo sapremmo solo rileggendolo, e
            // sarebbe un giro in piu' per arrivare alla stessa feature che
            // gia' abbiamo in mano.
            if (HaRiferimento(a, "feature_rif"))
            {
                int numero;
                var precedente = PassoCitato(a, "feature_rif", out numero, out perche);
                if (precedente == null) return Fallito(perche ?? "Riferimento non risolto.");
                if (precedente.Feature == null)
                    return Fallito($"Il passo {numero} non ha lasciato una lavorazione da ripetere.");
                if (!precedente.Feature.Select2(true, 4))
                    return Fallito($"Non sono riuscito a selezionare la lavorazione del passo {numero}.");
                _log($"[agente] ripeto la lavorazione del passo {numero}");
            }
            else if (!modello.Extension.SelectByID2(featureUid, "BODYFEATURE", 0, 0, 0, true, 4, null, 0))
            {
                return Fallito($"Non ho trovato la lavorazione «{featureUid}» da ripetere.");
            }

            var seme = SemeSelezionato(modello, 4);

            var feature = modello.FeatureManager.FeatureCircularPattern5(
                Number: istanze,
                Spacing: Unita.Rad(angolo),
                FlipDirection: false,
                DName: "",
                GeometryPattern: false,
                EqualSpacing: true,
                VaryInstance: false,
                SyncSubAssemblies: false,
                BDir2: false,
                BSymmetric: false,
                Number2: 1,
                Spacing2: 0,
                DName2: "",
                EqualSpacing2: false) as IFeature;

            if (feature == null) return Fallito("La ripetizione non e' stata creata.");
            var misure = new Dictionary<string, object>
            {
                ["istanze"] = istanze,
                ["angolo_gradi"] = angolo,
            };
            var fermo = ConfrontaCopia(
                modello, volumePrima, corpiPrima, faccePrima, misure,
                "La ripetizione circolare");
            if (fermo != null) return Fallito(fermo);

            var mancanti = CopieMancanti(
                misure, feature, seme, istanze - 1, "La ripetizione circolare",
                "Con un angolo minore del giro intero le copie si fermano dove " +
                "finisce l'arco: controlla «angolo_totale_gradi», oppure porta " +
                "le istanze a quante ce ne stanno.");
            if (mancanti != null) return Fallito(mancanti);

            return Riuscito(feature, misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // Mattoni comuni
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lo schizzo appena chiuso, SELEZIONATO, pronto da estrudere o tagliare.
        ///
        /// PERCHE' SERVE
        /// `InsertSketch(false)` esce dallo schizzo ma NON lo lascia
        /// selezionato, e FeatureExtrusion3 lavora su cio' che e' selezionato:
        /// senza questo passaggio la feature non nasce e basta. E' successo il
        /// 7 settembre 2026 alla prima estrusione vera: nell'albero restava
        /// «(-) Schizzo1» non consumato e il piano si fermava con «l'estrusione
        /// non e' stata creata», senza altra spiegazione. I parametri erano
        /// giusti: mancava la selezione.
        ///
        /// Si cerca l'ultima feature di tipo ProfileFeature — cioe' l'ultimo
        /// schizzo dell'albero, che e' quello appena creato — e NON per nome:
        /// i nomi delle feature sono tradotti («Schizzo1» contro «Sketch1») e
        /// una SelectByID2 con il nome sbagliato risponde false in silenzio.
        /// </summary>
        private static IFeature SchizzoAppenaCreato(IModelDoc2 modello)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return null;
            IFeature ultimo = null;
            try
            {
                var f = parte.IFirstFeature();
                while (f != null)
                {
                    try
                    {
                        if (f.GetTypeName2() == "ProfileFeature") ultimo = f;
                    }
                    catch { }
                    f = f.IGetNextFeature();
                }
            }
            catch { return null; }
            return ultimo;
        }

        /// <summary>Seleziona lo schizzo appena chiuso. False se non ci riesce.</summary>
        private bool SelezionaSchizzoAppenaCreato(IModelDoc2 modello, out string errore)
        {
            errore = null;
            var schizzo = SchizzoAppenaCreato(modello);
            if (schizzo == null)
            {
                errore = "Non ritrovo lo schizzo che ho appena creato.";
                return false;
            }
            try
            {
                modello.ClearSelection2(true);
                if (!schizzo.Select2(false, 0))
                {
                    errore = "Non sono riuscito a selezionare lo schizzo appena creato.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errore = "Selezione dello schizzo non riuscita: " + ex.Message;
                return false;
            }
            return true;
        }

        /// <summary>
        /// TOGLIE LO SCHIZZO QUANDO LA FEATURE NON E' NATA.
        ///
        /// IL DIFETTO DA CUI NASCE. L'11 settembre 2026, otto richieste oltre i
        /// limiti sulla scatola rettangolare: sette rifiuti giusti, zero fallimenti
        /// silenziosi — e sette schizzi orfani nel pezzo. Ogni rifiuto diceva
        /// «puoi toglierlo con Ctrl+Z», che e' onesto e non e' una riparazione:
        /// e' un compito dato a chi legge, e si moltiplica per il numero di
        /// tentativi.
        ///
        /// PERCHE' ADESSO SI PUO' FARE, E PRIMA NO. La regola in testa a questo
        /// file — non prendere iniziative dentro il documento di qualcuno —
        /// resta valida per le feature che hanno TOLTO o AGGIUNTO materiale:
        /// quelle si annullano con Ctrl+Z e basta (vedi
        /// <see cref="RestaNelPezzo"/>). Uno schizzo che non ha prodotto niente
        /// e' un caso diverso: non e' una modifica del pezzo, e' un rifiuto
        /// nostro rimasto per terra. Toglierlo non e' iniziativa, e' pulizia.
        ///
        /// QUATTRO CONTROLLI PRIMA DI CANCELLARE, PERCHE' CANCELLARE LA FEATURE
        /// SBAGLIATA E' MOLTO PEGGIO CHE LASCIARE UNO SCHIZZO.
        ///
        ///   1. E' UNO SCHIZZO? Non si guarda il nome del tipo — i nomi sono
        ///      tradotti, ed e' l'errore che questo file evita da sempre. Si
        ///      chiede l'interfaccia specifica: `IFeature.GetSpecificFeature2`
        ///      torna ISketch per uno schizzo 2D e per uno 3D, e torna NULL per
        ///      estrusioni, tagli, raccordi, smussi e lofts. E' scritto nella
        ///      guida API 2021, voce GetSpecificFeature2: «For some feature
        ///      types, this method returns Nothing or null because there is no
        ///      specific object for that type of feature (e.g., features such
        ///      as extrusions, lofts, fillets, chamfers, etc.)». Quindi: se la
        ///      feature in fondo all'albero e' quella che volevamo creare, qui
        ///      si esce.
        ///
        ///      E POI SI ESCLUDE L'ORIGINE, che e' la trappola misurata il 14
        ///      settembre 2026 camminando l'albero di una vasca: l'origine del
        ///      pezzo e' di tipo `OriginProfileFeature` e a
        ///      GetSpecificFeature2 risponde **ISketch anche lei**. Su un
        ///      documento vuoto e' l'ultima feature dell'albero. Il tipo si
        ///      guarda per RIFIUTARE, mai per accettare: se il nome del tipo
        ///      fosse diverso da quello che ci aspettiamo restano in piedi
        ///      tutti gli altri controlli.
        ///
        ///   2. STA IN FONDO ALL'ALBERO? Si guarda l'ULTIMA feature, non
        ///      «l'ultimo schizzo». E qui c'e' il secondo fatto misurato il 14
        ///      settembre 2026, che e' il contrario di quello che sembra: uno
        ///      schizzo ASSORBITO resta comunque nel giro di primo livello di
        ///      `IFirstFeature`/`GetNextFeature`. Su una vasca fatta con una
        ///      estrusione sola l'albero e': ... `#18 ProfileFeature Schizzo1`,
        ///      `#19 Extrusion Estrusione1`. Cioe' un'estrusione riuscita
        ///      aggiunge DUE feature al conto, non una — e lo schizzo e'
        ///      penultimo, non ultimo. E' esattamente il motivo per cui il
        ///      controllo e' «l'ULTIMA e' uno schizzo» e non «esiste uno
        ///      schizzo in coda»: quando la feature nasce, dopo lo schizzo c'e'
        ///      lei.
        ///
        ///   3. L'HO FATTO IO ADESSO? L'albero dev'essere CRESCIUTO da quando
        ///      il passo e' cominciato (<see cref="_featureAllInizioDelPasso"/>).
        ///      Se non e' cresciuto, lo schizzo in fondo c'era prima: e' di
        ///      qualcun altro e non si tocca.
        ///
        ///   4. DIPENDE QUALCOSA DA LUI? `IGetChildCount` dev'essere zero, ed
        ///      e' il controllo che pesa di piu' dei quattro. Sempre il 14
        ///      settembre: lo schizzo assorbito da un'estrusione riuscita ha
        ///      **1 figlio**, che e' l'estrusione. Un orfano di un fallimento
        ///      non ha nessuno che lo usa. Quindi questo numero non e' una
        ///      cautela generica: e' la differenza fra «consumato» e
        ///      «abbandonato», chiesta a SolidWorks invece che dedotta.
        ///
        /// DeleteSelection2 E NON EditDelete, E IL MOTIVO E' UNA TRAPPOLA NOTA.
        /// La guida API 2021, nelle Remarks di IModelDoc2::EditDelete, dice:
        /// «Use IModelDocExtension::DeleteSelection2 to turn off prompting the
        /// user to confirm the deletion». EditDelete apre una finestra di
        /// conferma: dentro l'add-in, che gira NEL processo di SolidWorks, la
        /// chiamata COM non tornerebbe mai e SolidWorks resterebbe piantato —
        /// e' esattamente quello che ha fatto AddDimension con
        /// swInputDimValOnCreate acceso, dieci minuti fermo, misurati l'11
        /// settembre 2026. DeleteSelection2 ritorna un bool e non chiede
        /// niente a nessuno. L'opzione e' swDelete_Absorbed: le quote dello
        /// schizzo sono assorbite da lui e devono andarsene con lui.
        /// swDelete_Children NON si passa: se un figlio ci fosse, il controllo
        /// 4 ha gia' rinunciato, e passare il flag vorrebbe dire cancellare di
        /// piu' proprio nel caso che non abbiamo capito.
        ///
        /// E DOPO SI MISURA. Volume, numero di corpi e numero di feature: il
        /// primo e il secondo devono essere identici, il terzo dev'essere
        /// calato di UNO. Se non torna, si dice <see cref="SchizzoToltoMaNonSolo"/>,
        /// che e' la sola risposta onesta: <see cref="LasciatoSchizzo"/> a quel
        /// punto sarebbe una bugia che copre un danno.
        /// </summary>
        /// <returns>
        /// Stringa vuota se lo schizzo e' stato tolto e il pezzo e' quello di
        /// prima — non c'e' niente da aggiungere al motivo del fallimento;
        /// <see cref="LasciatoSchizzo"/> se non si e' potuto toglierlo;
        /// <see cref="SchizzoToltoMaNonSolo"/> se toglierlo ha cambiato il pezzo.
        /// </returns>
        // ══════════════════════════════════════════════════════════════════
        // R2 — TOGLIERE una lavorazione dall'albero
        //
        // Fino alla 0.33.0 le trentasei operazioni erano tutte additive o
        // parametriche: l'albero poteva solo CRESCERE. L'unica correzione era
        // `modifica_quota` — si cambia un numero, non si ritira una scelta — e
        // `annulla` (R1), che pero' disfa l'ULTIMO piano intero e non una
        // lavorazione in mezzo.
        //
        // Queste due tolgono una lavorazione qualunque: `sopprimi_lavorazione`
        // la spegne (si riaccende), `elimina_lavorazione` la cancella.
        //
        // ── LA PARTE DIFFICILE E' IL RIFIUTO, come per `annulla` ───────────
        //
        // **Le lavorazioni hanno figli.** Togliere l'estrusione di base fa
        // crollare tutto quello che ci sta sopra, e SOLIDWORKS lo fa **senza
        // chiedere**. La guardia `IGetChildCount()` esisteva gia' per lo
        // schizzo orfano (`SenzaLoSchizzo`, punto 4); qui e' estesa, e quando
        // ci sono figli **si rifiuta dicendo quali** — non si sceglie per conto
        // di chi disegna.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>La lavorazione che porta quel nome, o null.</summary>
        private static IFeature TrovaLavorazione(IModelDoc2 modello, string uid)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return null;
            try
            {
                var f = parte.IFirstFeature();
                while (f != null)
                {
                    string nome = null;
                    try { nome = f.Name; } catch { }
                    if (string.Equals(nome, uid, StringComparison.OrdinalIgnoreCase)) return f;
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// I nomi di cio' che dipende da questa lavorazione.
        ///
        /// Servono a SCRIVERLI nel rifiuto. «Non posso, ha dei figli» manda chi
        /// legge a cercarli a mano; «non posso, ci stanno sopra Taglio1 e
        /// Raccordo2» gli dice cosa deve togliere prima, o gli fa capire che ha
        /// sbagliato lavorazione.
        /// </summary>
        private static List<string> FigliDi(IFeature f)
        {
            var nomi = new List<string>();
            try
            {
                var figli = f.GetChildren() as object[];
                if (figli == null) return nomi;
                foreach (var g in figli)
                {
                    var figlio = g as IFeature;
                    if (figlio == null) continue;
                    try { nomi.Add(figlio.Name); } catch { }
                }
            }
            catch { }
            return nomi;
        }

        /// <summary>
        /// I nomi delle lavorazioni che ci sono, per un rifiuto che aiuta
        /// invece di constatare. Corto: dodici bastano a far capire la forma
        /// dei nomi.
        /// </summary>
        private static string LavorazioniCheCiSono(IModelDoc2 modello)
        {
            var nomi = new List<string>();
            var parte = modello as IPartDoc;
            if (parte == null) return "";
            try
            {
                var f = parte.IFirstFeature();
                while (f != null && nomi.Count < 12)
                {
                    string nome = null;
                    try { nome = f.Name; } catch { }
                    if (!string.IsNullOrEmpty(nome)) nomi.Add(nome);
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return string.Join(", ", nomi);
        }

        /// <summary>
        /// La lavorazione nominata, con tutti i controlli che valgono per
        /// tutte e due le operazioni. Null col motivo se non si puo'.
        /// </summary>
        private IFeature LavorazioneDaToccare(
            IModelDoc2 modello, Dictionary<string, object> a, out string motivo)
        {
            motivo = null;
            string uid = Str(a, "feature_uid", "");
            if (string.IsNullOrWhiteSpace(uid))
            {
                motivo = "Non hai detto quale lavorazione. I nomi stanno nello " +
                         "stato del documento, dentro «feature» (per esempio " +
                         "«Taglio-Estrusione2»).";
                return null;
            }

            // E' ANCORA QUELLA? Un nome si rinomina, `GetID()` no. Stessa
            // difesa di `ripeti_lineare` e `specchia`, e qui pesa di piu':
            // la' si copia una lavorazione sbagliata, qui la si cancella.
            if (!LavorazioneEQuella(modello, uid, a, out motivo)) return null;

            var f = TrovaLavorazione(modello, uid);
            if (f == null)
            {
                string ci = LavorazioniCheCiSono(modello);
                motivo = "Non trovo la lavorazione «" + uid + "» in questo documento." +
                         (ci.Length > 0 ? " Ci sono: " + ci + "." : "");
                return null;
            }

            // L'ORIGINE NON SI TOCCA. E' uno schizzo speciale da cui puo'
            // dipendere tutto, e `GetChildren` non sempre lo racconta.
            string tipo = "";
            try { tipo = f.GetTypeName2(); } catch { }
            if (tipo == OrigineDelPezzo)
            {
                motivo = "«" + uid + "» e' l'origine del pezzo: non e' una " +
                         "lavorazione e togliendola non resta niente.";
                return null;
            }
            return f;
        }

        /// <summary>
        /// SPEGNE una lavorazione, o la riaccende.
        ///
        /// ── PERCHE' SOPPRIMERE E NON CANCELLARE ───────────────────────────
        ///
        /// La soppressione **si disfa**: e' la strada da prendere quando si
        /// vuole vedere il pezzo senza quella lavorazione senza impegnarsi.
        /// Lo stato la mostra gia' — `soppressa: true` — dalla 0.25.2, e quel
        /// campo esisteva prima che ci fosse un modo di metterlo: era un
        /// display senza interruttore.
        ///
        /// ── GLI IDENTIFICATIVI CAMBIANO, E VA DETTO ───────────────────────
        ///
        /// Sopprimere una lavorazione fa sparire le sue facce, e gli uid sono
        /// **posizionali**: si rinumerano per area a ogni lettura. Misurato
        /// nella misura 0.1: sopprimendo la lavorazione generatrice sono
        /// sparite due facce su otto. Chi ha in mano un piano scritto su «F3»
        /// dopo questo non ha piu' «F3»: la risposta lo dice, e non lo lascia
        /// scoprire al passo dopo.
        ///
        /// **I figli non fermano la soppressione, ma si dicono.** Sopprimere un
        /// padre spegne anche i figli, ed e' cio' che di solito si vuole («togli
        /// il foro e i suoi raccordi»). Diverso da `elimina_lavorazione`, dove
        /// i figli fermano tutto: li' non si torna indietro.
        /// </summary>
        private Esito SopprimiLavorazione(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string motivo;
            var f = LavorazioneDaToccare(modello, a, out motivo);
            if (f == null) return Fallito(motivo);

            string uid = Str(a, "feature_uid", "");
            bool riattiva = Bool(a, "riattiva", false);

            bool eraSoppressa = false;
            try { eraSoppressa = f.IsSuppressed(); } catch { }
            if (eraSoppressa == !riattiva)
            {
                // GIA' COM'E' CHIESTA: non e' un errore, ed e' importante non
                // trattarlo come tale — un piano che la chiede due volte non ha
                // rotto niente. Ma non si dice «fatto» e basta, perche' allora
                // «fatto» non distinguerebbe piu' un cambiamento da un nulla.
                return Fallito(
                    "«" + uid + "» e' gia' " +
                    (eraSoppressa ? "soppressa" : "attiva") +
                    ": non ho cambiato niente. Se volevi il contrario, " +
                    (eraSoppressa
                        ? "passa «riattiva»: true."
                        : "togli «riattiva»."));
            }

            double volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            var figli = FigliDi(f);

            // IN TUTTE LE CONFIGURAZIONI, per la stessa ragione di
            // `modifica_quota`: sopprimere in una sola lascerebbe il pezzo
            // diverso a seconda di come lo si guarda.
            const int inTutteLeConfigurazioni = 2;
            int azione = riattiva
                ? (int)swFeatureSuppressionAction_e.swUnSuppressFeature
                : (int)swFeatureSuppressionAction_e.swSuppressFeature;

            bool fatto;
            try { fatto = f.SetSuppression2(azione, inTutteLeConfigurazioni, null); }
            catch (Exception ex)
            {
                return Fallito("SOLIDWORKS non ha " +
                               (riattiva ? "riattivato" : "soppresso") +
                               " «" + uid + "»: " + ex.Message);
            }
            if (!fatto)
                return Fallito("SOLIDWORKS non ha " +
                               (riattiva ? "riattivato" : "soppresso") +
                               " «" + uid + "», e non ha detto perche'.");

            try { modello.EditRebuild3(); } catch { }

            double volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);

            var misure = new Dictionary<string, object>
            {
                ["lavorazione"] = uid,
                ["soppressa"] = !riattiva,
                ["volume_prima_mm3"] = Math.Round(volumePrima, 2),
                ["volume_dopo_mm3"] = Math.Round(volumeDopo, 2),
                ["corpi"] = corpiDopo,
                // GLI IDENTIFICATIVI NON VALGONO PIU', e si dice sempre: e' il
                // difetto piu' caro del progetto — un'operazione giusta fatta
                // alla cieca — e qui la cecita' e' garantita.
                ["nota_identificativi"] =
                    "Le facce sono cambiate: gli identificativi (F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo " +
                    "seguente.",
            };
            if (corpiDopo != corpiPrima)
                misure["nota_corpi"] =
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo + ".";
            if (figli.Count > 0)
                misure["nota_figli"] =
                    "Da «" + uid + "» dipendono " + string.Join(", ", figli) +
                    ": " + (riattiva ? "si sono riaccese con lei." : "si sono spente con lei.");

            _log("[agente] " + (riattiva ? "riattivata" : "soppressa") + " " + uid +
                 ", volume " + volumePrima.ToString("0.#") + " -> " + volumeDopo.ToString("0.#"));
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// CANCELLA una lavorazione. Non si torna indietro con un'altra
        /// operazione: solo con `annulla`.
        ///
        /// ── I FIGLI FERMANO TUTTO, E QUI SI' ──────────────────────────────
        ///
        /// In `sopprimi_lavorazione` i figli si spengono insieme al padre e si
        /// riaccendono insieme. Qui sparirebbero, e **SOLIDWORKS lo fa senza
        /// chiedere**. Quindi si rifiuta, dicendo quali sono: chi legge decide
        /// se toglierli prima o se aveva in mente un'altra lavorazione.
        ///
        /// E' la stessa guardia che `SenzaLoSchizzo` usa al punto 4, estesa da
        /// «lo schizzo che ho appena fatto io» a «una lavorazione qualunque».
        ///
        /// ── `DeleteSelection2` E NON `EditDelete` ─────────────────────────
        ///
        /// La documentazione lo dice, e il motivo e' una trappola nota:
        /// `EditDelete` apre la finestra di conferma, e una chiamata COM che
        /// apre un modale **non torna** — e' la stessa trappola di
        /// `AddDimension`. `DeleteSelection2` risponde un bool e non chiede
        /// niente a nessuno.
        ///
        /// `swDelete_Absorbed` toglie anche lo schizzo assorbito: senza, resta
        /// nell'albero uno schizzo orfano che non fa niente e confonde chi
        /// legge lo stato.
        /// </summary>
        private Esito EliminaLavorazione(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string motivo;
            var f = LavorazioneDaToccare(modello, a, out motivo);
            if (f == null) return Fallito(motivo);

            string uid = Str(a, "feature_uid", "");

            var figli = FigliDi(f);
            int quantiFigli = 0;
            try { quantiFigli = f.IGetChildCount(); } catch { quantiFigli = figli.Count; }
            if (quantiFigli > 0)
                return Fallito(
                    "Non elimino «" + uid + "»: da lei " +
                    (quantiFigli == 1 ? "dipende" : "dipendono") + " " +
                    (figli.Count > 0
                        ? string.Join(", ", figli)
                        : quantiFigli + " lavorazion" + (quantiFigli == 1 ? "e" : "i")) +
                    ". Cancellandola sparirebbe" + (quantiFigli == 1 ? "" : "ro") +
                    " anche quell" + (quantiFigli == 1 ? "a" : "e") +
                    ", e SOLIDWORKS lo farebbe senza chiedere.\n\n" +
                    "Se e' proprio lei che vuoi togliere, togli prima cio' che " +
                    "ci sta sopra. Se ti serve solo vedere il pezzo senza, usa " +
                    "«sopprimi_lavorazione»: spegne anche i figli e si disfa.");

            double volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            int featurePrima = ContaFeature(modello);

            bool tolta;
            try
            {
                modello.ClearSelection2(true);
                if (!f.Select2(false, 0))
                    return Fallito("Non sono riuscito a selezionare «" + uid + "».");
                tolta = modello.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                return Fallito("SOLIDWORKS non ha cancellato «" + uid + "»: " + ex.Message);
            }
            finally
            {
                try { modello.ClearSelection2(true); } catch { }
            }

            if (!tolta)
                return Fallito("SOLIDWORKS non ha cancellato «" + uid + "», e non ha " +
                               "detto perche'. Il pezzo e' rimasto com'era.");

            try { modello.EditRebuild3(); } catch { }

            // ── E ADESSO SI CONTROLLA CHE SIA DAVVERO SPARITA ────────────
            //
            // `DeleteSelection2` ha risposto true. Non basta: la domanda e' se
            // quel nome nell'albero non c'e' piu'. E' la differenza fra «ha
            // detto di averlo fatto» e «l'ha fatto» — la stessa distanza che
            // questo progetto insegue da cinquanta voci.
            if (TrovaLavorazione(modello, uid) != null)
                return Fallito(
                    "Ho chiesto di cancellare «" + uid + "», SOLIDWORKS ha " +
                    "risposto di si', e la lavorazione e' ancora nell'albero. " +
                    "Non so dirti cos'e' successo: guarda il pezzo prima di " +
                    "fare altro.");

            int featureDopo = ContaFeature(modello);
            double volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);

            var misure = new Dictionary<string, object>
            {
                ["lavorazione_tolta"] = uid,
                ["volume_prima_mm3"] = Math.Round(volumePrima, 2),
                ["volume_dopo_mm3"] = Math.Round(volumeDopo, 2),
                ["lavorazioni_prima"] = featurePrima,
                ["lavorazioni_dopo"] = featureDopo,
                ["corpi"] = corpiDopo,
                ["nota_identificativi"] =
                    "Le facce sono cambiate: gli identificativi (F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo " +
                    "seguente.",
            };
            if (corpiDopo != corpiPrima)
                misure["nota_corpi"] =
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": togliere questa lavorazione ha spezzato o unito il pezzo.";

            _log("[agente] eliminata " + uid + ", lavorazioni " + featurePrima +
                 " -> " + featureDopo + ", volume " + volumePrima.ToString("0.#") +
                 " -> " + volumeDopo.ToString("0.#"));
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Un successo che NON ha creato niente.
        ///
        /// `Riuscito` scrive nel log «creata feature X» e porta la feature
        /// nuova nell'esito, perche' le trentasei operazioni che c'erano prima
        /// creavano tutte qualcosa. Togliere no — e un'operazione che dice
        /// «creata feature» mentre ne cancella una lascia nel registro una
        /// frase falsa.
        ///
        /// **Nome nuovo invece di un parametro in piu' su `Riuscito`:**
        /// cambiare l'insieme delle firme rompe chi chiama per riflessione
        /// anche quando il compilatore tace. E' la lezione della 0.25.1, e
        /// `Riuscito` e' il punto da cui passa OGNI successo del progetto —
        /// il posto peggiore in cui provarla.
        ///
        /// I riancoraggi si drenano lo stesso: se nel trovare la lavorazione
        /// una faccia e' stata ritrovata altrove, chi legge deve saperlo anche
        /// qui.
        /// </summary>
        private Esito RiuscitoSenzaCreare(Dictionary<string, object> misure)
        {
            if (_riancoraggi.Count > 0)
            {
                if (misure == null) misure = new Dictionary<string, object>();
                misure["riancoraggi"] = _riancoraggi.ToArray();
            }
            return new Esito
            {
                Riuscito = true,
                FeatureCreata = null,
                Feature = null,
                Misure = misure,
            };
        }

        private string SenzaLoSchizzo(IModelDoc2 modello)
        {
            if (modello == null) return LasciatoSchizzo;

            // ── 2. l'ultima feature dell'albero, qualunque sia ──────────
            IFeature ultima = UltimaFeature(modello);
            if (ultima == null) return LasciatoSchizzo;

            // ── 1. e dev'essere uno schizzo, e NON l'origine ────────────
            ISketch schizzo = null;
            try { schizzo = ultima.GetSpecificFeature2() as ISketch; } catch { }
            if (schizzo == null) return LasciatoSchizzo;
            string tipo = "";
            try { tipo = ultima.GetTypeName2(); } catch { }
            if (tipo == OrigineDelPezzo) return LasciatoSchizzo;

            // ── 3. e dev'essere nato in questo passo ────────────────────
            int featureAdesso = ContaFeature(modello);
            if (featureAdesso <= _featureAllInizioDelPasso) return LasciatoSchizzo;

            // ── 4. e non deve avere figli ───────────────────────────────
            try { if (ultima.IGetChildCount() > 0) return LasciatoSchizzo; }
            catch { return LasciatoSchizzo; }

            // Cio' che NON deve cambiare, letto prima di toccare niente.
            double volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);

            string nome = "";
            try { nome = ultima.Name; } catch { }

            bool tolto;
            try
            {
                modello.ClearSelection2(true);
                if (!ultima.Select2(false, 0)) return LasciatoSchizzo;
                tolto = modello.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                _log("[agente] non sono riuscito a togliere lo schizzo: " + ex.Message);
                return LasciatoSchizzo;
            }
            finally
            {
                try { modello.ClearSelection2(true); } catch { }
            }

            if (!tolto) return LasciatoSchizzo;

            // ── e adesso si conta ───────────────────────────────────────
            int featureDopo = ContaFeature(modello);
            double volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            if (featureDopo != featureAdesso - 1
                || corpiDopo != corpiPrima
                || Math.Abs(volumeDopo - volumePrima) > 1e-6)
            {
                _log(string.Format(
                    "[agente] tolto lo schizzo «{0}» ma il pezzo e' cambiato: " +
                    "feature {1}->{2}, corpi {3}->{4}, volume {5:0.###}->{6:0.###} mm3",
                    nome, featureAdesso, featureDopo, corpiPrima, corpiDopo,
                    volumePrima, volumeDopo));
                return SchizzoToltoMaNonSolo;
            }

            _log("[agente] tolto lo schizzo «" + nome + "», che la feature non ha usato");
            return "";
        }

        private IFeature Estrudi(IModelDoc2 modello, double spessoreMm, bool unisci, bool simmetrico = false)
        {
            // Simmetrico = meta' per parte del piano. Va detto nel riassunto
            // perche' cambia dove finisce il pezzo: «100 simmetrica» sono 50 da
            // una parte e 50 dall'altra, e chi legge «100» puo' intendere
            // tutt'altro. swEndCondMidPlane = 6, verificato nell'enum.
            return modello.FeatureManager.FeatureExtrusion3(
                Sd: true,                       // una direzione sola
                Flip: false,
                Dir: false,
                T1: simmetrico
                    ? (int)swEndConditions_e.swEndCondMidPlane
                    : (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: Unita.M(spessoreMm),
                D2: 0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0, Dang2: 0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                Merge: unisci,
                UseFeatScope: true,
                UseAutoSelect: true,
                T0: (int)swStartConditions_e.swStartSketchPlane,
                StartOffset: 0,
                FlipStartOffset: false) as IFeature;
        }

        private IFeature Taglia(IModelDoc2 modello, bool passante, double profonditaMm)
        {
            return modello.FeatureManager.FeatureCut4(
                Sd: true,
                Flip: false,
                Dir: false,
                T1: passante
                    ? (int)swEndConditions_e.swEndCondThroughAll
                    : (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: passante ? 0 : Unita.M(profonditaMm),
                D2: 0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0, Dang2: 0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                NormalCut: false,
                UseFeatScope: true,
                UseAutoSelect: true,
                AssemblyFeatureScope: false,
                AutoSelectComponents: false,
                PropagateFeatureToParts: false,
                T0: (int)swStartConditions_e.swStartSketchPlane,
                StartOffset: 0,
                FlipStartOffset: false,
                OptimizeGeometry: true) as IFeature;
        }

        /// <summary>
        /// Seleziona uno dei tre piani base SENZA usarne il nome.
        ///
        /// In un'installazione italiana si chiamano Frontale/Superiore/Destro,
        /// in una inglese Front/Top/Right: un SelectByID2("Front Plane", ...)
        /// risponde False in silenzio e la feature a valle si costruisce su una
        /// selezione vuota. I piani base sono le prime tre feature di tipo
        /// RefPlane dell'albero, in ordine fisso, e quello non cambia con la
        /// lingua.
        /// </summary>
        private bool SelezionaPiano(IModelDoc2 modello, string quale)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return false;
            int voluto = quale == "superiore" ? 2 : quale == "destro" ? 3 : 1;

            int visti = 0;
            IFeature f = parte.IFirstFeature();
            while (f != null)
            {
                string tipo = "";
                try { tipo = f.GetTypeName2(); } catch { }
                if (tipo == "RefPlane")
                {
                    visti++;
                    if (visti == voluto)
                    {
                        modello.ClearSelection2(true);
                        return f.Select2(false, 0);
                    }
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { return false; }
            }
            return false;
        }

        private bool SelezionaFaccia(IFace2 faccia)
        {
            var entita = faccia as IEntity;
            if (entita == null) return false;
            return entita.Select4(false, null);
        }

        /// <summary>
        /// Quanto puo' essersi spostato il punto di riferimento di un'entita'
        /// fra la proposta e l'esecuzione. Deve combaciare con
        /// TOLLERANZA_PUNTO_MM del server: sono lo stesso controllo fatto due
        /// volte, e due tolleranze diverse vorrebbero dire che una delle due
        /// parti accetta cio' che l'altra rifiuta.
        /// </summary>
        private const double TolleranzaPuntoMm = 0.05;
        private const double TolleranzaNormale = 0.01;

        /// <summary>
        /// Trova la faccia indicata dal passo, E VERIFICA CHE SIA ANCORA QUELLA.
        ///
        /// PERCHE' NON BASTA CERCARE L'UID
        /// Gli uid sono POSIZIONALI: StatoDocumento li riassegna a ogni lettura
        /// enumerando le facce per area decrescente, e l'esecutore rilegge il
        /// documento prima di ogni passo. Il piano invece e' congelato al
        /// momento della conferma. Se il passo 1 aggiunge materiale, al passo 2
        /// «F1» e' un'altra faccia: la lavorazione riesce, il rebuild e'
        /// pulito, l'esito dice «riuscito» e il pezzo e' sbagliato.
        ///
        /// Per questo il piano porta con se' la FIRMA che la faccia aveva
        /// quando il modello l'ha scelta, e qui si riconfronta. Il commento in
        /// testa a StatoDocumento prometteva questo controllo da sempre; fino
        /// alla 0.24.0 non esisteva in nessun punto del codice.
        ///
        /// Si confronta il punto e la normale, non l'area: l'area cambia in
        /// modo legittimo quando sulla faccia si fa un foro.
        /// </summary>
        private IFace2 RisolviFaccia(
            StatoDocumento stato,
            Dictionary<string, object> argomenti,
            string uid,
            out string motivo)
        {
            motivo = null;

            // ── La faccia puo' venire da un passo precedente del piano ──
            //
            // Sta qui, e non nelle due operazioni di taglio, perche' qui e'
            // l'unico posto da cui passano tutte e due: un foro e una tasca
            // devono poter nominare la stessa faccia nello stesso modo, e due
            // copie di questa logica sarebbero divergenti alla prima modifica.
            //
            // Il riferimento ha la precedenza sull'uid: il server garantisce
            // che non arrivino tutti e due, ma se arrivassero comunque e'
            // meglio fallire su un uid che non c'e' che lavorare in silenzio
            // sulla faccia sbagliata.
            if (HaRiferimento(argomenti, "faccia_rif"))
            {
                int numero;
                var passo = PassoCitato(argomenti, "faccia_rif", out numero, out motivo);
                if (passo == null) return null;
                IFace2 dalPasso;
                if (!Riferimenti.Faccia(
                        passo, GenereDi(argomenti, "faccia_rif"), out dalPasso, out motivo))
                    return null;
                _log($"[agente] faccia presa dal passo {numero}");
                return dalPasso;
            }

            if (stato == null || string.IsNullOrEmpty(uid))
            {
                motivo =
                    "Nessuna faccia indicata. Se la faccia la crea un passo " +
                    "precedente di questo piano, va nominata con faccia_rif.";
                return null;
            }
            if (!stato.Firme.TryGetValue(uid, out var firma) || firma.Tipo != "FACE")
            {
                motivo = $"La faccia «{uid}» non c'e' piu' in questo documento.";
                return null;
            }
            var faccia = firma.Entita as IFace2;
            if (faccia == null)
            {
                motivo = $"La faccia «{uid}» non e' piu' utilizzabile.";
                return null;
            }
            if (FirmaCombacia(argomenti, uid, firma, out motivo)) return faccia;

            // ── IL PERSIST ID PROPONE, LA MISURA GIUDICA (voce E6) ──────────
            //
            // La firma non combacia. Prima di cercare alla cieca si chiede a
            // SOLIDWORKS: il riferimento persistente e' l'unica ancora che non
            // sia una nostra congettura sulla geometria.
            //
            // MAI DA SOLO. `GetObjectByPersistReference3` puo' restituire SENZA
            // ERRORE una faccia diversa dopo un rebuild che cambia la topologia:
            // sarebbe il difetto peggiore di tutta la sezione, perche' non
            // darebbe errore. Quello che torna e' un CANDIDATO, e passa dallo
            // stesso giudice di tutti gli altri — `Corrobora`.
            //
            // MISURATO (0.1, 14/9/2026): 8 facce su 8 ritrovate in tutte e
            // cinque le modifiche riuscite, comprese le tre in cui l'ancora sul
            // punto si perde. Dove ha ceduto — la soppressione della
            // lavorazione generatrice — ha ceduto in modo DISTINGUIBILE: le due
            // facce che avevano davvero smesso di esistere non tornano
            // (`ErrorCode = 1`) invece di tornare sbagliate.
            // E `IsSamePersistentID` non si usa: ha risposto vero 46 volte su
            // 46, anche dove l'area era raddoppiata perche' due facce si erano
            // fuse. Attesta l'identificativo, non la faccia.
            var attesaPersist = FirmaAttesa(argomenti, uid);
            if (attesaPersist != null)
            {
                string spostamento;
                var dalPersist = FacciaDalPersistId(stato, attesaPersist, uid, out spostamento);
                if (dalPersist != null)
                {
                    // SI SPIEGA DI QUANTO, INVECE DI RIFIUTARE. E' il motivo per
                    // cui questa voce esiste: la faccia e' quella giusta, si e'
                    // solo mossa, e chi legge deve sapere che si e' mossa.
                    Riancorata(spostamento);
                    motivo = null;
                    return dalPersist;
                }
            }

            // LA FIRMA NON COMBACIA: PRIMA DI RIFIUTARE, SI CERCA.
            //
            // Gli uid sono POSIZIONALI — le facce si rinumerano per area a
            // ogni lettura — quindi basta una lavorazione perche' «F13» non
            // sia piu' la faccia di prima. Fin qui il sistema se ne
            // accorgeva (la firma) e si fermava.
            //
            // Fermarsi era giusto e non bastava. Misurato il 10 settembre
            // 2026 disegnando il contenitore per l'antenna: «taglia quattro
            // asole uguali sulle quattro alette» — una richiesta
            // normalissima — non si poteva fare in un piano solo. La prima
            // asola cambiava le aree, le facce si rinumeravano, e dalla
            // seconda in poi il piano nominava facce sbagliate.
            //
            // La firma pero' non serve solo ad accorgersene: serve a
            // RITROVARE. Se nel documento c'e' UNA sola faccia che combacia
            // con quella che il piano descrive, e' quella — e usarla e'
            // piu' giusto che rifiutare. Se ce ne sono due, non si sceglie:
            // li' l'ambiguita' e' vera e va detta.
            var attesa = FirmaAttesa(argomenti, uid);
            if (attesa == null) return null;   // niente firma: resta il rifiuto di prima

            IFace2 ritrovata = null;
            StatoDocumento.FirmaEntita firmaRitrovata = null;
            int quante = 0;
            string scartataPerche = null;
            foreach (var voce in stato.Firme)
            {
                if (voce.Value.Tipo != "FACE") continue;
                var candidata = voce.Value.Entita as IFace2;
                if (candidata == null) continue;
                string perche;
                if (!FirmaCombacia(argomenti, uid, voce.Value, out perche)) continue;

                // COMBACIARE PER POSIZIONE NON BASTA. Vedi Corrobora.
                string cresciuta;
                string nonRegge = Corrobora(attesa, uid, voce.Value, out cresciuta);
                Riancorata(cresciuta);
                if (nonRegge != null)
                {
                    // Si tiene il PRIMO motivo di scarto per il messaggio: se
                    // alla fine non si ritrova niente, chi legge deve sapere
                    // che una candidata c'era e perche' non e' stata presa.
                    if (scartataPerche == null) scartataPerche = nonRegge;
                    continue;
                }

                quante++;
                ritrovata = candidata;
                firmaRitrovata = voce.Value;
            }

            if (quante == 1)
            {
                // SI DICE DI QUANTO, non solo CHE. Fino alla 0.29.0 questa
                // strada diceva «si era spostata: ritrovata dalla firma» e
                // basta — un fatto senza una misura, e per giunta solo su
                // stderr. La firma misurata e' gia' in mano (`firmaRitrovata`),
                // e la frase la scrive la stessa funzione che usa il persist
                // ID: due strade diverse per ritrovare, una sola per dirlo.
                Riancorata(
                    FraseDelloSpostamento(attesaPersist, uid, firmaRitrovata, null)
                    ?? "la faccia «" + uid + "» si era spostata ed e' stata ritrovata " +
                       "dalla firma misurata.");
                motivo = null;
                return ritrovata;
            }
            if (quante > 1)
            {
                motivo =
                    "La faccia «" + uid + "» si e' spostata e ce ne sono " + quante +
                    " che le somigliano tutte uguali: non scelgo io. Rileggi lo " +
                    "stato del documento e nominala di nuovo.";
            }
            else if (scartataPerche != null)
            {
                // Il caso che conta: UNA candidata c'era, combaciava per
                // posizione, e la corroborazione l'ha smentita. Prima veniva
                // presa e il pezzo usciva sbagliato senza che nessuno dicesse
                // niente.
                motivo = scartataPerche;
            }
            return null;
        }

        /// <summary>
        /// LA SECONDA PROVA, prima di dire «ritrovata».
        ///
        /// Il ritrovamento per firma esisteva gia' e serviva: gli uid sono
        /// posizionali, e senza di lui «taglia quattro asole uguali» non si
        /// poteva scrivere in un piano solo. Ma accettava la candidata
        /// confrontando **solo punto e normale**, e quello non basta.
        ///
        /// MISURATO (voce E2, 14/9/2026). Piastra 100x60x20 con una tasca 40x40
        /// profonda 8: il fondo sta a Z=12 con area 1600, la faccia in alto a
        /// Z=20 con area 4400. Si porta lo spessore a 12; il fondo scende a Z=4
        /// e **la faccia in alto scende a Z=12**, cioe' esattamente dove il
        /// piano cerca il fondo.
        ///
        /// Il punto combaciava. L'area era 4400 contro 1600 attesi — quasi il
        /// triplo — e veniva presa lo stesso: `riuscita: true`, ricostruzione
        /// pulita, e un mezzo foro profondo 2 al posto di un foro intero
        /// profondo 4. **Il punto aveva vinto sull'area.**
        ///
        /// DUE PROVE, E NESSUNA DELLE DUE E' «L'AREA DEVE COMBACIARE».
        ///
        /// 1. **La lavorazione dev'essere la stessa**, per identificativo e non
        ///    per nome (`IFeature.GetID()`). Una faccia che viene da un'altra
        ///    lavorazione non e' la stessa faccia, comunque sia messa.
        ///
        /// 2. **L'area puo' solo CALARE**, ma SOLO quando la prima prova non ha
        ///    gia' risposto. E' un vincolo monotono, non un confronto: ci si fa
        ///    un foro e la faccia perde area, ed e' legittimo — e' proprio il
        ///    motivo per cui l'area era stata esclusa dal confronto. Ma non ne
        ///    GUADAGNA: una faccia cresciuta e' un altro pezzo di pezzo.
        ///
        /// IL CONTROLLO SULL'AREA SI MORDEVA LA CODA, e riparato il 14/9/2026.
        ///
        /// Gli uid si assegnano per area decrescente. Una faccia che si allarga
        /// abbastanza da scavalcarne un'altra in classifica CAMBIA UID per
        /// forza: `FirmaCombacia` sull'uid vecchio fallisce, si passa alla
        /// ricerca, la faccia vera viene ritrovata — e veniva scartata proprio
        /// perche' era cresciuta. **La crescita dell'area era insieme la causa
        /// di questa ricerca e il motivo del suo fallimento.**
        ///
        /// Misurato: staffa a elle, aletta orizzontale 100x20 = 2000 mm² (F2) e
        /// verticale 80x20 = 1600 mm² (F3). Piano di due passi: `modifica_quota`
        /// porta la verticale da 80 a 120, poi `taglio_cilindrico` su «F3». Dopo
        /// il primo passo la verticale vale 2400 e diventa F2; la ricerca
        /// ritrova la faccia giusta — stesso piano, stesso punto, stessa
        /// normale, **stessa lavorazione** — e il controllo sull'area la
        /// buttava. Un rifiuto di lavoro legittimo.
        ///
        /// LA RIPARAZIONE non e' togliere il controllo: e' ordinarlo. Quando la
        /// prima prova ha ACCERTATO che la lavorazione e' la stessa — non
        /// «non lo so», proprio accertato — l'identita' e' gia' stabilita da un
        /// segnale piu' forte, e la crescita dell'area diventa una cosa da
        /// DIRE, non da rifiutare. Il vincolo monotono resta intero dove serve
        /// davvero: quando la lavorazione non si sa, che e' il caso E2.
        ///
        /// In E2 le due facce venivano da lavorazioni diverse (l'estrusione e
        /// il taglio), quindi la prima prova le separa e il difetto resta
        /// chiuso: si controlla in `Banco/casi/E2-i-due-casi.md`.
        ///
        /// PERCHE' QUI E NON DENTRO `FirmaCombacia`. Perche' quel confronto
        /// serve anche a decidere se la faccia di partenza va ancora bene, e
        /// li' l'area che cresce e' normale — si allarga il pezzo e la faccia
        /// in alto diventa piu' grande, senza che il punto si muova. La
        /// corroborazione vale **solo quando si sta gia' cercando un
        /// sostituto**, cioe' quando qualcosa si e' gia' mosso.
        ///
        /// Ritorna null se la candidata regge; il motivo del rifiuto se no —
        /// e il motivo da' **il criterio**, non il verdetto.
        /// </summary>
        /// <summary>
        /// La faccia che SOLIDWORKS ritrova dal riferimento persistente, se la
        /// ritrova E se regge al giudizio. Null in ogni altro caso — e null
        /// qui vuol dire «prova un'altra strada», non «rifiuta».
        ///
        /// TRE PORTE, E TUTTE E TRE DEVONO APRIRSI.
        ///
        ///  1. **Il piano deve portare un persist ID.** Le firme di prima della
        ///     0.26.0 non ce l'hanno, e un piano scritto ieri deve continuare a
        ///     funzionare: senza, si torna alla ricerca di sempre.
        ///  2. **SOLIDWORKS deve ritrovare qualcosa.** `ErrorCode` diverso da
        ///     zero, oppure un oggetto che non e' una faccia, vogliono dire che
        ///     quella faccia non esiste piu': e' la risposta giusta al caso
        ///     della lavorazione soppressa, e va lasciata passare alla ricerca
        ///     che sapra' dirlo meglio.
        ///  3. **Il giudizio deve reggere.** Il candidato passa da `Corrobora`
        ///     come qualunque altro: stessa lavorazione, e l'area che cresce
        ///     solo se la lavorazione e' accertata. Un persist ID che torna una
        ///     faccia di un'altra lavorazione e' esattamente il difetto
        ///     silenzioso che questa voce temeva, e qui si ferma.
        ///
        /// <param name="spostamento">
        /// Di quanto si e' mossa, in parole, quando si e' mossa. Va a chi legge:
        /// la faccia e' quella giusta e il pezzo NON e' piu' quello di prima,
        /// e tacerlo sarebbe lavorare bene su un pezzo cambiato senza dirlo.
        /// </param>
        /// </summary>
        private IFace2 FacciaDalPersistId(
            StatoDocumento stato, Dictionary<string, object> attesa, string uid,
            out string spostamento)
        {
            spostamento = null;
            if (stato == null || stato.Modello == null || attesa == null) return null;

            object grezzo;
            if (!attesa.TryGetValue("persist_id", out grezzo) || grezzo == null) return null;
            var base64 = Convert.ToString(grezzo);
            if (string.IsNullOrEmpty(base64)) return null;

            byte[] blob;
            try { blob = Convert.FromBase64String(base64); }
            catch { return null; }
            if (blob.Length == 0) return null;

            IFace2 candidata;
            try
            {
                var estensione = stato.Modello.Extension;
                if (estensione == null) return null;
                int errore;
                var oggetto = estensione.GetObjectByPersistReference3(blob, out errore);
                if (errore != 0 || oggetto == null) return null;
                candidata = oggetto as IFace2;
                if (candidata == null) return null;
            }
            catch { return null; }

            // IL GIUDIZIO. La firma si rimisura ADESSO sulla faccia ritrovata:
            // non si usa quella che il piano porta, che e' cio' che si sta
            // verificando.
            var firmaOra = StatoDocumento.FirmaDiUnaFaccia(candidata);
            if (firmaOra == null) return null;
            // `FirmaDiUnaFaccia` misura UNA faccia e non rilegge il documento:
            // la scatola del pezzo non ce l'ha, e senza quella la frase non si
            // puo' scrivere. Gliela passa chi il documento l'ha letto.
            firmaOra.ScatolaPezzoMm = stato?.ScatolaPezzoMm;

            string nota;
            if (Corrobora(attesa, uid, firmaOra, out nota) != null) return null;

            spostamento = FraseDelloSpostamento(attesa, uid, firmaOra, nota);
            return candidata;
        }

        /// <summary>
        /// Che cosa e' cambiato fra la firma che il piano portava e quella
        /// misurata adesso, in una frase. Null se non e' cambiato niente che
        /// valga la pena dire.
        ///
        /// SI DICE IL CRITERIO, NON IL VERDETTO: quanto si e' spostata e di
        /// quanto e' cambiata l'area, cosi' chi legge puo' decidere se il piano
        /// contava su quei numeri.
        /// </summary>
        private static string FraseDelloSpostamento(
            Dictionary<string, object> attesa, string uid,
            StatoDocumento.FirmaEntita ora, string notaCorrobora)
        {
            var pezzi = new List<string>();

            var primaPunto = Terna(attesa, "punto_mm");
            if (primaPunto != null && ora.PuntoMm != null && primaPunto.Length >= 3 &&
                ora.PuntoMm.Length >= 3)
            {
                double dx = ora.PuntoMm[0] - primaPunto[0];
                double dy = ora.PuntoMm[1] - primaPunto[1];
                double dz = ora.PuntoMm[2] - primaPunto[2];
                double quanto = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (quanto > 0.05)
                    pezzi.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "si e' spostata di {0:0.##} mm, da [{1:0.##}, {2:0.##}, {3:0.##}] " +
                        "a [{4:0.##}, {5:0.##}, {6:0.##}]",
                        quanto, primaPunto[0], primaPunto[1], primaPunto[2],
                        ora.PuntoMm[0], ora.PuntoMm[1], ora.PuntoMm[2]));
            }

            object grezzoArea;
            if (attesa.TryGetValue("area_mm2", out grezzoArea) && grezzoArea != null)
            {
                double primaArea;
                try
                {
                    primaArea = Convert.ToDouble(
                        grezzoArea, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { primaArea = 0; }
                if (primaArea > 0 && Math.Abs(ora.Misura - primaArea) > primaArea * 0.001)
                    pezzi.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "l'area e' passata da {0:0.##} a {1:0.##} mm²", primaArea, ora.Misura));
            }

            if (pezzi.Count == 0) return null;

            // LA SPIEGAZIONE, che qui e' il caso per cui la voce E10 esisteva:
            // la faccia si e' mossa di sessanta millimetri E sta dove stava
            // rispetto al pezzo. Dirlo aiuta a capire; deciderlo no — e infatti
            // qui non si decide niente: la faccia l'ha gia' scelta il
            // riferimento persistente, e il giudizio l'ha gia' dato Corrobora.
            return "la faccia «" + uid + "» l'ha ritrovata SOLIDWORKS dal suo " +
                   "riferimento persistente: " + string.Join(", ", pezzi) +
                   ". Il pezzo e' cambiato da quando l'hai guardato, ma la faccia e' " +
                   "quella giusta — se il piano contava sulle misure di prima, " +
                   "ricontrolla il risultato." +
                   Frasi.DoveStavaEDoveSta(
                       uid, primaPunto, ora.PuntoMm,
                       Sestina(attesa, "scatola_pezzo_mm"), ora.ScatolaPezzoMm) +
                   (notaCorrobora == null ? "" : " " + notaCorrobora);
        }

        private static string Corrobora(
            Dictionary<string, object> attesa, string uid,
            StatoDocumento.FirmaEntita candidata, out string nota)
        {
            nota = null;
            if (attesa == null) return null;

            // 1. LA STESSA LAVORAZIONE.
            // Solo se li conosciamo tutti e due: due «-1» non sono una
            // corrispondenza, sono due volte «non lo so».
            bool stessaLavorazioneAccertata = false;
            object grezzoId;
            if (attesa.TryGetValue("lavorazione_id", out grezzoId) && grezzoId != null &&
                candidata.LavorazioneId >= 0)
            {
                int atteso;
                try { atteso = Convert.ToInt32(grezzoId); } catch { atteso = -1; }
                if (atteso >= 0 && atteso == candidata.LavorazioneId)
                    stessaLavorazioneAccertata = true;
                if (atteso >= 0 && atteso != candidata.LavorazioneId)
                {
                    object nomeAtteso;
                    string daChi = attesa.TryGetValue("lavorazione", out nomeAtteso) &&
                                   nomeAtteso != null
                        ? "«" + Convert.ToString(nomeAtteso) + "»"
                        : "un'altra lavorazione";
                    return
                        "La faccia «" + uid + "» si e' spostata, e l'unica che combacia " +
                        "per posizione viene da «" + (candidata.Lavorazione ?? "?") +
                        "» invece che da " + daChi + ": non e' la stessa faccia, e' " +
                        "un'altra finita nello stesso posto. Rileggi lo stato del " +
                        "documento e dimmi tu quale.";
                }
            }

            // 2. L'AREA PUO' SOLO CALARE.
            object grezzoArea;
            if (attesa.TryGetValue("area_mm2", out grezzoArea) && grezzoArea != null)
            {
                double areaAttesa;
                try { areaAttesa = Convert.ToDouble(grezzoArea, System.Globalization.CultureInfo.InvariantCulture); }
                catch { return null; }
                // Un per mille di margine: le aree si arrotondano al
                // centesimo e un confronto secco accuserebbe l'arrotondamento.
                if (areaAttesa > 0 && candidata.Misura > areaAttesa * 1.001)
                {
                    // LA LAVORAZIONE ACCERTATA VINCE SULL'AREA.
                    // Se si sa con certezza che viene dalla stessa lavorazione,
                    // l'identita' e' gia' stabilita: una faccia puo' essere
                    // cresciuta perche' una quota e' cambiata, ed e' legittimo.
                    // Si DICE di quanto, e si va avanti.
                    if (stessaLavorazioneAccertata)
                    {
                        nota = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "La faccia «{0}» si era spostata ed e' anche CRESCIUTA: " +
                            "{1:0.##} mm² contro {2:0.##} che il piano si aspettava. " +
                            "L'ho presa lo stesso perche' viene dalla stessa lavorazione " +
                            "«{3}», ma se il piano contava sulla misura di prima, " +
                            "ricontrolla il risultato.",
                            uid, candidata.Misura, areaAttesa, candidata.Lavorazione ?? "?");
                        return null;
                    }

                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "La faccia «{0}» si e' spostata, e l'unica che combacia per " +
                        "posizione e' piu' GRANDE di quella che cercavo: {1:0.##} mm² " +
                        "contro {2:0.##}. Una lavorazione toglie area a una faccia, non " +
                        "gliene aggiunge: quella non e' la stessa faccia. Rileggi lo " +
                        "stato del documento e dimmi tu quale.",
                        uid, candidata.Misura, areaAttesa);
                }
            }

            return null;
        }

        /// <summary>
        /// La firma portata dal piano combacia con quella letta adesso?
        /// True anche quando il piano non porta nessuna firma: in quel caso non
        /// c'e' niente da confrontare e non si puo' inventare un rifiuto.
        /// </summary>
        private static bool FirmaCombacia(
            Dictionary<string, object> argomenti,
            string uid,
            StatoDocumento.FirmaEntita adesso,
            out string motivo)
        {
            motivo = null;
            var attesa = FirmaAttesa(argomenti, uid);
            if (attesa == null) return true;

            // DUE NOMI PER LA STESSA COSA, e non e' pigrizia.
            // Una faccia pubblica `punto_mm` — un punto SU di lei; un corpo
            // pubblica `centro_mm` — il centro della sua scatola. Sono due
            // misure diverse con lo stesso mestiere: dire DOVE sta l'entita'.
            // Chiamarle allo stesso modo nello stato sarebbe stato peggio (il
            // centro di un corpo non sta sul corpo, se il corpo e' a ferro di
            // cavallo), quindi qui si accettano tutti e due.
            var puntoAtteso = Terna(attesa, "punto_mm") ?? Terna(attesa, "centro_mm");
            if (puntoAtteso != null && adesso.PuntoMm != null && adesso.PuntoMm.Length == 3)
            {
                double scarto = 0;
                for (int i = 0; i < 3; i++)
                    scarto = Math.Max(scarto, Math.Abs(puntoAtteso[i] - adesso.PuntoMm[i]));
                if (scarto > TolleranzaPuntoMm)
                {
                    // LA PAROLA GIUSTA PER LA COSA GIUSTA. Lo stesso confronto
                    // serve a facce, spigoli e corpi, e dire «non lavoro sulla
                    // faccia sbagliata» di un corpo manda a cercare una faccia
                    // che non c'entra niente.
                    // LA SPIEGAZIONE SI ATTACCA DOPO IL VERDETTO, e il verdetto
                    // e' gia' preso: sopra, sui millimetri. Quello che segue e'
                    // una frase — non un numero, non un `bool` — e non puo'
                    // cambiare l'esito nemmeno se qualcuno lo volesse. Vedi
                    // Frasi.DoveStavaEDoveSta (voce E10).
                    motivo =
                        $"«{uid}» adesso indica un'altra parte del pezzo (spostato di " +
                        $"{scarto:0.##} mm). Il pezzo e' cambiato da quando hai " +
                        $"confermato: {NonLavoroSu(adesso)}" +
                        Frasi.DoveStavaEDoveSta(
                            uid, puntoAtteso, adesso.PuntoMm,
                            Sestina(attesa, "scatola_pezzo_mm"), adesso.ScatolaPezzoMm);
                    return false;
                }
            }

            var normaleAttesa = Terna(attesa, "normale");
            if (normaleAttesa != null && adesso.Normale != null && adesso.Normale.Length == 3)
            {
                double scarto = 0;
                for (int i = 0; i < 3; i++)
                    scarto = Math.Max(scarto, Math.Abs(normaleAttesa[i] - adesso.Normale[i]));
                if (scarto > TolleranzaNormale)
                {
                    motivo =
                        $"«{uid}» adesso e' orientata in un altro modo. Il pezzo e' " +
                        "cambiato da quando hai confermato.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// La frase di chiusura del rifiuto, con la parola giusta per il tipo
        /// di entita'. Tre parole invece di una, ed e' la differenza fra un
        /// messaggio che si capisce e uno che manda a cercare la cosa
        /// sbagliata.
        /// </summary>
        private static string NonLavoroSu(StatoDocumento.FirmaEntita entita)
        {
            switch (entita?.Tipo)
            {
                case "BODY": return "non lavoro sul corpo sbagliato.";
                case "EDGE": return "non lavoro sullo spigolo sbagliato.";
                default: return "non lavoro sulla faccia sbagliata.";
            }
        }

        private static Dictionary<string, object> FirmaAttesa(
            Dictionary<string, object> argomenti, string uid)
        {
            if (argomenti == null) return null;
            if (!argomenti.TryGetValue("_firme", out var tutte)) return null;
            var mappa = tutte as Dictionary<string, object>;
            if (mappa == null) return null;
            foreach (var voce in mappa)
            {
                if (string.Equals(voce.Key, uid, StringComparison.OrdinalIgnoreCase))
                    return voce.Value as Dictionary<string, object>;
            }
            return null;
        }

        /// <summary>Tre numeri da un campo del JSON, o null se non ci sono.</summary>
        private static double[] Terna(Dictionary<string, object> mappa, string chiave)
        {
            if (mappa == null || !mappa.TryGetValue(chiave, out var v) || v == null) return null;
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null) return null;
            var fuori = new List<double>();
            foreach (var x in elenco)
            {
                try { fuori.Add(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return fuori.Count == 3 ? fuori.ToArray() : null;
        }

        /// <summary>
        /// I sei numeri della scatola del pezzo da un campo del JSON
        /// (`{minX,minY,minZ,maxX,maxY,maxZ}`), o null se non ci sono.
        ///
        /// Serve solo a scrivere una frase: vedi
        /// <see cref="Frasi.DoveStavaEDoveSta"/>, dove c'e' anche il motivo per
        /// cui questi numeri non entrano in nessuna decisione.
        /// </summary>
        private static double[] Sestina(Dictionary<string, object> mappa, string chiave)
        {
            if (mappa == null || !mappa.TryGetValue(chiave, out var v) || v == null) return null;
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null) return null;
            var fuori = new List<double>();
            foreach (var x in elenco)
            {
                try { fuori.Add(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return fuori.Count == 6 ? fuori.ToArray() : null;
        }

        /// <summary>
        /// Quali assi del modello corrispondono a X e Y dello schizzo su questa
        /// faccia. Ritorna null se la faccia non e' piana e perpendicolare a un
        /// asse principale — caso in cui rifiutiamo invece di indovinare.
        /// </summary>
        private double[] AssiDellaFaccia(IFace2 faccia, out double offsetM, out string errore)
        {
            errore = null;
            offsetM = 0;
            try
            {
                var superficie = faccia.IGetSurface();
                if (superficie == null || !superficie.IsPlane())
                {
                    errore = "La faccia non e' piana: questa versione non ci lavora sopra.";
                    return null;
                }
                var piano = superficie.PlaneParams as double[];
                if (piano == null || piano.Length < 6)
                {
                    // Servono sei numeri: la normale e un punto sul piano. Con
                    // i soli tre della normale non si sa DOVE sta il piano, e
                    // la terza coordinata del punto finirebbe a zero — cioe'
                    // sul piano che passa per l'origine, non su questa faccia.
                    errore = "Non riesco a leggere la posizione della faccia.";
                    return null;
                }
                double ax = Math.Abs(piano[0]), ay = Math.Abs(piano[1]), az = Math.Abs(piano[2]);
                const double quasiUno = 0.999;
                // L'ultimo indice e' l'asse della NORMALE: da li' si prende
                // l'offset del piano, cioe' la coordinata che il modello non
                // deve dire perche' e' la faccia stessa a fissarla.
                if (az > quasiUno) { offsetM = piano[5]; return new double[] { 0, 1, 2 }; } // normale Z
                if (ay > quasiUno) { offsetM = piano[4]; return new double[] { 0, 2, 1 }; } // normale Y
                if (ax > quasiUno) { offsetM = piano[3]; return new double[] { 1, 2, 0 }; } // normale X
                errore =
                    "La faccia e' inclinata rispetto agli assi principali: in questa " +
                    "versione non ci faccio lavorazioni, perche' le coordinate " +
                    "finirebbero nel punto sbagliato senza dare errore.";
                return null;
            }
            catch (Exception ex)
            {
                errore = "Non riesco a leggere la faccia: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Da (x, y) del MODELLO a coordinate di schizzo, in metri.
        ///
        /// PERCHE' SERVE UNA CONVERSIONE VERA
        /// Fino alla 0.24.0 questo metodo riceveva la mappatura degli assi e la
        /// IGNORAVA: restituiva (x, y, 0) come se le coordinate del modello
        /// fossero gia' coordinate di schizzo. Non lo sono. Quando SolidWorks
        /// apre uno schizzo su una faccia sceglie da solo origine e verso degli
        /// assi in base alla normale: per due facce opposte l'asse Y risulta
        /// invertito, e un foro chiesto a (30, 20) usciva a (30, -20) — 40 mm
        /// piu' in la', con la feature verde e nessun avviso.
        ///
        /// La conversione non la indoviniamo noi: la chiediamo a SolidWorks.
        /// ISketch.ModelToSketchTransform E' la trasformazione che lui stesso
        /// ha usato per costruire lo schizzo. (Esiste nella 2021: verificato
        /// per riflessione sull'interop 29.5.1.1, insieme a
        /// IMathUtility.CreatePoint e IMathPoint.MultiplyTransform.)
        ///
        /// Va chiamato con lo schizzo GIA' APERTO: prima di InsertSketch(true)
        /// non c'e' nessuno schizzo attivo da cui leggere la trasformazione.
        /// </summary>
        private double[] PuntoNelloSchizzo(
            IModelDoc2 modello,
            double[] assi,
            double offsetM,
            double xMm,
            double yMm,
            out string errore)
        {
            errore = null;

            // 1. Il punto nel sistema del MODELLO, in metri. Le due coordinate
            //    che il modello linguistico ha dato vanno sui due assi liberi;
            //    la terza e' il piano della faccia, che lui non deve indovinare.
            var nelModello = new double[3];
            nelModello[(int)assi[0]] = Unita.M(xMm);
            nelModello[(int)assi[1]] = Unita.M(yMm);
            nelModello[(int)assi[2]] = offsetM;

            try
            {
                var schizzo = modello.SketchManager?.ActiveSketch;
                if (schizzo == null)
                {
                    errore = "Nessuno schizzo aperto: non posso posizionare la lavorazione.";
                    return null;
                }
                var trasformazione = schizzo.ModelToSketchTransform;
                if (trasformazione == null)
                {
                    errore = "SolidWorks non mi da' l'orientamento dello schizzo.";
                    return null;
                }
                var matematica = _sw.GetMathUtility() as IMathUtility;
                var punto = matematica?.CreatePoint(nelModello) as IMathPoint;
                var nelloSchizzo = punto?.MultiplyTransform(trasformazione) as IMathPoint;
                var fuori = nelloSchizzo?.ArrayData as double[];
                if (fuori == null || fuori.Length < 3)
                {
                    errore = "Non sono riuscito a convertire le coordinate nello schizzo.";
                    return null;
                }
                return new[] { fuori[0], fuori[1], fuori[2] };
            }
            catch (Exception ex)
            {
                errore = "Conversione delle coordinate non riuscita: " + ex.Message;
                return null;
            }
        }

        private Esito Riuscito(
            IFeature feature,
            Dictionary<string, object> misure,
            double[] direzione = null)
        {
            string nome = "";
            try { nome = feature.Name; } catch { }
            _log($"[agente] creata feature {nome}");

            // CIO' CHE SI E' MOSSO ARRIVA A CHI LEGGE (voce E8).
            //
            // Qui e' l'unico punto da cui passa OGNI successo di tutte e
            // trentasei le operazioni, comprese quelle nei file `partial`
            // (Fori, Saldatura, Tavola, Materiale). Drenare qui vuol dire che
            // nessuna operazione puo' dimenticarsene.
            //
            // IL DIFETTO CHE QUESTO CHIUDE, ed e' il piu' beffardo di tutti:
            // la 0.26.0 ha costruito il ritrovamento per persist ID e con esso
            // la frase che dice DI QUANTO la faccia si e' spostata — «si e'
            // spostata di 60 mm, da [50, 0, 0] a [110, 0, 0]». Poi l'ha
            // consegnata a `_log`, che in questo ponte finisce su
            // `Console.Error`. E in un server MCP su stdio **stderr non e' la
            // risposta**: il modello che ha scritto il piano e la persona che
            // l'ha confermato non la vedono mai.
            //
            // Risultato misurato: `e2_caso_a` esegue il foro su un pezzo lungo
            // il 60% in piu' di quello confermato, sulla faccia giusta, e nella
            // risposta c'e' `riuscita: true` e nient'altro. Il meccanismo
            // scritto apposta per non avere difetti silenziosi ne aveva uno
            // addosso: **aveva misurato lo spostamento e buttato via la
            // misura**.
            //
            // Il canale giusto esisteva gia' ed era vuoto: `Esito.Misure`
            // arriva dritto nel JSON della risposta, come fa `nota_volume`.
            if (_riancoraggi.Count > 0)
            {
                if (misure == null) misure = new Dictionary<string, object>();
                misure["riancoraggi"] = _riancoraggi.ToArray();
            }

            return new Esito
            {
                Riuscito = true,
                FeatureCreata = nome,
                Feature = feature,
                Direzione = direzione,
                Misure = misure,
            };
        }

        /// <summary>
        /// Le facce che sono state ritrovate altrove rispetto a dove il piano
        /// se le aspettava, con scritto di quanto si sono mosse.
        ///
        /// E' un CAMPO e non un parametro: nessuna firma di metodo cambia,
        /// quindi chi chiama per riflessione non se ne accorge. E' la regola
        /// che la 0.25.1 ha imparato a caro prezzo.
        ///
        /// Si svuota all'inizio di ogni operazione, non alla fine: un elenco
        /// che si svuota alla fine resta pieno se l'operazione fallisce, e la
        /// successiva erediterebbe gli spostamenti di quella prima.
        /// </summary>
        private readonly List<string> _riancoraggi = new List<string>();

        /// <summary>
        /// Registra che una faccia e' stata ritrovata altrove, e lo dice anche
        /// a stderr per chi guarda il ponte lavorare.
        /// </summary>
        private void Riancorata(string frase)
        {
            if (string.IsNullOrEmpty(frase)) return;
            _log("[agente] " + frase);
            _riancoraggi.Add(frase);
        }

        // ─────────────────────────────────────────────────────────────────
        // Riferimenti a passi precedenti
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Il passo citato da un riferimento, oppure null con il motivo.
        ///
        /// Il numero del passo lo ha gia' controllato il server mentre
        /// scriveva il piano; qui si ricontrolla lo stesso, perche' fra quel
        /// controllo e questa riga c'e' tutto il giro di rete, la conferma
        /// dell'utente, e i passi precedenti che possono essere falliti.
        /// </summary>
        /// <summary>
        /// C'e' davvero un riferimento in quel campo?
        ///
        /// GUARDA IL VALORE, NON LA CHIAVE, e la differenza e' costata un
        /// difetto. Un modello che vede due campi alternativi ne emette spesso
        /// tutti e due, riempiendo quello che non usa con `null`. Con
        /// `ContainsKey` un `faccia_rif: null` accanto a un `faccia_uid`
        /// perfettamente valido faceva prendere il ramo del riferimento,
        /// buttava via l'identificativo buono, e il passo moriva. Il server
        /// adesso quelle chiavi le toglie dal piano; questo controllo e' la
        /// seconda serratura, per i piani gia' salvati e per il giorno in cui
        /// qualcuno cambia il server.
        /// </summary>
        private static bool HaRiferimento(Dictionary<string, object> a, string campo)
        {
            object grezzo;
            return a != null && a.TryGetValue(campo, out grezzo) && grezzo != null;
        }

        private Riferimenti.Passo PassoCitato(
            Dictionary<string, object> a, string campo, out int numero, out string motivo)
        {
            numero = 0;
            // Mai null: un motivo nullo diventa `Fallito(null)`, e chi legge
            // trova «Mi sono fermato dopo 1 operazioni: » con il vuoto dopo i
            // due punti. Un fallimento senza ragione scritta e' un fallimento
            // che non si puo' correggere.
            motivo = $"Il riferimento «{campo}» non si e' potuto risolvere.";
            object grezzo;
            if (a == null || !a.TryGetValue(campo, out grezzo) || grezzo == null)
            {
                motivo =
                    $"Il piano dice di usare «{campo}», ma quel riferimento e' " +
                    "vuoto. Il passo non si puo' eseguire.";
                return null;
            }

            var mappa = grezzo as Dictionary<string, object>;
            if (mappa == null)
            {
                motivo = $"Il riferimento «{campo}» non e' leggibile.";
                return null;
            }
            object valore;
            if (!mappa.TryGetValue("passo", out valore))
            {
                motivo = $"Il riferimento «{campo}» non dice a quale passo.";
                return null;
            }
            try { numero = Convert.ToInt32(valore); }
            catch
            {
                motivo = $"Il riferimento «{campo}» ha un numero di passo illeggibile.";
                return null;
            }

            Riferimenti.Passo passo;
            if (!_passiFatti.TryGetValue(numero, out passo) || passo == null)
            {
                motivo =
                    $"Il riferimento «{campo}» cita il passo {numero}, ma di quel " +
                    "passo non ho la geometria: o non e' ancora stato eseguito, " +
                    "o non ha creato niente.";
                return null;
            }
            return passo;
        }

        /// <summary>
        /// La normale della faccia su cui si e' tagliato: e' la direzione del
        /// taglio, e serve a un passo successivo per sapere quale faccia della
        /// tasca e' il fondo. Null se la faccia non e' piana — in quel caso il
        /// passo dopo non potra' nominarne il fondo, e lo dira'.
        /// </summary>
        private static double[] NormaleDellaFaccia(IFace2 faccia)
        {
            try
            {
                var superficie = faccia?.GetSurface() as ISurface;
                if (superficie == null || !superficie.IsPlane()) return null;
                return faccia.Normal as double[];
            }
            catch { return null; }
        }

        private static string GenereDi(Dictionary<string, object> a, string campo)
        {
            object grezzo;
            if (a == null || !a.TryGetValue(campo, out grezzo)) return "";
            var mappa = grezzo as Dictionary<string, object>;
            if (mappa == null) return "";
            object g;
            return mappa.TryGetValue("genere", out g) ? Convert.ToString(g) ?? "" : "";
        }

        private Esito Fallito(string motivo)
        {
            // Un fallimento senza ragione scritta non si puo' correggere.
            // Succedeva davvero: un percorso ritornava il motivo a null, e chi
            // leggeva trovava «Mi sono fermato dopo 1 operazioni: » con il
            // vuoto dopo i due punti. Meglio una frase generica che il nulla.
            if (string.IsNullOrWhiteSpace(motivo))
                motivo =
                    "L'operazione non e' riuscita, e non ho una ragione da " +
                    "darti: e' un difetto mio. Segnalalo insieme al log.";
            _log("[agente] " + motivo);
            return new Esito { Riuscito = false, Errore = motivo };
        }

        // ─── lettura dei parametri, senza sorprese ───────────────────────
        private static double Num(Dictionary<string, object> a, string chiave, double se = 0)
        {
            if (a == null || !a.TryGetValue(chiave, out var v) || v == null) return se;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return se; }
        }

        /// <summary>
        /// Un numero che DEVE esserci. Fallisce invece di restituire zero.
        ///
        /// Num() sopra inghiotte ogni eccezione e restituisce il valore
        /// predefinito: comodo per «centrato» o «angolo», rovinoso per una
        /// coordinata. Uno zero al posto di un valore illeggibile non da'
        /// nessun errore: sposta il foro nell'origine e basta.
        /// </summary>
        private static bool NumObbligatorio(
            Dictionary<string, object> a, string chiave, out double valore, out string errore)
        {
            valore = 0;
            errore = null;
            object v = null;
            if (a == null || !a.TryGetValue(chiave, out v) || v == null)
            {
                errore = $"Manca il parametro «{chiave}».";
                return false;
            }
            try
            {
                valore = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                errore = $"«{chiave}» non e' un numero: {v}.";
                return false;
            }
            if (double.IsNaN(valore) || double.IsInfinity(valore))
            {
                errore = $"«{chiave}» non e' un numero utilizzabile: {v}.";
                return false;
            }
            return true;
        }

        private static bool Bool(Dictionary<string, object> a, string chiave, bool se)
        {
            if (a == null || !a.TryGetValue(chiave, out var v) || v == null) return se;
            try { return Convert.ToBoolean(v); }
            catch { return se; }
        }

        private static string Str(Dictionary<string, object> a, string chiave, string se)
        {
            if (a == null || !a.TryGetValue(chiave, out var v) || v == null) return se;
            return Convert.ToString(v) ?? se;
        }

        /// <summary>C'e' un elenco non vuoto sotto questa chiave?</summary>
        private static bool HaElenco(Dictionary<string, object> a, string chiave)
        {
            if (a == null || !a.TryGetValue(chiave, out var v) || v == null) return false;
            if (v is string) return false;
            var e = v as System.Collections.IEnumerable;
            if (e == null) return false;
            foreach (var x in e) if (x != null) return true;
            return false;
        }

        /// <summary>
        /// Le facce che un elenco di riferimenti a passi precedenti risolve.
        ///
        /// UN ELENCO DI RIFERIMENTI SINGOLI, e non un genere plurale. Il
        /// vocabolario di <see cref="Riferimenti"/> risolve una faccia per
        /// volta e rifiuta quando il ruolo non e' univoco — che e' la regola
        /// giusta e non va toccata. Per averne piu' d'una si ripete l'elemento:
        /// e' cio' che fa gia' chi scrive `facce_uid` con tre uid dentro.
        ///
        /// SE UNO SOLO NON SI RISOLVE, FALLISCE TUTTO. Un guscio aperto su due
        /// facce invece che su tre non e' «quasi giusto»: e' un pezzo diverso,
        /// e sarebbe riuscito in silenzio.
        /// </summary>
        private bool FacceDaiRiferimenti(
            Dictionary<string, object> a, string campo,
            out List<IFace2> facce, out string motivo)
        {
            facce = new List<IFace2>();
            motivo = null;

            object grezzo;
            if (a == null || !a.TryGetValue(campo, out grezzo) || grezzo == null)
            {
                motivo = "«" + campo + "» e' vuoto.";
                return false;
            }
            var elenco = grezzo as System.Collections.IEnumerable;
            if (elenco == null || grezzo is string)
            {
                motivo = "«" + campo + "» deve essere un elenco di riferimenti a passi.";
                return false;
            }

            int quale = 0;
            foreach (var v in elenco)
            {
                quale++;
                var voce = v as Dictionary<string, object>;
                if (voce == null)
                {
                    motivo = "Il riferimento n. " + quale + " di «" + campo +
                             "» non e' un oggetto {passo, genere}.";
                    return false;
                }

                // Si riusa `PassoCitato` costruendo un dizionario di un campo
                // solo: cosi' i controlli sul passo — che esista, che sia
                // precedente, che abbia creato qualcosa — restano quelli, e
                // non se ne scrive una seconda copia che divergera'.
                var uno = new Dictionary<string, object> { [campo] = voce };
                int numero;
                var passo = PassoCitato(uno, campo, out numero, out motivo);
                if (passo == null)
                {
                    motivo = "Riferimento n. " + quale + ": " +
                             (motivo ?? "non risolto.");
                    return false;
                }

                IFace2 faccia;
                if (!Riferimenti.Faccia(passo, GenereDi(uno, campo), out faccia, out motivo))
                {
                    motivo = "Riferimento n. " + quale + ": " +
                             (motivo ?? "faccia non trovata.");
                    return false;
                }
                facce.Add(faccia);
            }

            if (facce.Count == 0)
            {
                motivo = "«" + campo + "» non ha nessun riferimento.";
                return false;
            }
            return true;
        }

        private static List<string> Lista(Dictionary<string, object> a, string chiave)
        {
            var fuori = new List<string>();
            if (a == null || !a.TryGetValue(chiave, out var v) || v == null) return fuori;
            if (v is System.Collections.IEnumerable e && !(v is string))
            {
                foreach (var x in e) if (x != null) fuori.Add(Convert.ToString(x));
            }
            return fuori;
        }
    }
}
