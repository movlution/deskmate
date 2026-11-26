using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    // A single window-only snapshot, owned here until the last animation frame.
    sealed class DockMinimizeSnapshot : NativeWindow, IDisposable
    {
        // PrintWindow may hang in the source app. Keep at most one capture in flight.
        static int capturing, prefetching;
        readonly object gate = new object();
        bool ready, disposed;
        Size sourceSize, rasterSize;
        Bitmap pendingImage, restoreImage;
        IntPtr from, to, image, canvas;
        CompGlass.MotionSurface surface;
        Rectangle viewport;
        public bool Composited { get { return surface != null; } }

        public bool Queue(IntPtr hwnd, Rectangle card, bool prefetch = false)
        {
            if ((prefetch ? Interlocked.CompareExchange(ref prefetching, 1, 0)
                : Interlocked.CompareExchange(ref capturing, 1, 0)) != 0) return false;
            try
            {
                if (ThreadPool.QueueUserWorkItem(delegate { Capture(hwnd, card, prefetch); })) return true;
                ReleaseCapture(prefetch);
                return false;
            }
            catch { ReleaseCapture(prefetch); throw; }
        }

        static void ReleaseCapture(bool prefetch)
        {
            if (prefetch) Interlocked.Exchange(ref prefetching, 0);
            else Interlocked.Exchange(ref capturing, 0);
        }

        static Rectangle WindowBounds(IntPtr hwnd)
        {
            Native.RECT r;
            return Native.GetWindowRect(hwnd, out r) ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;
        }

        void Capture(IntPtr hwnd, Rectangle card, bool prefetch)
        {
            Bitmap captured = null;
            Rectangle bounds = Rectangle.Empty;
            try
            {
                lock (gate) { if (disposed) return; }
                bounds = WindowBounds(hwnd);
                if (Native.IsWindowVisible(hwnd) && bounds.Contains(card) && (long)bounds.Width * bounds.Height <= 8500000
                    && DockCardMotion.CanProxy(hwnd))
                {
                    using (var bmp = IngatText.Snap(hwnd, true))
                        if (bmp != null && bmp.Size == bounds.Size && WindowBounds(hwnd) == bounds)
                        {
                            var region = new Rectangle(card.X - bounds.X, card.Y - bounds.Y, card.Width, card.Height);
                            captured = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppPArgb);
                            using (var g = Graphics.FromImage(captured))
                                g.DrawImageUnscaled(bmp, -region.X, -region.Y);
                        }
                }
            }
            catch (Exception ex)
            {
                if (captured != null) { captured.Dispose(); captured = null; }
                Log.Write("dock window snapshot: " + ex.Message);
            }
            finally
            {
                lock (gate)
                {
                    ReleaseCapture(prefetch);
                    if (disposed) { if (captured != null) captured.Dispose(); }
                    else
                    {
                        pendingImage = captured;
                        sourceSize = card.Size;
                        ready = true;
                    }
                }
            }
        }

        public bool Ready { get { lock (gate) return ready; } }

        public Bitmap TakeCapturedImage()
        {
            lock (gate)
            {
                var bitmap = pendingImage;
                pendingImage = null;
                return bitmap;
            }
        }

        public DockMinimizeSnapshot() { }

        public DockMinimizeSnapshot(Bitmap bitmap)
        {
            pendingImage = bitmap;
            sourceSize = bitmap.Size;
            ready = true;
        }

        public Bitmap TakeRestoreImage()
        {
            var bitmap = restoreImage;
            restoreImage = null;
            return bitmap;
        }

        public bool Prepare(Rectangle card, Rectangle monitor, bool retainForRestore = false)
        {
            var bitmap = TakeCapturedImage();
            if (bitmap == null) return false;
            try
            {
                if (retainForRestore) restoreImage = bitmap;
                if (PrepareComposition(bitmap, monitor)) return true;
                image = bitmap.GetHbitmap();
            }
            finally { if (!retainForRestore) bitmap.Dispose(); }
            from = Native.CreateCompatibleDC(IntPtr.Zero);
            to = Native.CreateCompatibleDC(IntPtr.Zero);
            if (from == IntPtr.Zero || to == IntPtr.Zero) return false;
            if (Native.SelectObject(from, image) == IntPtr.Zero) return false;
            var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = (int)Math.Ceiling(card.Width * 1.025f), biHeight = -(int)Math.Ceiling(card.Height * 1.025f),
                biPlanes = 1, biBitCount = 32 };
            IntPtr bits;
            canvas = Native.CreateDIBSection(to, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (canvas == IntPtr.Zero) return false;
            if (Native.SelectObject(to, canvas) == IntPtr.Zero) return false;
            Native.SetStretchBltMode(to, 4); // HALFTONE
            Native.SetBrushOrgEx(to, 0, 0, IntPtr.Zero);
            CreateHandle(new CreateParams
            {
                Caption = "Deskmate Dock Snapshot",
                Style = Native.WS_POPUP,
                ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                X = card.X, Y = card.Y, Width = card.Width, Height = card.Height
            });
            DockCardMotion.DisableProxyTransitions(Handle);
            return true;
        }

        bool PrepareComposition(Bitmap bitmap, Rectangle monitor)
        {
            if (!CompGlass.Available) return false;
            viewport = monitor;
            var candidate = new CompGlass.MotionSurface();
            try
            {
                CreateHandle(new CreateParams
                {
                    Caption = "Deskmate Dock Snapshot",
                    Style = Native.WS_POPUP | 0x08000000,
                    ExStyle = Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT,
                    X = viewport.X, Y = viewport.Y, Width = viewport.Width, Height = viewport.Height
                });
                DockCardMotion.DisableProxyTransitions(Handle);
                if (!candidate.Attach(Handle, bitmap)) return false;
                surface = candidate;
                return true;
            }
            catch (Exception ex) { Log.Write("dock composition snapshot: " + ex.Message); return false; }
            finally
            {
                if (surface == null)
                {
                    candidate.Dispose();
                    if (Handle != IntPtr.Zero) DestroyHandle();
                }
            }
        }

        public void Play(Rectangle card, Rectangle icon, float dpi, bool exiting, float duration, bool fromDock)
        {
            if (surface != null) surface.Play(card, icon, viewport, dpi, exiting, duration, fromDock);
        }

        public bool Show()
        {
            return Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        public bool Present(Rectangle frame, Rectangle monitor, byte opacity)
        {
            if (surface != null)
            {
                surface.Set(frame, viewport, opacity);
                return true;
            }
            bool unscaled = frame.Size == sourceSize;
            if (!unscaled && rasterSize != frame.Size)
            {
                if (!Native.StretchBlt(to, 0, 0, frame.Width, frame.Height, from,
                    0, 0, sourceSize.Width, sourceSize.Height, 0x00CC0020)) return false;
                rasterSize = frame.Size;
            }
            var onScreen = Rectangle.Intersect(frame, monitor);
            if (onScreen.Width <= 0 || onScreen.Height <= 0) return false;
            var at = new Native.POINT(onScreen.X, onScreen.Y);
            var size = new Native.SIZE(onScreen.Width, onScreen.Height);
            var src = new Native.POINT(onScreen.X - frame.X, onScreen.Y - frame.Y);
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = opacity };
            return Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref at, ref size, unscaled ? from : to, ref src, 0, ref blend, Native.ULW_ALPHA);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (pendingImage != null) { pendingImage.Dispose(); pendingImage = null; }
            }
            if (surface != null) { surface.Dispose(); surface = null; }
            if (restoreImage != null) { restoreImage.Dispose(); restoreImage = null; }
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (from != IntPtr.Zero) Native.DeleteDC(from);
            if (to != IntPtr.Zero) Native.DeleteDC(to);
            if (image != IntPtr.Zero) Native.DeleteObject(image);
            if (canvas != IntPtr.Zero) Native.DeleteObject(canvas);
        }
    }
}
