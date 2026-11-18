using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Deskmate
{
    // wall.sync's glass source: a colour field taken from the wallpaper tint.
    // The wallpaper picture itself is never drawn behind the glass.
    static class WallBackdrop
    {
        // Leave a little of the desktop visible through the tinted pane.
        // The pixels stay mostly tinted; this is not the old clear-window mode.
        public const float BehindOpacity = 0.85f;

        static readonly object gate = new object();
        static Color tint = Color.Empty;
        static bool active;
        static int version;

        public static bool Ready { get { lock (gate) return active; } }
        public static int Version { get { lock (gate) return version; } }

        public static void SetActive(bool on)
        {
            lock (gate)
            {
                if (active == on) return;
                active = on;
                version++;
            }
        }

        // Wallpaper-derived veil colour, set by WallApply on the UI thread
        public static void SetTint(Color c)
        {
            lock (gate)
            {
                if (tint.ToArgb() == c.ToArgb()) return;
                tint = c;
                version++;
            }
        }

        public static void Clear()
        {
            lock (gate)
            {
                if (!active && tint.IsEmpty) return;
                active = false;
                tint = Color.Empty;
                version++;
            }
        }

        // Fade a finished plate once so the desktop can show through a little.
        // PArgb pixels are premultiplied, so all four channels scale together.
        public static void Fade(Bitmap image)
        {
            if (image == null) return;
            lock (gate) { if (!active) return; }
            int k = (int)Math.Round(BehindOpacity * 255f);
            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, image.Width, image.Height);
                data = image.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
                int stride = data.Stride;
                int n = Math.Min(stride, image.Width * 4);
                var row = new byte[stride];
                for (int y = 0; y < image.Height; y++)
                {
                    IntPtr at = IntPtr.Add(data.Scan0, y * stride);
                    Marshal.Copy(at, row, 0, stride);
                    for (int i = 0; i < n; i++) row[i] = (byte)((row[i] * k + 127) / 255);
                    Marshal.Copy(row, 0, at, stride);
                }
            }
            finally
            {
                if (data != null) image.UnlockBits(data);
            }
        }

        // The dock keeps a solid tint behind its content and lets the desktop
        // show only at the rounded edges. The inside stays fully opaque.
        public static void FadeCore(Bitmap image, Rectangle bar, float radius)
        {
            if (image == null) return;
            lock (gate) { if (!active) return; }
            var r = Rectangle.Intersect(bar, new Rectangle(0, 0, image.Width, image.Height));
            if (r.Width < 4 || r.Height < 4) return;
            float rad = Math.Max(0f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f));
            float band = Math.Max(3f, Math.Min(16f, Math.Min(r.Width, r.Height) * 0.20f));
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float hw = r.Width / 2f - rad, hh = r.Height / 2f - rad;
            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, image.Width, image.Height);
                data = image.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
                int stride = data.Stride;
                var row = new byte[stride];
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    IntPtr at = IntPtr.Add(data.Scan0, y * stride);
                    Marshal.Copy(at, row, 0, stride);
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        float dx = Math.Abs(x + 0.5f - cx) - hw;
                        float dy = Math.Abs(y + 0.5f - cy) - hh;
                        float mx = Math.Max(dx, 0f), my = Math.Max(dy, 0f);
                        float d = (float)Math.Sqrt(mx * mx + my * my) + Math.Min(Math.Max(dx, dy), 0f) - rad;
                        float a = d >= 0f ? 0f : d <= -band ? 1f : -d / band;
                        int i = x * 4;
                        if (a <= 0f)
                        {
                            row[i] = 0; row[i + 1] = 0; row[i + 2] = 0; row[i + 3] = 0;
                        }
                        else if (a < 1f)
                        {
                            int k = (int)(a * 255f + 0.5f);
                            row[i] = (byte)((row[i] * k + 127) / 255);
                            row[i + 1] = (byte)((row[i + 1] * k + 127) / 255);
                            row[i + 2] = (byte)((row[i + 2] * k + 127) / 255);
                            row[i + 3] = (byte)((row[i + 3] * k + 127) / 255);
                        }
                    }
                    Marshal.Copy(row, 0, at, stride);
                }
            }
            finally
            {
                if (data != null) image.UnlockBits(data);
            }
        }

        // Fill a pane with a soft two-stop wash. The screen rectangle is only
        // validated; no desktop wallpaper is decoded or cropped here.
        public static bool Draw(Graphics g, Rectangle screenRect, Size dest)
        {
            if (g == null || screenRect.Width <= 0 || screenRect.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return false;
            Color c;
            lock (gate)
            {
                if (!active) return false;
                c = tint.IsEmpty ? Ink.Veil : tint;
            }
            bool light = c.GetBrightness() > 0.6f;
            Color top = Mix(c, Color.White, light ? 0.02f : 0.07f);
            Color bottom = Mix(c, Color.Black, light ? 0.04f : 0.09f);
            g.CompositingMode = CompositingMode.SourceOver;
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, dest.Width, dest.Height), top, bottom, 90f))
            {
                b.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(b, 0, 0, dest.Width, dest.Height);
            }
            return true;
        }

        static Color Mix(Color a, Color b, float k)
        {
            k = Math.Max(0f, Math.Min(1f, k));
            return Color.FromArgb(255,
                (int)Math.Round(a.R + (b.R - a.R) * k),
                (int)Math.Round(a.G + (b.G - a.G) * k),
                (int)Math.Round(a.B + (b.B - a.B) * k));
        }
    }
}
