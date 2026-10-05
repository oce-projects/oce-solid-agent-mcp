using System;

namespace Shared
{
    /// <summary>
    /// QUANDO SOLIDWORKS SE N'E' ANDATO, LE PROVE DOPO NON SONO ROSSE:
    /// NON SONO STATE FATTE (voce K17).
    ///
    /// ── QUANTO E' COSTATO NON AVERLA ─────────────────────────────────────
    ///
    /// Il 23/9/2026, giro delle 15:36. Dopo un'ora e mezza di lavoro pulito —
    /// 312 controlli verdi — SOLIDWORKS ha smesso di produrre documenti:
    /// `NewDocument` ha risposto `null`, e da li' in avanti ha risposto `null`
    /// a tutti. Il banco aveva gia' la voce F12, quindi ha DETTO cosa non era
    /// riuscito a preparare, per ognuno:
    ///
    ///     NO   detail_enlarges   SOLIDWORKS non ha prodotto la parte
    ///     NO   loft_is_not_a_cone      SOLIDWORKS non ha prodotto la parte
    ///     NO   plate_bounding_box     SOLIDWORKS non ha prodotto la parte
    ///     ...
    ///
    /// Nove casi rossi in fila, e poi il processo si e' piantato dentro la
    /// chiamata COM successiva — `Responding=False` sul processo di
    /// SOLIDWORKS, nessuna finestra modale aperta, titolo senza nome
    /// documento. Il banco e' rimasto li' a tempo indeterminato: non ha una
    /// scadenza sulle chiamate alla libreria, solo sul ponte.
    ///
    /// DUE DIFETTI, NON UNO:
    ///
    ///   1. Il resoconto diceva **rosso** dove il vero fatto era «non
    ///      misurato». Chi legge «detail_enlarges: NO» va a guardare
    ///      il caso — che era giusto — invece di guardare SOLIDWORKS.
    ///
    ///   2. Il giro **continuava**. Quaranta minuti di prove contro un
    ///      programma morto, e il verdetto finale sarebbe stato un elenco di
    ///      rossi che non dicono niente su niente.
    ///
    /// Il messaggio della F12 finiva gia' con «Guarda quanti documenti sono
    /// rimasti aperti, e rilancia». Era il rimedio giusto scritto nel posto
    /// sbagliato: **a un umano che forse non c'e'**. Qui il banco lo fa da se'.
    ///
    /// ── PERCHE' TRE, E PERCHE' DI FILA ───────────────────────────────────
    ///
    /// Una parte nuova che non nasce UNA volta puo' essere il pezzo: un
    /// modello di documento che manca, un disco pieno, un caso che ha lasciato
    /// SOLIDWORKS in un modo strano e il successivo si riprende. Tre di fila
    /// senza che in mezzo ne nasca una sola non e' piu' il pezzo: e' il
    /// programma.
    ///
    /// «Di fila» e' la parte che conta, ed e' per questo che
    /// <see cref="Succeeded"/> azzera. Un conto che non si azzera confonde tre
    /// fallimenti sparsi in ottocento controlli — che sono rumore normale —
    /// con tre fallimenti attaccati, che sono un programma che non c'e' piu'.
    ///
    /// ── PERCHE' STA QUI E NON NEL BANCO ──────────────────────────────────
    ///
    /// Perche' una guardia che nessuno ha mai visto scattare non e' una
    /// guardia. Per vederla scattare nel banco servirebbe far morire
    /// SOLIDWORKS a comando; in `Shared` la compila anche
    /// `Oce.Solid.Agent.ContractTests`, dove SOLIDWORKS non c'e' affatto, e li' la si fa
    /// fallire apposta ogni volta che si lanciano le prove — in un decimo di
    /// secondo, senza CAD.
    /// </summary>
    internal sealed class SolidWorksSentinel
    {
        private readonly int _threshold;
        private int _inARow;

        /// <summary>
        /// Quanti erano NEL MOMENTO IN CUI E' SCATTATA. Non e' la stessa cosa
        /// di <see cref="InARow"/>: quello e' un contatore vivo, e chi
        /// chiedesse il referto dopo che qualcosa ha chiamato
        /// <see cref="Succeeded"/> leggerebbe «0 tentativi di fila andati a
        /// vuoto» in un messaggio che annuncia una morte.
        ///
        /// E' successo il 23/9/2026, nella prova scritta per il referto: il
        /// controllo passava lo stesso perche' cercava la cifra «3», che nella
        /// frase c'era — era la SOGLIA. Un numero giusto nel posto sbagliato
        /// che copre un numero sbagliato: la prova adesso pretende «3
        /// tentativi», non «da qualche parte c'e' un 3».
        /// </summary>
        private int _whenItFires;

        /// <summary>
        /// Quanti fallimenti DI FILA prima di dire che il programma non c'e'
        /// piu'. Tre nel banco; il costruttore lo prende perche' le prove
        /// devono poter provare anche la soglia uno e la soglia cinque —
        /// altrimenti si proverebbe il numero tre, non la regola.
        /// </summary>
        internal SolidWorksSentinel(int threshold)
        {
            if (threshold < 1)
                throw new ArgumentOutOfRangeException(
                    "soglia",
                    "una sentinella che scatta dopo zero fallimenti scatta prima "
                    + "di guardare: la soglia e' almeno 1.");
            _threshold = threshold;
        }

        /// <summary>Vero quando il programma va considerato morto. Non torna
        /// piu' falso: da qui in poi non si misura piu' niente, si dice cosa
        /// non si e' misurato.</summary>
        internal bool HasGone { get; private set; }

        /// <summary>Quanti fallimenti di fila in questo momento. Serve al
        /// messaggio: «tre di fila» e' un fatto, «e' morto» e' una
        /// conclusione, e chi legge ha diritto al fatto.</summary>
        internal int InARow { get { return _inARow; } }

        /// <summary>La soglia con cui e' stata costruita, per il messaggio.</summary>
        internal int Threshold { get { return _threshold; } }

        /// <summary>Un documento e' nato: il conto riparte da zero.</summary>
        internal void Succeeded()
        {
            _inARow = 0;
        }

        /// <summary>
        /// Un documento non e' nato. Torna vero quando questo e' il fallimento
        /// che fa scattare la sentinella — o uno successivo.
        /// </summary>
        internal bool Failed()
        {
            _inARow++;
            if (_inARow >= _threshold && !HasGone)
            {
                HasGone = true;
                _whenItFires = _inARow;
            }
            return HasGone;
        }

        /// <summary>
        /// Cosa dire a chi legge il resoconto. Porta i NUMERI: quanti di fila
        /// e su quale soglia. «SOLIDWORKS non risponde» senza numeri non si
        /// distingue da un'opinione.
        /// </summary>
        internal string Report()
        {
            int howMany = HasGone ? _whenItFires : _inARow;
            return "SOLIDWORKS NON PRODUCE PIU' DOCUMENTI: " + howMany
                 + " tentativi di fila andati a vuoto (la soglia e' " + _threshold
                 + "). Le prove che seguono non sono rosse — NON SONO STATE "
                 + "FATTE. Chiudi SOLIDWORKS, riaprilo e rilancia il giro.";
        }
    }
}
