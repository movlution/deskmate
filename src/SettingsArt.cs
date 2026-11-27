using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Deskmate
{
    // Illustrated Settings artwork (assets/settings): category icon tiles and hero banner scenes.
    // Both atlases are embedded by build.cmd; cell order matches assets/settings/make_art.py IDS.
    // Scaled copies are cached per size, so paints only blit. A missing atlas falls back to glyph tiles.
    static class SettingsArt
    {
        static readonly string[] Ids =
        {
            "general", "look", "island", "dock", "messages", "work", "remind",
            "ai", "agents", "ingat", "files", "system", "game", "keys",
        };
        const int IconCell = 128, IconCols = 7;
        const int SceneW = 400, SceneH = 160, SceneCols = 2;

        static Bitmap icons, scenes;
        static bool loaded;
        static readonly Dictionary<long, Bitmap> tileCache = new Dictionary<long, Bitmap>();
        static readonly Dictionary<long, Bitmap> sceneCache = new Dictionary<long, Bitmap>();

        static int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            return Array.IndexOf(Ids, id);
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            icons = LoadAtlas("SettingsIcons.png");
            scenes = LoadAtlas("SettingsScenes.png");
        }

        static Bitmap LoadAtlas(string name)
        {
            try
            {
                using (var stream = typeof(SettingsArt).Assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        using (var raw = new Bitmap(ms))
                        {
                            var bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
                            using (var g = Graphics.FromImage(bmp)) g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                            return bmp;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("settings art " + name + ": " + ex.Message); return null; }
        }

        public static bool Has(string id)
        {
            Load();
            return icons != null && IndexOf(id) >= 0;
        }

        // Draws the illustrated tile for a category into r (square). Returns false when there is no art.
        public static bool DrawTile(Graphics g, string id, RectangleF r)
        {
            int idx = IndexOf(id);
            if (idx < 0) return false;
            Load();
            if (icons == null) return false;
            int px = Math.Max(8, (int)Math.Round(Math.Min(r.Width, r.Height)));
            long key = ((long)idx << 32) | (uint)px;
            Bitmap bmp;
            if (!tileCache.TryGetValue(key, out bmp))
            {
                var src = new Rectangle((idx % IconCols) * IconCell, (idx / IconCols) * IconCell, IconCell, IconCell);
                bmp = Scale(icons, src, px, px);
                tileCache[key] = bmp;
            }
            g.DrawImageUnscaled(bmp, (int)Math.Round(r.X + (r.Width - px) / 2f), (int)Math.Round(r.Y + (r.Height - px) / 2f));
            return true;
        }

        // Width at the right of a card that DrawScene's opaque art covers; keep text left of it.
        public static float SceneReserve(string id, float cardHeight)
        {
            if (IndexOf(id) < 0) return 0f;
            Load();
            if (scenes == null) return 0f;
            float h = cardHeight * 1.12f;
            return h * (SceneW / (float)SceneH) * 0.48f - h * 0.06f;
        }

        // Paints the category scene along the right edge of a card, clipped to its rounded shape.
        // The art fades out toward the left by itself, so text on the left stays on a plain surface.
        public static void DrawScene(Graphics g, string id, RectangleF card, float radius)
        {
            int idx = IndexOf(id);
            if (idx < 0) return;
            Load();
            if (scenes == null) return;
            int h = Math.Max(16, (int)Math.Round(card.Height * 1.12f));
            int w = (int)Math.Round(h * (SceneW / (float)SceneH));
            int alpha = Ink.Light ? 92 : 100;
            long key = ((long)idx << 40) | ((long)(w & 0xFFFF) << 24) | ((long)(h & 0xFFFF) << 8) | (uint)alpha;
            Bitmap bmp;
            if (!sceneCache.TryGetValue(key, out bmp))
            {
                var src = new Rectangle((idx % SceneCols) * SceneW, (idx / SceneCols) * SceneH, SceneW, SceneH);
                bmp = Scale(scenes, src, w, h, alpha / 100f);
                sceneCache[key] = bmp;
            }
            var state = g.Save();
            g.SetClip(KitInk.Shape(card, radius), CombineMode.Intersect);
            int x = (int)Math.Round(card.Right - w + h * 0.06f);
            int y = (int)Math.Round(card.Bottom - h + h * 0.02f);
            g.DrawImageUnscaled(bmp, x, y);
            g.Restore(state);
        }

        static Bitmap Scale(Bitmap atlas, Rectangle src, int w, int h, float opacity = 1f)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            // Cut the cell out first: wrap modes would otherwise sample the neighbouring cells
            using (var cell = atlas.Clone(src, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var attr = new ImageAttributes())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY);
                if (opacity < 0.999f)
                    attr.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
                g.DrawImage(cell, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            }
            return bmp;
        }
    }
}
