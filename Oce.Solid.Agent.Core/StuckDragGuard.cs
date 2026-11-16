using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A DRAG THAT NOBODY RELEASES (library 0.198.7, note 101 §16.7).
    ///
    /// ── WHAT HAPPENS ───────────────────────────────────────────────────
    ///
    /// Some calls select by simulating a click in a drawing view: `ISilhouetteEdge.Select2`
    /// (SOLIDWORKS stack `auModelDoc_c::s_selectByPick` -> `uiModelView_c::OnLButtonDown`) and a
    /// reading of the view that goes through `uiViewBucket_c::getPolylines` -> `s_selectByPick`.
    /// Now and then the click takes the drawing view and SOLIDWORKS starts dragging it
    /// (`uiSelectionMan_c::startDragDrop` -> `uiDrawingDoc_c::specialDrag` -> `CRectTracker::Track`):
    /// its main thread holds the mouse and waits for a button that nobody pressed, and the call of the
    /// bridge never comes back. Measured on 02/11/2026: chamfered_shaft_does_not_dimension_the_legs hung
    /// in 3 runs of 8 with library 0.198.6 and in 1 of 8 with 0.198.2; two full benches of bridge 0.170.6
    /// were lost; the stacks of 11:59 and 12:07 both end in `CRectTracker::Track`, with the mouse
    /// captured (GetGUIThreadInfo) and no button down. Escape posted to the window that held the mouse
    /// ended the drag without moving anything: the bench went on within 8 s and the case was green.
    ///
    /// ── WHAT IT DOES ───────────────────────────────────────────────────
    ///
    /// While a call of the bridge is under way (<see cref="During"/>), a timer looks every 0,5 s at the
    /// threads that own a window of SOLIDWORKS, its main thread among them. When one of its windows
    /// has held the mouse for 3 s, with no menu and no move or size loop open (the flags of
    /// GUITHREADINFO) and no mouse button down (GetAsyncKeyState), Escape is posted to that window once
    /// and the event is kept, so that the call can say it. A person who drags holds a button: a drag of
    /// the owner's is never touched. Outside the calls of the bridge nothing is looked at.
    /// </summary>
    internal static class StuckDragGuard
    {
        private const int TickMs = 500;
        private const int HeldMsBeforeEscape = 3000;
        private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
        private const int VK_ESCAPE = 0x1B, VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04;
        // GUI_INMOVESIZE, GUI_INMENUMODE, GUI_SYSTEMMENUMODE, GUI_POPUPMENUMODE: a loop of the system, not a drag.
        private const int SystemLoops = 0x02 | 0x04 | 0x08 | 0x10;

        /// <summary>The process watched. The contract suite points it at itself, to see the guard end a capture.</summary>
        internal static string ProcessName = "SLDWORKS";

        private static readonly object Gate = new object();
        private static readonly List<string> Events = new List<string>();
        private static Timer _timer;
        private static int _active;
        private static IntPtr _held = IntPtr.Zero;
        private static int _heldMs;
        private static bool _escaped;

        /// <summary>
        /// Watches SOLIDWORKS until the returned scope is disposed; <paramref name="mark"/> is the number
        /// of events so far, for <see cref="Since"/>. Scopes nest: the timer runs while one is open.
        /// </summary>
        internal static IDisposable During(out int mark)
        {
            lock (Gate)
            {
                mark = Events.Count;
                // OCE_DRAG_GUARD=off: nothing is watched (an A/B of the guard on the same build).
                if (string.Equals(Environment.GetEnvironmentVariable("OCE_DRAG_GUARD"), "off", StringComparison.OrdinalIgnoreCase))
                    return new Scope(false);
                if (_active++ == 0)
                {
                    _held = IntPtr.Zero; _heldMs = 0; _escaped = false;
                    if (_timer == null) _timer = new Timer(_ => Tick(), null, TickMs, TickMs);
                    else _timer.Change(TickMs, TickMs);
                }
            }
            return new Scope(true);
        }

        /// <summary>The events kept since <paramref name="mark"/>, each a sentence.</summary>
        internal static List<string> Since(int mark)
        {
            lock (Gate) return mark < Events.Count ? Events.GetRange(mark, Events.Count - mark) : new List<string>();
        }

        private sealed class Scope : IDisposable
        {
            private int _done;

            public Scope(bool counted) { if (!counted) _done = 1; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) != 0) return;
                lock (Gate)
                    if (--_active == 0 && _timer != null) _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private static void Tick()
        {
            try
            {
                IntPtr capture = IntPtr.Zero;
                foreach (uint thread in WindowThreadsOf(ProcessName))
                {
                    var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                    if (!GetGUIThreadInfo(thread, ref info)) continue;
                    if (info.hwndCapture != IntPtr.Zero && (info.flags & SystemLoops) == 0) { capture = info.hwndCapture; break; }
                }
                bool button = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0 ||
                              (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
                lock (Gate)
                {
                    if (_active == 0) return;
                    if (capture == IntPtr.Zero || button) { _held = IntPtr.Zero; _heldMs = 0; _escaped = false; return; }
                    if (capture != _held) { _held = capture; _heldMs = 0; _escaped = false; return; }
                    _heldMs += TickMs;
                    if (_escaped || _heldMs < HeldMsBeforeEscape) return;
                    bool down = PostMessage(capture, WM_KEYDOWN, (IntPtr)VK_ESCAPE, (IntPtr)0x00010001);
                    bool up = PostMessage(capture, WM_KEYUP, (IntPtr)VK_ESCAPE, unchecked((IntPtr)(int)0xC0010001));
                    _escaped = true;
                    Events.Add("SOLIDWORKS held the mouse for " + (_heldMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) +
                               " s with no button down (a drag of the view started by a simulated click): Escape " +
                               (down && up ? "was posted to it, at " : "could not be posted to it, at ") +
                               DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }

        /// <summary>
        /// The threads that own a top-level window of the processes so named: the main thread of SOLIDWORKS
        /// among them. Every window is looked at, not Process.MainWindowHandle, which for a console process
        /// is the console.
        /// </summary>
        private static HashSet<uint> WindowThreadsOf(string processName)
        {
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try { pids.Add((uint)p.Id); } catch { }
                finally { p.Dispose(); }
            }
            var threads = new HashSet<uint>();
            if (pids.Count == 0) return threads;
            EnumWindows((h, l) =>
            {
                uint pid;
                uint thread = GetWindowThreadProcessId(h, out pid);
                if (thread != 0 && pids.Contains(pid)) threads.Add(thread);
                return true;
            }, IntPtr.Zero);
            return threads;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
