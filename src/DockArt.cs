using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Deskmate
{
    // Illustrated Dock system buttons (assets/dock): Start, Task view, Work, Suruh, Rak.
    // The atlas is embedded by build.cmd; cell order matches assets/dock/make_icons.py IDS.
    // Scaled copies are cached per pixel size, so paints only blit. A missing atlas falls back to glyphs.
    static class DockArt
    {
        static readonly string[] Ids = { "start", "taskview", "work", "suruh", "shelf" };
        const int Cell = 128;

        static Bitmap atlas;
        static bool loaded;
        static readonly Dictionary<long, Bitmap> cache = new Dictionary<long, Bitmap>();

        static Bitmap Atlas()
        {
            if (loaded) return atlas;
            loaded = true;
            try
            {
                using (var stream = typeof(DockArt).Assembly.GetManifestResourceStream("DockIcons.png"))
                {
                    if (stream == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        using (var raw = new Bitmap(ms))
                        {
                            atlas = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
                            using (var g = Graphics.FromImage(atlas)) g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("dock art: " + ex.Message); atlas = null; }
            return atlas;
        }

        public static bool Has(string id)
        {
            return Array.IndexOf(Ids, id) >= 0 && Atlas() != null;
        }

        // The tile for id at px × px device pixels, or null when there is no art.
        public static Bitmap Get(string id, int px)
        {
            int idx = Array.IndexOf(Ids, id);
            if (idx < 0 || px < 4) return null;
            var src = Atlas();
            if (src == null) return null;
            px = Math.Min(px, 512);
            long key = ((long)idx << 32) | (uint)px;
            Bitmap bmp;
            if (cache.TryGetValue(key, out bmp)) return bmp;
            if (cache.Count > 40)
            {
                foreach (var b in cache.Values) b.Dispose();
                cache.Clear();
            }
            bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            // Cut the cell out first: wrap modes would otherwise sample the neighbouring cells
            using (var cell = src.Clone(new Rectangle(idx * Cell, 0, Cell, Cell), PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var attr = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(cell, new Rectangle(0, 0, px, px), 0, 0, Cell, Cell, GraphicsUnit.Pixel, attr);
            }
            cache[key] = bmp;
            return bmp;
        }
    }
}
