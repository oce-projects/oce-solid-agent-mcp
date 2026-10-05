using System.Collections.Generic;
using System.Threading.Tasks;
using Oce.Solid.Agent.Core;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// IL PIANO CHE ARRIVA DA MCP, servito a <see cref="PlanExecutor"/> un
    /// passo alla volta.
    ///
    /// PERCHE' ESISTE, VISTO CHE IL PIANO E' GIA' TUTTO QUI.
    /// Perche' l'esecutore non deve sapere da dove vengono i passi: nell'add-in
    /// glieli da' il server di OpenPDM, qui glieli da' questa lista. Il confine
    /// e' <see cref="IPlanSource"/>, e senza di lui il ponte avrebbe dovuto
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
    /// `McpTools.Build`, e se qualcosa non va il documento non viene toccato
    /// affatto. Validare a mano a mano vorrebbe dire fermarsi al passo 3 con i
    /// primi due gia' costruiti, cioe' lasciare in giro mezzo pezzo per un
    /// errore che si poteva vedere senza aprire niente.
    /// </summary>
    internal sealed class PlanInMemory : IPlanSource
    {
        private readonly List<PlanStep> _steps;
        private int _next;

        /// <summary>
        /// Il filo dell'avanzamento (voce W2.2), e da che numero riprendere.
        ///
        /// QUESTO E' IL PUNTO CHE CONTA, e non il controllo del piano: il
        /// controllo dura millisecondi, l'esecuzione puo' durare minuti. La
        /// notifica parte da `NextAsync`, cioe' **prima** che il passo
        /// cominci — dire «sto per fare il passo 3 di 5» serve; dirlo dopo che
        /// e' finito serve molto meno.
        ///
        /// E non e' servito toccare la libreria: l'esecutore chiede il passo
        /// successivo a questa classe, che sta nel ponte. L'aggancio c'era
        /// gia', mancava solo chi lo usasse.
        /// </summary>
        private readonly Mcp.Progress.Wire _wire;
        private readonly double _startedAt;

        /// <summary>
        /// Com'e' andato ogni passo, nell'ordine in cui sono stati eseguiti.
        /// E' la risposta che il ponte manda indietro.
        /// </summary>
        public readonly List<Dictionary<string, object>> Outcomes =
            new List<Dictionary<string, object>>();

        public PlanInMemory(List<PlanStep> steps)
            : this(steps, null) { }

        public PlanInMemory(List<PlanStep> steps, Mcp.Progress.Wire wire)
        {
            _steps = steps;
            _wire = wire ?? new Mcp.Progress.Wire(null);
            // Da dove riprendere: se il controllo del piano ha gia' raccontato
            // cinque passi, l'esecuzione comincia a sei. Chiederlo al filo
            // invece di calcolarlo vuol dire che i due punti non possono
            // dire numeri che si sovrappongono.
            _startedAt = _wire.Last;
        }

        public Task<NextStep> NextAsync(Dictionary<string, object> docState)
        {
            // Lo stato non serve: chi doveva guardarlo — il controllo
            // d'identita' del documento — lo fa l'esecutore, sul documento
            // vero e non su una fotografia.
            if (_next >= _steps.Count) return Task.FromResult(NextStep.Fine());
            var thisOne = _steps[_next++];
            _wire.Step(_startedAt + _next,
                        "eseguo il passo " + _next + " di " + _steps.Count +
                        ": " + thisOne.Operation);
            return Task.FromResult(NextStep.This(thisOne));
        }

        public Task OutcomeAsync(
            int order, bool succeeded, Dictionary<string, object> measurements, string error)
        {
            object numbers = null;
            if (measurements != null) measurements.TryGetValue("measurements", out numbers);

            Outcomes.Add(new Dictionary<string, object>
            {
                ["operation"] = Name(order),
                ["succeeded"] = succeeded,
                ["error"] = succeeded ? null : error,
                ["measurements"] = numbers,
            });
            return Task.FromResult(0);
        }

        private string Name(int order)
        {
            foreach (var p in _steps) if (p.Order == order) return p.Operation;
            return "";
        }
    }
}
