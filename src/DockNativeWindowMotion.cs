using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class DockWindow
        {
            const uint MoveSizeEnd = 0x000b, MinimizeStart = 0x0016, MinimizeEnd = 0x0017;
            Native.WinEventProc minimizeEvent;
            IntPtr minimizeHook, closingFromDock;
            DateTime closeRequestedUtc;
            CompGlass.WindowOutline outline;
            Timer outlineRetry;
            IntPtr pendingOutline, lastOpeningOutline;
            int outlineRequestTick, lastOpeningTick;
            bool outlineFailed;

            internal bool WindowMotionEnabled { get { return !Dry && DockWindowMotion.Enabled(S); } }

            void WatchNativeMinimize()
            {
                if (Dry || minimizeHook != IntPtr.Zero) return;
                minimizeEvent = OnNativeMinimize;
                minimizeHook = Native.SetWinEventHook(MoveSizeEnd, MinimizeEnd, IntPtr.Zero, minimizeEvent, 0, 0, 0);
            }

            void StopNativeMinimize()
            {
                if (minimizeHook != IntPtr.Zero) Native.UnhookWinEvent(minimizeHook);
                minimizeHook = IntPtr.Zero;
                minimizeEvent = null;
                if (outlineRetry != null) { outlineRetry.Dispose(); outlineRetry = null; }
                if (outline != null) { outline.Dispose(); outline = null; }
            }

            DockApp AppForWindow(IntPtr hwnd)
            {
                if (hwnd != IntPtr.Zero && hwnd == isle.WorkHwnd) return PreviewAppById("sys:work");
                if (hwnd != IntPtr.Zero && hwnd == isle.SuruhHwnd) return PreviewAppById("sys:suruh");
                return apps.Find(a => !a.Leaving && a.Wins.Exists(w => w.H == hwnd));
            }

            void OnNativeMinimize(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
            {
                if (evt != MoveSizeEnd && evt != MinimizeStart && evt != MinimizeEnd) return;
                if (closing || hwnd == IntPtr.Zero || obj != 0 || child != 0 || !shown || !WindowMotionEnabled
                    || Native.MonitorFromWindow(hwnd, 1) != monitorHandle) return;
                try
                {
                    var win = watch.Find(hwnd);
                    if (evt == MoveSizeEnd) { DockWindowFrame.Update(win); return; }
                    uint age = unchecked((uint)Environment.TickCount - time);
                    if (age > 200u) return;
                    var app = AppForWindow(hwnd);
                    if (app == null) return;
                    bool opening = evt == MinimizeEnd;
                    if (opening) QueueOpeningOutline(hwnd);
                    else
                    {
                        if (pendingOutline == hwnd) CancelOpeningOutline();
                        if (lastOpeningOutline == hwnd) lastOpeningOutline = IntPtr.Zero;
                        PlayOutline(win, app, false);
                        if (win != null) win.FrameMinimized = true;
                    }
                    // A Dock restore already started the same response at click time.
                    if (opening && app.WindowOpening && DockWindowMotion.Progress(app, DateTime.UtcNow) < 1f) return;
                    WindowFeedback(app, opening, DateTime.UtcNow);
                    if (!wantShown && !opening)
                    {
                        Reveal(true);
                        hideLater.Start();
                    }
                }
                catch (Exception ex) { Log.Write("dock window feedback: " + ex.Message); }
            }

            void WindowFeedback(DockApp app, bool opening, DateTime now)
            {
                if (app == null) return;
                app.WindowMotionFrom = DockWindowMotion.Sample(app, DockWindowMotion.Progress(app, now));
                app.WindowOpening = opening;
                app.WindowMotionUtc = now;
                Animate();
            }

            void LaunchFeedback(DockApp app)
            {
                app.LaunchUtc = DateTime.UtcNow;
                if (WindowMotionEnabled)
                {
                    app.Hop = -1;
                    app.Hops = 0;
                    WindowFeedback(app, true, app.LaunchUtc);
                    return;
                }
                if (app.Hop < 0) { app.Hop = 0; app.Hops = Anim.Reduced ? 1 : 2; }
                Animate();
            }

            void WindowWentAway(IntPtr hwnd)
            {
                if (pendingOutline == hwnd) CancelOpeningOutline();
                if (lastOpeningOutline == hwnd) lastOpeningOutline = IntPtr.Zero;
                bool fromDock = closingFromDock == hwnd;
                if (fromDock) closingFromDock = IntPtr.Zero;
                if (!WindowMotionEnabled) return;
                var app = AppForWindow(hwnd);
                var win = watch.Find(hwnd);
                if (app != null && win != null && !win.FrameMinimized) PlayOutline(win, app, false);
                DateTime now = DateTime.UtcNow;
                if (fromDock && (now - closeRequestedUtc).TotalSeconds <= 1) WindowFeedback(app, false, now);
            }

            void ShellWindowActivated(IntPtr hwnd)
            {
                if (!WindowMotionEnabled || !shown || Native.MonitorFromWindow(hwnd, 1) != monitorHandle) return;
                var win = watch.Find(hwnd);
                if (win == null) return;
                // A newly shown window can receive activation before the app's Dock entry is rebuilt.
                if (hwnd == pendingOutline || (DateTime.UtcNow - win.BornUtc).TotalMilliseconds < 550)
                    QueueOpeningOutline(hwnd);
            }

            void QueueOpeningOutline(IntPtr hwnd)
            {
                if (outlineFailed || !shown || !WindowMotionEnabled ||
                    Native.MonitorFromWindow(hwnd, 1) != monitorHandle) return;
                int now = Environment.TickCount;
                if (lastOpeningOutline == hwnd && unchecked(now - lastOpeningTick) < 300) return;
                pendingOutline = hwnd;
                outlineRequestTick = now;
                if (outlineRetry == null)
                {
                    outlineRetry = new Timer { Interval = 16 };
                    outlineRetry.Tick += delegate { TryOpeningOutline(); };
                }
                outlineRetry.Start();
            }

            void CancelOpeningOutline()
            {
                pendingOutline = IntPtr.Zero;
                if (outlineRetry != null) outlineRetry.Stop();
            }

            void TryOpeningOutline()
            {
                IntPtr hwnd = pendingOutline;
                int now = Environment.TickCount;
                if (closing || !WindowMotionEnabled || !shown || unchecked(now - outlineRequestTick) > 200 ||
                    !ShellApp.WindowExists(hwnd))
                {
                    CancelOpeningOutline();
                    return;
                }
                if (GetForegroundWindow() != hwnd) return;
                var win = watch.Find(hwnd);
                var app = AppForWindow(hwnd);
                if (app == null || !DockWindowFrame.Update(win)) return;
                CancelOpeningOutline();
                lastOpeningOutline = hwnd;
                lastOpeningTick = now;
                PlayOutline(win, app, true);
            }

            void PlayOutline(DockWin win, DockApp app, bool opening)
            {
                if (outlineFailed || closing || !shown || win == null || win.FrameMonitor != monitorHandle ||
                    win.LastFrame.IsEmpty || !win.LastFrame.IntersectsWith(mon)) return;
                try
                {
                    if (outline == null) outline = new CompGlass.WindowOutline();
                    outline.Play(win.LastFrame, mon, MotionIcon(app), scale, opening, win.FrameMaximized);
                }
                catch (Exception ex)
                {
                    outlineFailed = true;
                    if (outline != null) { outline.Dispose(); outline = null; }
                    Log.Write("dock window outline: " + ex.Message);
                }
            }

            Rectangle MotionIcon(DockApp app)
            {
                var icon = IconScreenRect(app);
                if (!icon.IsEmpty && !RevealReady)
                    icon.Offset(0, BarTopShownScreen() - ToScreen(new RectangleF(0, barTop, 1, 1)).Top);
                return icon;
            }
        }
    }
}
