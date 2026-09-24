using System.Collections.Generic;
using System.Threading.Tasks;
using Riscontro.Disegno;

namespace Oce.Solid.Agent.Ponte
{
    /// <summary>
    /// IL PIANO CHE ARRIVA DA MCP, servito a <see cref="EsecutorePiano"/> un
    /// passo alla volta.
    ///
    /// PERCHE' ESISTE, VISTO CHE IL PIANO E' GIA' TUTTO QUI.
    /// Perche' l'esecutore non deve sapere da dove vengono i passi: nell'add-in
    /// glieli da' il server di OpenPDM, qui glieli da' questa lista. Il confine
    /// e' <see cref="IFontePiano"/>, e senza di lui il ponte avrebbe dovuto
    /// riscriversi il giro di esecuzione — che e' esattamente quello che
    /// faceva, e che gli costava tre cose:
    ///
    ///   * niente riferimenti fra passi: «il foro sulla faccia in cima a quello
    ///     che hai fatto al passo 1» non si poteva scrivere, perche' la
    ///     geometria dei passi gia' fatti non veniva tenuta da nessuna parte;
    ///   * niente voce di annullamento unica: `Ctrl+Z` disfaceva un passo solo,
    ///     e per tornare indietro da un piano di cinque passi ne servivano
    ///     cinque — sperando di non averne premuto uno di troppo;
    ///   * una seconda implementazione, piu' povera, dello stesso ciclo. Due
    ///     implementazioni sono due comportamenti che divergono, e le prove si
    ///     fanno su una sola delle due.
    ///
    /// QUI NON SI VALIDA NIENTE, ed e' voluto. Nome dell'operazione, percorsi e
    /// forma degli argomenti si controllano **tutti insieme prima**, in
    /// `Attrezzi.Disegna`, e se qualcosa non va il documento non viene toccato
    /// affatto. Validare a mano a mano vorrebbe dire fermarsi al passo 3 con i
    /// primi due gia' costruiti, cioe' lasciare in giro mezzo pezzo per un
    /// errore che si poteva vedere senza aprire niente.
    /// </summary>
    internal sealed class PianoInMemoria : IFontePiano
    {
        private readonly List<PassoDelPiano> _passi;
        private int _prossimo;

        /// <summary>
        /// Il filo dell'avanzamento (voce W2.2), e da che numero riprendere.
        ///
        /// QUESTO E' IL PUNTO CHE CONTA, e non il controllo del piano: il
        /// controllo dura millisecondi, l'esecuzione puo' durare minuti. La
        /// notifica parte da `ProssimoAsync`, cioe' **prima** che il passo
        /// cominci — dire «sto per fare il passo 3 di 5» serve; dirlo dopo che
        /// e' finito serve molto meno.
        ///
        /// E non e' servito toccare la libreria: l'esecutore chiede il passo
        /// successivo a questa classe, che sta nel ponte. L'aggancio c'era
        /// gia', mancava solo chi lo usasse.
        /// </summary>
        private readonly Mcp.Avanzamento.Filo _filo;
        private readonly double _partenza;

        /// <summary>
        /// Com'e' andato ogni passo, nell'ordine in cui sono stati eseguiti.
        /// E' la risposta che il ponte manda indietro.
        /// </summary>
        public readonly List<Dictionary<string, object>> Esiti =
            new List<Dictionary<string, object>>();

        public PianoInMemoria(List<PassoDelPiano> passi)
            : this(passi, null) { }

        public PianoInMemoria(List<PassoDelPiano> passi, Mcp.Avanzamento.Filo filo)
        {
            _passi = passi;
            _filo = filo ?? new Mcp.Avanzamento.Filo(null);
            // Da dove riprendere: se il controllo del piano ha gia' raccontato
            // cinque passi, l'esecuzione comincia a sei. Chiederlo al filo
            // invece di calcolarlo vuol dire che i due punti non possono
            // dire numeri che si sovrappongono.
            _partenza = _filo.Ultimo;
        }

        public Task<ProssimoPasso> ProssimoAsync(Dictionary<string, object> stato)
        {
            // Lo stato non serve: chi doveva guardarlo — il controllo
            // d'identita' del documento — lo fa l'esecutore, sul documento
            // vero e non su una fotografia.
            if (_prossimo >= _passi.Count) return Task.FromResult(ProssimoPasso.Fine());
            var questo = _passi[_prossimo++];
            _filo.Passo(_partenza + _prossimo,
                        "eseguo il passo " + _prossimo + " di " + _passi.Count +
                        ": " + questo.Operazione);
            return Task.FromResult(ProssimoPasso.Questo(questo));
        }

        public Task EsitoAsync(
            int ordine, bool riuscito, Dictionary<string, object> misure, string errore)
        {
            object numeri = null;
            if (misure != null) misure.TryGetValue("misure", out numeri);

            Esiti.Add(new Dictionary<string, object>
            {
                ["operazione"] = Nome(ordine),
                ["riuscita"] = riuscito,
                ["errore"] = riuscito ? null : errore,
                ["misure"] = numeri,
            });
            return Task.FromResult(0);
        }

        private string Nome(int ordine)
        {
            foreach (var p in _passi) if (p.Ordine == ordine) return p.Operazione;
            return "";
        }
    }
}
