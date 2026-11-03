using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A DRAG THAT NOBODY RELEASES, ENDED (library 0.198.8, StuckDragGuard).
    ///
    /// The guard of library 0.198.7 had never been seen firing: in the A/B of 02/11/2026 (12 runs of
    /// chamfered_shaft_does_not_dimension_the_legs, guard on and off alternated) it posted Escape 0 times,
    /// and the only proof that it was wired was a line of its source (Zc2204DrawingRound22Contract). It
    /// also looked only at the thread of Process.MainWindowHandle, one window of the process.
    ///
    /// Here it is pointed at this process (StuckDragGuard.ProcessName): a window of a thread of ours takes
    /// the mouse (SetCapture) with no button down and lets it go on Escape, as CRectTracker::Track of
    /// SOLIDWORKS does. The control first: with the guard off (OCE_DRAG_GUARD=off) the window still holds
    /// the mouse after 5 s and no event is kept — else the second check would prove nothing. Then with the
    /// guard on: the mouse let go within 8 s, Escape received once, one event kept. Neither check needs
    /// SOLIDWORKS. Where a thread cannot take the mouse at all (a machine with no desktop), both checks
    /// are «not runnable here», with the reading that proves it.
    /// </summary>
    internal static class StuckDragGuardContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private const string ControlLabel = "guard off: a window that holds the mouse still holds it after 5 s, no Escape, no event (the control)";
        private const string GuardLabel = "guard on: a window of the watched process that holds the mouse with no button down gets Escape once within 8 s and lets go; one event kept";

        internal static void RunAll()
        {
            Group("a drag that nobody releases, ended (library 0.198.8): the guard pointed at a window of this process that holds the mouse");

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere(ControlLabel, library + " is missing: build the library first (build.ps1)");
                NotHere(GuardLabel, library + " is missing: build the library first (build.ps1)");
                return;
            }
            Type guard;
            try
            {
                // From the bytes: the repository may sit on a network drive, where LoadFrom is refused.
                var core = Assembly.Load(File.ReadAllBytes(library));
                guard = core.GetType("Oce.Solid.Agent.Core.StuckDragGuard", false);
            }
            catch (Exception ex) { Check("the library loads", false, Why(ex)); return; }
            if (guard == null) { Check("StuckDragGuard is in the library", false, "type missing"); return; }

            var processName = ReflectionLog.Global.Field(guard, "ProcessName", Any);
            if (processName == null) { Check(GuardLabel, false, "StuckDragGuard.ProcessName missing: the guard cannot be pointed at this process"); return; }

            var window = new CaptureWindow();
            string before = processName.GetValue(null) as string;
            string switchBefore = Environment.GetEnvironmentVariable("OCE_DRAG_GUARD");
            try
            {
                string why;
                if (!window.Start(out why))
                {
                    NotHere(ControlLabel, why);
                    NotHere(GuardLabel, why);
                    return;
                }
                using (var self = Process.GetCurrentProcess()) processName.SetValue(null, self.ProcessName);

                Environment.SetEnvironmentVariable("OCE_DRAG_GUARD", "off");
                Watch(guard, window, ControlLabel, 5000, false);

                Environment.SetEnvironmentVariable("OCE_DRAG_GUARD", null);
                Watch(guard, window, GuardLabel, 8000, true);
            }
            catch (Exception ex) { Check(GuardLabel, false, Why(ex)); }
            finally
            {
                processName.SetValue(null, before);
                Environment.SetEnvironmentVariable("OCE_DRAG_GUARD", switchBefore);
                window.Stop();
            }
        }

        /// <summary>
        /// A call of the bridge as the guard sees it: During open for up to <paramref name="ms"/>,
        /// closed as soon as the window lets the mouse go.
        /// </summary>
        private static void Watch(Type guard, CaptureWindow window, string label, int ms, bool guardOn)
        {
            var during = ReflectionLog.Global.Method(guard, "During", Any);
            var since = ReflectionLog.Global.Method(guard, "Since", Any);
            if (during == null || since == null) { Check(label, false, "StuckDragGuard.During or .Since missing"); return; }

            int escapesBefore = window.Escapes;
            var args = new object[] { 0 };
            var clock = Stopwatch.StartNew();
            List<string> events;
            bool held;
            using ((IDisposable)Invoke(during, args))
            {
                int mark = (int)args[0];
                while ((held = window.Holds()) && clock.ElapsedMilliseconds < ms) Thread.Sleep(100);
                events = (List<string>)Invoke(since, new object[] { mark });
            }
            long after = clock.ElapsedMilliseconds;
            int escapes = window.Escapes - escapesBefore;

            var wrong = new List<string>();
            if (guardOn)
            {
                if (held) wrong.Add("the window still holds the mouse after " + after + " ms");
                if (escapes != 1) wrong.Add("Escape received " + escapes + " times, not once");
                if (events.Count != 1) wrong.Add(events.Count + " events kept, not one");
            }
            else
            {
                if (!held) wrong.Add("the window let the mouse go by itself after " + after + " ms: the check of the guard on would prove nothing");
                if (escapes != 0) wrong.Add("Escape received " + escapes + " times with the guard off");
                if (events.Count != 0) wrong.Add(events.Count + " events kept with the guard off");
            }
            string seen = (held ? "held" : "let go") + " after " + after + " ms, Escape " + escapes + ", events " + events.Count +
                          (events.Count > 0 ? ": «" + string.Join("» «", events) + "»" : "");
            Check(label, wrong.Count == 0, wrong.Count == 0 ? seen : string.Join("; ", wrong) + " (" + seen + ")");
        }

        private static object Invoke(MethodInfo m, object[] args)
        {
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static string Why(Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return inner.GetType().Name + ": " + inner.Message;
        }

        /// <summary>
        /// A top-level window on a thread of its own, with a message loop: it takes the mouse with no
        /// button down and lets it go on Escape, counting the Escapes it gets.
        /// </summary>
        private sealed class CaptureWindow
        {
            private const uint WS_POPUP = 0x80000000;
            private const uint WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_KEYDOWN = 0x0100;
            private const int VK_ESCAPE = 0x1B;

            private readonly WndProc _proc;
            private readonly ManualResetEvent _ready = new ManualResetEvent(false);
            private Thread _thread;
            private IntPtr _hwnd = IntPtr.Zero;
            private uint _threadId;
            private int _escapes;
            private string _why;

            internal CaptureWindow() { _proc = Proc; }

            internal int Escapes { get { return Volatile.Read(ref _escapes); } }

            internal bool Start(out string why)
            {
                _thread = new Thread(Loop) { IsBackground = true, Name = "drag guard contract window" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                if (!_ready.WaitOne(5000)) { why = "the window thread did not start within 5 s"; return false; }
                why = _why;
                return why == null;
            }

            /// <summary>The window holds the mouse, as GetGUIThreadInfo of its thread says: what the guard reads.</summary>
            internal bool Holds()
            {
                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                return GetGUIThreadInfo(_threadId, ref info) && info.hwndCapture == _hwnd;
            }

            internal void Stop()
            {
                if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (_thread != null) _thread.Join(2000);
            }

            private void Loop()
            {
                try
                {
                    _threadId = GetCurrentThreadId();
                    IntPtr instance = GetModuleHandle(null);
                    string name = "OceStuckDragGuardContract" + _threadId;
                    var cls = new WNDCLASSEX
                    {
                        cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = _proc, hInstance = instance, lpszClassName = name
                    };
                    if (RegisterClassEx(ref cls) == 0) { _why = "RegisterClassEx failed, error " + Marshal.GetLastWin32Error(); return; }
                    _hwnd = CreateWindowEx(0, name, "drag guard contract", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                    if (_hwnd == IntPtr.Zero) { _why = "CreateWindowEx failed, error " + Marshal.GetLastWin32Error(); return; }
                    SetCapture(_hwnd);
                    if (!Holds())
                    {
                        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                        bool read = GetGUIThreadInfo(_threadId, ref info);
                        _why = "a thread of this process cannot take the mouse here: after SetCapture GetGUIThreadInfo " +
                               (read ? "says capture 0x" + info.hwndCapture.ToString("X") + ", not the window 0x" + _hwnd.ToString("X")
                                     : "fails, error " + Marshal.GetLastWin32Error());
                        DestroyWindow(_hwnd);
                        _hwnd = IntPtr.Zero;
                        return;
                    }
                }
                catch (Exception ex) { _why = Why(ex); return; }
                finally { _ready.Set(); }

                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }

            private IntPtr Proc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
            {
                if (msg == WM_KEYDOWN && (int)wParam == VK_ESCAPE)
                {
                    Interlocked.Increment(ref _escapes);
                    ReleaseCapture();
                    return IntPtr.Zero;
                }
                if (msg == WM_DESTROY) { PostQuitMessage(0); return IntPtr.Zero; }
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
        }

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
                                                    IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    }
}
