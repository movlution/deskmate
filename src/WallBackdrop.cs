using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Deskmate
{
    // wall.sync's glass source: a colour field taken from the wallpaper tint.
    // The wallpaper picture itself is never drawn behind the glass.
    static class WallBackdrop
    {
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
