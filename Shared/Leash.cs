using System;

namespace Shared
{
    /// <summary>
    /// WHEN A CALL DOES NOT RETURN, AT LEAST WE KNOW WHICH ONE (entries K18 and Z-C4).
    ///
    /// ── WHAT IT COST NOT TO HAVE IT ───────────────────────────────────────
    ///
    /// On 23/9/2026 three bench runs died hanging. The process was alive,
    /// SOLIDWORKS `Responding=False`, **zero CPU on both**, no modal
    /// window: stopped inside `NewDocument`, which was reading the
    /// document template from a network share with a lock held on it.
    ///
    /// The sentinel (entry K17) could not help: **it counts the answers**, and
    /// there no answer was coming back to be counted. The run stayed hanging
    /// until someone looked — and **nobody looks at a run that is
    /// going well**.
    ///
    /// ── WHAT IT DOES, AND ABOVE ALL WHAT IT DOES NOT DO ───────────────────
    ///
    /// **It does not save the call.** A stuck COM call cannot be cancelled: the
    /// thread making it stays there forever, and there is nothing to be done
    /// about it. Promising a «timeout that retries» would be the usual
    /// guard that does not do what it says.
    ///
    /// What it does is turn **an infinite and mute wait** into **a
    /// report**: which operation, for how long, on which document. Then whoever
    /// uses it decides — in the bench, it prints and dies, because with SOLIDWORKS
    /// stuck the checks that follow would measure nothing.
    ///
    /// ── WHY A HEARTBEAT AND NOT A WORKER THREAD ───────────────────────────
    ///
    /// The obvious way — run the call on another thread and wait for it
    /// with a deadline — **is wrong here**: the SOLIDWORKS interop is
    /// single-threaded (STA), and moving the calls onto any thread makes
    /// them go through COM marshalling, that is, it adds a new way of
    /// getting stuck instead of removing one.
    ///
    /// Here the thread that works never changes: it **announces** what it is about to do
    /// and **says** when it has finished. A guardian, from outside, watches the clock.
    /// It does not touch the work: it watches it.
    /// </summary>
    internal sealed class Leash
    {
        private readonly object _key = new object();
        private string _what;
        private DateTime _da;

        /// <summary>
        /// How long a call may last before it is said to be hung.
        /// It is not «how long it should last»: it is **how long beyond any doubt**.
        /// A tight value turns a slow check into a false alarm, and
        /// a false alarm switches the guardian off the next day.
        /// </summary>
        internal int ExpiryMs { get; private set; }

        internal Leash(int expiryMs)
        {
            if (expiryMs < 1000)
                throw new ArgumentOutOfRangeException(
                    "expiryMs",
                    "sotto il secondo non e' un guinzaglio, e' un cronometro: "
                    + "qualunque prova vera lo farebbe scattare.");
            ExpiryMs = expiryMs;
        }

        /// <summary>A call is about to start, and it says which.</summary>
        internal void Begin(string what, DateTime now)
        {
            lock (_key) { _what = what; _da = now; }
        }

        /// <summary>It has returned. From here on there is nothing to watch —
        /// the guardian must stay quiet, otherwise the first pause between two checks
        /// would become an alarm.</summary>
        internal void Finished()
        {
            lock (_key) { _what = null; }
        }

        /// <summary>
        /// What is hung at this moment, or `null`. It takes the time from
        /// outside: a clock that is passed in is a clock that can be made to lie
        /// in a check, and **a guard that cannot be made to trip cannot
        /// be believed**.
        /// </summary>
        internal string Hanging(DateTime now)
        {
            lock (_key)
            {
                if (_what == null) return null;
                var howMuch = now - _da;
                if (howMuch.TotalMilliseconds < ExpiryMs) return null;
                return "LA CHIAMATA NON TORNA: «" + _what + "» e' partita "
                     + ((int)howMuch.TotalSeconds) + " secondi fa e non ha "
                     + "ancora risposto (la soglia e' " + (ExpiryMs / 1000)
                     + "). Non si puo' annullare una chiamata COM piantata: "
                     + "quello che si puo' fare e' dire QUALE era, e fermarsi.";
            }
        }

        /// <summary>For the report: how long the current one has lasted, in
        /// seconds, or -1 if there is none.</summary>
        internal int ForHowManySeconds(DateTime now)
        {
            lock (_key)
            {
                if (_what == null) return -1;
                return (int)(now - _da).TotalSeconds;
            }
        }
    }
}
