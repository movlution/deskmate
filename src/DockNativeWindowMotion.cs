using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class DockWindow
        {
            const uint MinimizeStart = 0x0016;
            Native.WinEventProc minimizeEvent;
            IntPtr minimizeHook;
            readonly Timer minimizePreviewTimer = new Timer { Interval = 400 };
            DockMinimizeSnapshot pendingMinimizePreview;
            IntPtr pendingPreviewWindow, minimizePreviewWindow;
            Rectangle pendingPreviewBounds, minimizePreviewBounds;
            Bitmap minimizePreview;
            double minimizePreviewAt;

            [StructLayout(LayoutKind.Sequential)]
            struct WindowPlacement
            {
                public uint Length, Flags, Show;
                public Native.POINT Min, Max;
                public Native.RECT Normal;
            }

            [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

            void WatchNativeMinimize()
            {
                if (Dry || minimizeHook != IntPtr.Zero) return;
                minimizeEvent = OnNativeMinimize;
                minimizeHook = Native.SetWinEventHook(MinimizeStart, MinimizeStart, IntPtr.Zero, minimizeEvent, 0, 0, 0);
                if (minimizeHook == IntPtr.Zero) return;
                minimizePreviewTimer.Tick += delegate { PrimeMinimizePreview(); };
                minimizePreviewTimer.Start();
            }

            void StopNativeMinimize()
            {
                minimizePreviewTimer.Stop();
                if (minimizeHook != IntPtr.Zero) Native.UnhookWinEvent(minimizeHook);
                minimizeHook = IntPtr.Zero;
                minimizeEvent = null;
                ClearMinimizePreview();
            }

            void ClearMinimizePreview()
            {
                if (pendingMinimizePreview != null) { pendingMinimizePreview.Dispose(); pendingMinimizePreview = null; }
                if (minimizePreview != null) { minimizePreview.Dispose(); minimizePreview = null; }
            }

            DockApp AppForWindow(IntPtr hwnd)
            {
                if (hwnd != IntPtr.Zero && hwnd == isle.WorkHwnd) return PreviewAppById("sys:work");
                return apps.Find(a => !a.Leaving && a.Wins.Exists(w => w.H == hwnd));
            }

            void PrimeMinimizePreview()
            {
                if (closing || !shown || !WindowMotionEnabled)
                {
                    ClearMinimizePreview();
                    return;
                }
                if (pendingMinimizePreview != null)
                {
                    if (!pendingMinimizePreview.Ready) return;
                    var image = pendingMinimizePreview.TakeCapturedImage();
                    pendingMinimizePreview.Dispose();
                    pendingMinimizePreview = null;
                    if (image != null)
                    {
                        if (minimizePreview != null) minimizePreview.Dispose();
                        minimizePreview = image;
                        minimizePreviewWindow = pendingPreviewWindow;
                        minimizePreviewBounds = pendingPreviewBounds;
                        minimizePreviewAt = clock.Elapsed.TotalMilliseconds;
                    }
                }
                if (minimizePreview != null && clock.Elapsed.TotalMilliseconds - minimizePreviewAt >= 5000)
                {
                    minimizePreview.Dispose();
                    minimizePreview = null;
                }
                if (cardMotion != null || openingApp != null) return;
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || ShellApp.IsMinimized(hwnd) || AppForWindow(hwnd) == null) return;
                var bounds = DockCardMotion.CardBounds(hwnd, mon);
                if (bounds.IsEmpty) return;
                var mouse = Cursor.Position;
                bool nearCaption = bounds.Contains(mouse) && mouse.Y < bounds.Top + 56 * scale;
                if (minimizePreview != null && minimizePreviewWindow == hwnd && minimizePreviewBounds == bounds
                    && clock.Elapsed.TotalMilliseconds - minimizePreviewAt < (nearCaption ? 900 : 3500)) return;
                var pending = new DockMinimizeSnapshot();
                if (!pending.Queue(hwnd, bounds, true)) { pending.Dispose(); return; }
                pendingMinimizePreview = pending;
                pendingPreviewWindow = hwnd;
                pendingPreviewBounds = bounds;
            }

            void OnNativeMinimize(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
            {
                if (closing || hwnd == IntPtr.Zero || obj != 0 || child != 0 || !shown || !WindowMotionEnabled
                    || (cardMotion != null && cardMotion.Handles(hwnd))) return;
                try
                {
                    var app = AppForWindow(hwnd);
                    if (app == null || !DockCardMotion.CanProxy(hwnd)) return;
                    var icon = MotionIcon(app);
                    if (icon.IsEmpty || !mon.Contains(icon)) return;
                    var bounds = MinimizedCardBounds(hwnd);
                    if (bounds.IsEmpty || (long)bounds.Width * bounds.Height > 8500000) return;
                    Bitmap image = null;
                    if (pendingMinimizePreview != null && pendingPreviewWindow == hwnd && pendingPreviewBounds == bounds
                        && pendingMinimizePreview.Ready) image = pendingMinimizePreview.TakeCapturedImage();
                    if (image == null && minimizePreview != null && minimizePreviewWindow == hwnd
                        && minimizePreviewBounds == bounds && clock.Elapsed.TotalMilliseconds - minimizePreviewAt < 5000)
                    {
                        image = minimizePreview;
                        minimizePreview = null;
                    }
                    bool realPreview = image != null;
                    if (image == null) image = MakeAppCard(app, bounds.Size);
                    StopCardMotion(false);
                    openingApp = null;
                    var motion = new DockCardMotion(hwnd, icon, bounds, DockWindowAction.Minimize);
                    double now = clock.Elapsed.TotalMilliseconds;
                    motion.ReuseSnapshot(image, bounds, now, realPreview);
                    ScheduleCardMotion(motion, app, now);
                    if (!wantShown)
                    {
                        Reveal(true);
                        hideLater.Start();
                    }
                }
                catch (Exception ex) { Log.Write("dock native minimize: " + ex.Message); }
            }

            Rectangle MinimizedCardBounds(IntPtr hwnd)
            {
                if (!ShellApp.IsMinimized(hwnd)) return DockCardMotion.CardBounds(hwnd, mon);
                // The minimized HWND can have a parking rectangle off-screen.
                if (pendingMinimizePreview != null && pendingPreviewWindow == hwnd && mon.Contains(pendingPreviewBounds))
                    return pendingPreviewBounds;
                if (minimizePreview != null && minimizePreviewWindow == hwnd && mon.Contains(minimizePreviewBounds))
                    return minimizePreviewBounds;
                var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf(typeof(WindowPlacement)) };
                if (!GetWindowPlacement(hwnd, ref placement)) return Rectangle.Empty;
                var screen = Screen.FromHandle(hwnd);
                if (screen.Bounds != mon) return Rectangle.Empty;
                if ((placement.Flags & 2) != 0) return screen.WorkingArea;
                var rect = Rectangle.FromLTRB(placement.Normal.Left, placement.Normal.Top, placement.Normal.Right, placement.Normal.Bottom);
                rect.Offset(screen.WorkingArea.Left - mon.Left, screen.WorkingArea.Top - mon.Top);
                return DockCardMotion.FitCardBounds(rect, mon, false);
            }

            Rectangle MotionIcon(DockApp app)
            {
                var icon = IconScreenRect(app);
                if (!icon.IsEmpty && !RevealReady)
                {
                    // Aim at the shown position while Auto-hide is moving the actual icon.
                    icon.Offset(0, BarTopShownScreen() - ToScreen(new RectangleF(0, barTop, 1, 1)).Top);
                }
                return icon;
            }

            Bitmap MakeAppCard(DockApp app, Size size)
            {
                if (Math.Max(size.Width, size.Height) > 480)
                {
                    float ratio = 480f / Math.Max(size.Width, size.Height);
                    size = new Size(Math.Max(1, (int)(size.Width * ratio)), Math.Max(1, (int)(size.Height * ratio)));
                }
                var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Ink.Light ? Color.FromArgb(226, 235, 248) : Color.FromArgb(34, 47, 68));
                    Bitmap badge;
                    if (icons.TryGetValue(IconCacheKey(app.Key), out badge) && badge != null)
                    {
                        int side = Math.Min(size.Width, size.Height) / 4;
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(badge, (size.Width - side) / 2, (size.Height - side) / 2, side, side);
                    }
                }
                return bitmap;
            }
        }
    }
}
