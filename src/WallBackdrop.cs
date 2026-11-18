using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Deskmate
{
    // wall.sync's glass source: the wallpaper, never the windows in front of it.
    // A small desktop-sized bitmap is composed once; every pane then samples its
    // own corner of that picture.
    static class WallBackdrop
    {
        const int CanvasDiv = 4, Fill = 10, Fit = 6, Stretch = 2, Centre = 0, Span = 22, Tile = 1;

        sealed class Snapshot : IDisposable
        {
            public Bitmap Image;
            public Rectangle Bounds;
            public Color Background;
            public void Dispose() { if (Image != null) Image.Dispose(); Image = null; }
        }

        static readonly object gate = new object();
        static Bitmap canvas;
        static Rectangle screenBounds;
        static Color background = Color.Black;
        static bool active;
        static int loadGen, version;

        public static bool Ready { get { lock (gate) return active && canvas != null; } }
        public static int Version { get { lock (gate) return version; } }

        public static void SetActive(bool on)
        {
            lock (gate) active = on;
        }

        public static void Clear()
        {
            lock (gate)
            {
                loadGen++;
                active = false;
                if (canvas != null) { canvas.Dispose(); canvas = null; }
                screenBounds = Rectangle.Empty;
                background = Color.Black;
                version++;
            }
        }

        // Called off the UI thread. A late result is dropped if Clear/Reload ran.
        public static void Reload()
        {
            int token;
            lock (gate) token = ++loadGen;
            Snapshot snap = null;
            try { snap = Build(); }
            catch (Exception ex) { Log.Write("wall backdrop: " + ex.Message); }
            lock (gate)
            {
                if (token != loadGen) { if (snap != null) snap.Dispose(); return; }
                if (canvas != null) canvas.Dispose();
                canvas = snap == null ? null : snap.Image;
                screenBounds = snap == null ? Rectangle.Empty : snap.Bounds;
                background = snap == null ? Color.Black : snap.Background;
                version++;
            }
        }

        // True once the pane was filled from the wallpaper. False asks the caller
        // to fall back to the live screen (only before wall.sync has loaded).
        public static bool Draw(Graphics g, Rectangle screenRect, Size dest)
        {
            if (g == null || screenRect.Width <= 0 || screenRect.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return false;
            lock (gate)
            {
                if (!active || canvas == null || screenBounds.IsEmpty) return false;
                g.CompositingMode = CompositingMode.SourceOver;
                g.Clear(background);
                var part = Rectangle.Intersect(screenRect, screenBounds);
                if (part.IsEmpty) return true;
                float sx = canvas.Width / (float)screenBounds.Width;
                float sy = canvas.Height / (float)screenBounds.Height;
                float dx = dest.Width / (float)screenRect.Width;
                float dy = dest.Height / (float)screenRect.Height;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.None;
                g.DrawImage(canvas,
                    new RectangleF((part.X - screenRect.X) * dx, (part.Y - screenRect.Y) * dy, part.Width * dx, part.Height * dy),
                    new RectangleF((part.X - screenBounds.X) * sx, (part.Y - screenBounds.Y) * sy, part.Width * sx, part.Height * sy),
                    GraphicsUnit.Pixel);
                return true;
            }
        }

        static Snapshot Build()
        {
            var vs = SystemInformation.VirtualScreen;
            if (vs.Width < 1 || vs.Height < 1) return null;
            var bg = ReadBackground();
            int cw = Math.Max(1, (int)Math.Round(vs.Width / (double)CanvasDiv));
            int ch = Math.Max(1, (int)Math.Round(vs.Height / (double)CanvasDiv));
            var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppRgb);
            try
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(bg);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.None;
                    string path = WallpaperPath();
                    if (path.Length > 0 && File.Exists(path))
                    {
                        try
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var img = Image.FromStream(fs, false, false))
                                Paint(g, vs, cw / (float)vs.Width, ch / (float)vs.Height, img, ReadStyle(), bg);
                        }
                        catch (Exception ex) { Log.Write("wall backdrop image: " + ex.Message); }
                    }
                }
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
            return new Snapshot { Image = bmp, Bounds = vs, Background = bg };
        }

        static void Paint(Graphics g, Rectangle vs, float sx, float sy, Image img, int style, Color bg)
        {
            if (style == Span) { PaintMonitor(g, vs, vs, sx, sy, img, Fill); return; }
            if (style == Tile) { PaintTile(g, vs, sx, sy, img); return; }
            var screens = Screen.AllScreens;
            if (screens.Length == 0) { PaintMonitor(g, vs, vs, sx, sy, img, style); return; }
            foreach (var screen in screens) PaintMonitor(g, screen.Bounds, vs, sx, sy, img, style);
        }

        static void PaintMonitor(Graphics g, Rectangle mon, Rectangle vs, float sx, float sy, Image img, int style)
        {
            var clip = new RectangleF((mon.X - vs.X) * sx, (mon.Y - vs.Y) * sy, mon.Width * sx, mon.Height * sy);
            var state = g.Save();
            g.SetClip(clip);
            float iw = img.Width, ih = img.Height;
            float dw, dh, dx, dy;
            if (style == Stretch)
            {
                dw = mon.Width; dh = mon.Height; dx = mon.X; dy = mon.Y;
            }
            else
            {
                float scale = style == Fit ? Math.Min(mon.Width / iw, mon.Height / ih)
                    : style == Centre ? 1f : Math.Max(mon.Width / iw, mon.Height / ih);
                dw = iw * scale; dh = ih * scale;
                dx = mon.X + (mon.Width - dw) / 2f;
                dy = mon.Y + (mon.Height - dh) / 2f;
            }
            g.DrawImage(img,
                new RectangleF((dx - vs.X) * sx, (dy - vs.Y) * sy, dw * sx, dh * sy),
                new RectangleF(0, 0, iw, ih), GraphicsUnit.Pixel);
            g.Restore(state);
        }

        static void PaintTile(Graphics g, Rectangle vs, float sx, float sy, Image img)
        {
            float iw = img.Width, ih = img.Height;
            for (float y = vs.Top; y < vs.Bottom; y += ih)
                for (float x = vs.Left; x < vs.Right; x += iw)
                    g.DrawImage(img, new RectangleF((x - vs.X) * sx, (y - vs.Y) * sy, iw * sx, ih * sy),
                        new RectangleF(0, 0, iw, ih), GraphicsUnit.Pixel);
        }

        static int ReadStyle()
        {
            int style = Fill;
            bool tile = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (k != null)
                    {
                        int v;
                        if (int.TryParse(Convert.ToString(k.GetValue("WallpaperStyle"), CultureInfo.InvariantCulture), out v)) style = v;
                        tile = Convert.ToString(k.GetValue("TileWallpaper"), CultureInfo.InvariantCulture) == "1";
                    }
                }
            }
            catch (Exception ex) { Log.Write("wall backdrop style: " + ex.Message); }
            if (tile) return Tile;
            if (style == Span || style == Fit || style == Stretch || style == Centre) return style;
            return Fill;
        }

        static Color ReadBackground()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                {
                    var v = k == null ? null : k.GetValue("Background") as string;
                    var part = v == null ? new string[0] : v.Split(' ');
                    int r, g, b;
                    if (part.Length == 3 && int.TryParse(part[0], out r) && int.TryParse(part[1], out g) && int.TryParse(part[2], out b))
                        return Color.FromArgb(255, Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, b)));
                }
            }
            catch (Exception ex) { Log.Write("wall backdrop colour: " + ex.Message); }
            return Color.Black;
        }

        static string WallpaperPath()
        {
            try
            {
                var sb = new StringBuilder(520);
                if (SpiGetString(0x73, 520, sb, 0))
                {
                    string path = sb.ToString();
                    if (File.Exists(path)) return path;
                }
                // Slideshow and some themes keep the current picture here
                string cached = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(cached)) return cached;
            }
            catch (Exception ex) { Log.Write("wall backdrop path: " + ex.Message); }
            return "";
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
        static extern bool SpiGetString(uint action, uint size, StringBuilder buf, uint ini);
    }
}
