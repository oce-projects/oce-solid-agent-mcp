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
        /// Com'e' andato ogni passo, nell'ordine in cui sono stati eseguiti.
        /// E' la risposta che il ponte manda indietro.
        /// </summary>
        public readonly List<Dictionary<string, object>> Esiti =
            new List<Dictionary<string, object>>();

        public PianoInMemoria(List<PassoDelPiano> passi)
        {
            _passi = passi;
        }

        public Task<ProssimoPasso> ProssimoAsync(Dictionary<string, object> stato)
        {
            // Lo stato non serve: chi doveva guardarlo — il controllo
            // d'identita' del documento — lo fa l'esecutore, sul documento
            // vero e non su una fotografia.
            if (_prossimo >= _passi.Count) return Task.FromResult(ProssimoPasso.Fine());
            return Task.FromResult(ProssimoPasso.Questo(_passi[_prossimo++]));
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
