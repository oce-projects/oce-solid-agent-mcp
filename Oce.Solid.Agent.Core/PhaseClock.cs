using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHERE THE TIME OF A CALL GOES, phase by phase (brief R2 and R24, 25/10/2026).
    ///
    /// ── WHY IT EXISTS ──────────────────────────────────────────────────
    ///
    /// On 24/10/2026 a step of a plan cost 54.5 s at the median, and nothing
    /// said where those seconds went: the bridge wrote no log, and the
    /// client kept only «still running (30s elapsed)». The estimate that was
    /// written down — «a reading costs 0.2-3 s, so 48 s per step are not
    /// explained» — had been measured on EMPTY parts only (`solid_body:
    /// false` in all ten readings of that session). A number taken in the
    /// wrong place, and nothing in the bridge could say it was wrong.
    ///
    /// ── HOW IT WORKS ───────────────────────────────────────────────────
    ///
    /// One collection per call, on the thread that serves it: the bridge
    /// serves one request at a time on one thread (Program.cs, «it serves one
    /// request at a time on the same thread»), so a thread-static field is
    /// exactly «this call». Nothing is collected when no call opened a
    /// collection: the library also runs inside the bench and the probes,
    /// and there the timers cost one null check.
    ///
    /// Every phase keeps the total milliseconds and how many times it ran: a
    /// reading of the state done twice per step must show up as `n: 42` on a
    /// plan of 21 steps, not as one big number with no count.
    ///
    /// ── WHAT IT DOES NOT DO ────────────────────────────────────────────
    ///
    /// It does not change what is measured: a timer around a call returns
    /// what the call returns and lets through what it throws.
    /// </summary>
    public static class PhaseClock
    {
        [ThreadStatic] private static Dictionary<string, double[]> _totals;

        /// <summary>Opens the collection of this call, discarding any left over.</summary>
        public static void Begin()
        {
            _totals = new Dictionary<string, double[]>(StringComparer.Ordinal);
        }

        /// <summary>Closes the collection and gives back what it holds.</summary>
        public static Dictionary<string, object> Finish()
        {
            var done = Totals();
            _totals = null;
            return done;
        }

        /// <summary>True while a call is collecting.</summary>
        public static bool Collecting { get { return _totals != null; } }

        /// <summary>Adds <paramref name="milliseconds"/> to <paramref name="phase"/>.</summary>
        public static void Add(string phase, double milliseconds)
        {
            var totals = _totals;
            if (totals == null || string.IsNullOrEmpty(phase)) return;
            double[] slot;
            if (!totals.TryGetValue(phase, out slot))
            {
                slot = new double[2];
                totals[phase] = slot;
            }
            slot[0] += milliseconds;
            slot[1] += 1;
        }

        /// <summary>Runs <paramref name="work"/> and adds its time to <paramref name="phase"/>.</summary>
        /// <summary>
        /// <see cref="Add"/> under a name SOLIDWORKS does not use (library 0.179.4): the
        /// contract on undecided calls counts every «Add» whose receiver it cannot type,
        /// because the interop has members called Add. For a stopwatch kept by the
        /// caller around lines whose text a contract reads.
        /// </summary>
        public static void Spent(string phase, double milliseconds)
        {
            Add(phase, milliseconds);
        }

        public static T Time<T>(string phase, Func<T> work)
        {
            if (_totals == null) return work();
            var watch = Stopwatch.StartNew();
            try { return work(); }
            finally { Add(phase, watch.Elapsed.TotalMilliseconds); }
        }

        /// <summary>Runs <paramref name="work"/> and adds its time to <paramref name="phase"/>.</summary>
        public static void Time(string phase, Action work)
        {
            if (_totals == null) { work(); return; }
            var watch = Stopwatch.StartNew();
            try { work(); }
            finally { Add(phase, watch.Elapsed.TotalMilliseconds); }
        }

        /// <summary>
        /// A lap timer for a long method cut into sections: every
        /// <see cref="Lap.Next"/> adds the time since the previous mark to
        /// «prefix.section». One line per boundary instead of a lambda around
        /// every block of a method of nine hundred lines.
        /// </summary>
        public static Lap Laps(string prefix)
        {
            return new Lap(prefix, _totals != null);
        }

        /// <summary>See <see cref="Laps"/>.</summary>
        public sealed class Lap
        {
            private readonly string _prefix;
            private readonly Stopwatch _watch;
            private double _last;

            internal Lap(string prefix, bool on)
            {
                _prefix = prefix;
                _watch = on ? Stopwatch.StartNew() : null;
            }

            /// <summary>Closes the section that ends here, under its name.</summary>
            public void Next(string section)
            {
                if (_watch == null) return;
                double now = _watch.Elapsed.TotalMilliseconds;
                Add(_prefix + "." + section, now - _last);
                _last = now;
            }
        }

        /// <summary>
        /// A copy of what is collected so far: phase → { ms, n }, with the
        /// milliseconds rounded to a tenth.
        /// </summary>
        public static Dictionary<string, object> Totals()
        {
            var output = new Dictionary<string, object>(StringComparer.Ordinal);
            var totals = _totals;
            if (totals == null) return output;
            foreach (string phase in totals.Keys)
            {
                double[] sum = totals[phase];
                output[phase] = new Dictionary<string, object>
                {
                    ["ms"] = Math.Round(sum[0], 1),
                    ["n"] = (int)sum[1],
                };
            }
            return output;
        }

        /// <summary>
        /// The milliseconds collected so far for one phase, 0 if none. The
        /// executor uses it to tell the time of ONE step: the total before
        /// the step and after it.
        /// </summary>
        public static double Milliseconds(string phase)
        {
            var totals = _totals;
            double[] slot;
            return totals != null && totals.TryGetValue(phase, out slot) ? slot[0] : 0;
        }

        /// <summary>Milliseconds as the log and the answers write them.</summary>
        public static double Round(double milliseconds)
        {
            return Math.Round(milliseconds, 1, MidpointRounding.AwayFromZero);
        }
    }
}
