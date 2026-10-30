using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Shared
{
    /// <summary>
    /// NO SOLIDWORKS OBJECT IS RELEASED WHILE A CALL RUNS (bridge 0.160.2, 30/10/2026).
    ///
    /// ── THE FALL ──────────────────────────────────────────────────────────
    ///
    /// A cycle of 15 drawings (new drawing + second sheet + save, then close) driven through
    /// the bridge on a fresh SOLIDWORKS fell 8 times out of 8 on 30/10/2026 (bridge 0.159.10 and
    /// 0.160.1), always painting a drawing view: `uiBaseView_c::OnPaint` →
    /// `grUiSceneGraphEngine_c::drawWithSceneGraph` → `gfxSgDrawingView_c::removeSheet` →
    /// `gfxSgSheetView_c::removeOleBitmapNode` → `uoBillboardi_c::make`, an object already
    /// destroyed. The same cycle driven by a probe outside the repository never fell in 12 runs.
    ///
    /// ── WHY ───────────────────────────────────────────────────────────────
    ///
    /// The bridge has no [STAThread]: its main thread is in the MTA, and there the wrappers of
    /// the SOLIDWORKS objects (runtime callable wrappers) are released by the finalizer thread,
    /// on its own, at any moment after a collection. A collection starts by itself whenever a
    /// call allocates enough, so a Release could reach SOLIDWORKS while SOLIDWORKS was inside a
    /// call of the bridge — making a new drawing, for example. SOLIDWORKS takes an incoming call
    /// whenever it pumps its messages, and a call of its own pumps them while it paints. The
    /// probe fell with the same stack as soon as it was made to do the same: objects dropped and
    /// a thread collecting every 50 ms while the cycle ran (round k2 at the 8th drawing, round
    /// a2 at the 10th); with the same objects collected only between two drawings it did not.
    ///
    /// ── MEASURED: IT DOES NOT CURE THE FALL OF THE BRIDGE ─────────────────
    ///
    /// The same cycle through bridge 0.160.2, every run on a freshly restarted SOLIDWORKS, the
    /// gate on and off alternated (30/10/2026): on, 3 falls in 3 valid runs; off, 3 in 3, with the
    /// same two crash signatures on both sides. The gate stays on against the release in the
    /// middle of a call, which the probe showed; the cause of the falls of the bridge is
    /// elsewhere (the journal of SOLIDWORKS is the same for bridge and probe: the difference is
    /// in what the bridge reads).
    ///
    /// ── WHAT THE GATE DOES ────────────────────────────────────────────────
    ///
    /// <see cref="Close"/> holds the finalizer thread: an object with a finalizer is left
    /// unreachable, generation 0 is collected, and the finalizer of that object waits until
    /// <see cref="Open"/>. While it waits the finalizer thread finalizes nothing and releases
    /// no wrapper: what a collection finds dead during the call waits in the queue, and is
    /// released after the answer, by the collection that follows every call, while SOLIDWORKS
    /// is idle. A collection during the call still happens; only the releases wait.
    ///
    /// ── WHAT MUST NOT HAPPEN WHILE IT IS CLOSED ───────────────────────────
    ///
    /// Nothing may wait for the finalizers (`GC.WaitForPendingFinalizers`) between Close and
    /// Open: it would wait for ever. Whoever closes the gate opens it before collecting.
    ///
    /// OCE_FINALIZER_GATE (0, false or off) leaves the gate open, to measure the two ways.
    /// </summary>
    internal static class FinalizerGate
    {
        private static readonly ManualResetEvent Opened = new ManualResetEvent(true);
        private static readonly ManualResetEvent Held = new ManualResetEvent(false);
        private static bool _closed;

        /// <summary>False when OCE_FINALIZER_GATE is 0, false or off.</summary>
        internal static readonly bool Wanted = WantedNow();

        /// <summary>The object the finalizer thread waits in.</summary>
        private sealed class Keeper
        {
            ~Keeper()
            {
                Held.Set();
                Opened.WaitOne();
            }
        }

        /// <summary>
        /// Closes the gate. True when the finalizer thread is held within <paramref name="waitMs"/>;
        /// false when it is still busy with something else (the gate is closed all the same,
        /// and holds it as soon as it gets there).
        /// </summary>
        internal static bool Close(int waitMs)
        {
            if (_closed) return true;
            _closed = true;
            Opened.Reset();
            Held.Reset();
            LeaveAKeeper();
            GC.Collect(0, GCCollectionMode.Forced, true);
            return Held.WaitOne(waitMs);
        }

        /// <summary>Opens the gate: the finalizer thread goes on, and releases what waited.</summary>
        internal static void Open()
        {
            if (!_closed) return;
            _closed = false;
            Opened.Set();
        }

        // Not inlined: no reference to the keeper may stay in the frame of Close, or the
        // collection would find it alive.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void LeaveAKeeper()
        {
            new Keeper();
        }

        private static bool WantedNow()
        {
            var v = Environment.GetEnvironmentVariable("OCE_FINALIZER_GATE");
            if (string.IsNullOrWhiteSpace(v)) return true;
            v = v.Trim();
            return !(v == "0" ||
                     string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));
        }
    }
}
