using System.Collections.Generic;
using System.Threading.Tasks;
using Oce.Solid.Agent.Core;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// THE PLAN THAT COMES FROM MCP, served to <see cref="PlanExecutor"/> one
    /// step at a time.
    ///
    /// WHY IT EXISTS, GIVEN THAT THE PLAN IS ALL HERE ALREADY.
    /// Because the executor must not know where the steps come from: in the add-in
    /// the OpenPDM server gives them to it, here this list gives them. The boundary
    /// is <see cref="IPlanSource"/>, and without it the bridge would have had to
    /// rewrite for itself the execution loop — which is exactly what it
    /// used to do, and which cost it three things:
    ///
    ///   * no references between steps: «the hole on the top face of what
    ///     you did at step 1» could not be written, because the
    ///     geometry of the steps already done was not kept anywhere;
    ///   * no single undo entry: `Ctrl+Z` undid only one step,
    ///     and to go back from a plan of five steps five were needed
    ///     — hoping not to have pressed one too many;
    ///   * a second, poorer implementation of the same cycle. Two
    ///     implementations are two behaviors that diverge, and the checks are
    ///     done on only one of the two.
    ///
    /// NOTHING IS VALIDATED HERE, and it is intended. Operation name, paths and
    /// shape of the arguments are checked **all together beforehand**, in
    /// `McpTools.Build`, and if anything is wrong the document is not touched
    /// at all. Validating as one goes would mean stopping at step 3 with the
    /// first two already built, that is, leaving half a part lying around for an
    /// error that could have been seen without opening anything.
    /// </summary>
    internal sealed class PlanInMemory : IPlanSourceWithoutState
    {
        private readonly List<PlanStep> _steps;
        private int _next;

        /// <summary>
        /// The progress thread (entry W2.2), and from which number to resume.
        ///
        /// THIS IS THE POINT THAT COUNTS, and not the check of the plan: the
        /// check lasts milliseconds, the execution can last minutes. The
        /// notification goes out from `NextAsync`, that is **before** the step
        /// begins — saying «I am about to do step 3 of 5» is useful; saying it after
        /// it has finished is much less useful.
        ///
        /// And the library did not need to be touched: the executor asks this class,
        /// which lives in the bridge, for the next step. The hook was already
        /// there, it was only missing someone to use it.
        /// </summary>
        private readonly Mcp.Progress.Wire _wire;
        private readonly double _startedAt;

        /// <summary>
        /// How each step went, in the order in which they were executed.
        /// It is the answer that the bridge sends back.
        /// </summary>
        public readonly List<Dictionary<string, object>> Outcomes =
            new List<Dictionary<string, object>>();

        public PlanInMemory(List<PlanStep> steps)
            : this(steps, null) { }

        public PlanInMemory(List<PlanStep> steps, Mcp.Progress.Wire wire)
        {
            _steps = steps;
            _wire = wire ?? new Mcp.Progress.Wire(null);
            // Where to resume from: if the check of the plan has already told
            // five steps, the execution starts at six. Asking the thread
            // instead of computing it means that the two points cannot
            // say numbers that overlap.
            _startedAt = _wire.Last;
        }

        public Task<NextStep> NextAsync(Dictionary<string, object> docState)
        {
            // The state is not needed: whoever had to look at it — the identity
            // check of the document — is the executor, on the real
            // document and not on a photograph.
            StopHeartbeat();
            if (_next >= _steps.Count) return Task.FromResult(NextStep.Fine());
            var thisOne = _steps[_next++];
            var line = "running step " + _next + " of " + _steps.Count + ": " + thisOne.Operation;
            _wire.Step(_startedAt + _next, line);
            StartHeartbeat(_startedAt + _next, line);
            return Task.FromResult(NextStep.This(thisOne));
        }

        // ── THE HEARTBEAT OF A LONG STEP (brief R13) ─────────────────────
        //
        // One notification per step said «running step 3 of 6» and then
        // nothing until the step was over: on 24/10/2026 a step of 1,069 s sent
        // nothing for eighteen minutes, and from outside «working» and «stuck»
        // looked the same. Every BeatEvery while the step runs a notification
        // says how long it has been on it and the last line the executor wrote
        // (what it is doing). Its progress grows inside the step
        // (k + n/(n+1): k.5, k.67, k.75 ...) and never reaches k + 1, so the next
        // step is still a step forward.
        //
        // It is stopped before the next step, at the outcome, and by the session
        // when the executor returns or throws (StopHeartbeat): a beat that went
        // out after the answer is the defect R13 found in the dry run, where the
        // client no longer knew the token. The beat holds the lock while it
        // sends, so once StopHeartbeat returns nothing more goes out, and a
        // callback of an old timer finds another generation and sends nothing.
        internal static readonly System.TimeSpan BeatEvery = System.TimeSpan.FromSeconds(10);
        private readonly object _beatKey = new object();
        private System.Threading.Timer _beat;
        private int _generation;
        private int _beats;
        private double _beatStep;
        private string _beatLine;
        private System.DateTime _beatStart;

        /// <summary>
        /// The last line the executor wrote, set by the session through the log
        /// callback: it is what the heartbeat says the step is doing.
        /// </summary>
        internal string LastLine;

        private void StartHeartbeat(double step, string line)
        {
            if (!_wire.On) return;
            lock (_beatKey)
            {
                _generation++;
                _beats = 0;
                _beatStep = step;
                _beatLine = line;
                _beatStart = System.DateTime.Now;
                int generation = _generation;
                _beat = new System.Threading.Timer(_ => Beat(generation), null, BeatEvery, BeatEvery);
            }
        }

        /// <summary>
        /// One beat of the step of that generation; nothing when the heartbeat
        /// was stopped or another step began. Internal so the contract suite
        /// drives it without waiting ten seconds.
        /// </summary>
        internal void Beat(int generation)
        {
            lock (_beatKey)
            {
                if (_beat == null || generation != _generation) return;
                _beats++;
                int seconds = (int)(System.DateTime.Now - _beatStart).TotalSeconds;
                var doing = LastLine;
                if (!string.IsNullOrWhiteSpace(doing))
                {
                    doing = doing.Trim();
                    if (doing.Length > 100) doing = doing.Substring(0, 100) + "...";
                }
                _wire.Step(_beatStep + _beats / (_beats + 1.0),
                           _beatLine + ", still running after " + seconds + "s" +
                           (string.IsNullOrWhiteSpace(doing) ? "" : " (last: " + doing + ")"));
            }
        }

        /// <summary>The generation of the step now running, for the contract suite.</summary>
        internal int Generation { get { lock (_beatKey) return _generation; } }

        /// <summary>
        /// Stops the heartbeat and waits for a beat that is being sent. The
        /// session calls it when the executor returns or throws, before the answer.
        /// </summary>
        internal void StopHeartbeat()
        {
            lock (_beatKey)
            {
                if (_beat != null) _beat.Dispose();
                _beat = null;
                _generation++;
            }
        }

        public Task OutcomeAsync(
            int order, bool succeeded, Dictionary<string, object> measurements, string error)
        {
            StopHeartbeat();
            object numbers = null;
            if (measurements != null) measurements.TryGetValue("measurements", out numbers);

            var outcome = new Dictionary<string, object>
            {
                ["operation"] = Name(order),
                ["succeeded"] = succeeded,
                ["error"] = succeeded ? null : error,
                ["measurements"] = numbers,
            };
            // WHERE THE STEP'S TIME WENT (brief R2): present only when the
            // executor measured it, so an outcome never carries an empty field.
            object timing = null;
            if (measurements != null && measurements.TryGetValue("timing_ms", out timing) && timing != null)
                outcome["timing_ms"] = timing;
            Outcomes.Add(outcome);
            return Task.FromResult(0);
        }

        private string Name(int order)
        {
            foreach (var p in _steps) if (p.Order == order) return p.Operation;
            return "";
        }
    }
}
