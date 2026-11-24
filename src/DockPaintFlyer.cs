using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class DockWindow
        {
            sealed class DockFlyer
            {
                public Bitmap Img;
                public Rectangle Src;
                public RectangleF From;
                public float Vx, Vy, Spin, Delay;
            }

            readonly List<DockFlyer> dockFlyers = new List<DockFlyer>();
            readonly List<Bitmap> dockFlyerImgs = new List<Bitmap>();
            DateTime dockFlyUtc = DateTime.MinValue;
            const double DockScatterSecs = 0.44;

            bool DockFlyersMoving(DateTime now)
            {
                if (dockFlyers.Count == 0) return false;
                if ((now - dockFlyUtc).TotalSeconds < DockScatterSecs + 0.12) return true;
                ClearDockFlyers();
                return false;
            }

            void ClearDockFlyers()
            {
                dockFlyers.Clear();
                foreach (var b in dockFlyerImgs) b.Dispose();
                dockFlyerImgs.Clear();
            }

            void ShatterDockIcon(DockApp a, RectangleF r)
            {
                if (Anim.Reduced || a == null || r.Width < 8 || r.Height < 8) return;
                Bitmap src = IconFor(a.Key);
                Bitmap bmp;
                if (src != null) bmp = new Bitmap(src);
                else
                {
                    bmp = new Bitmap(48, 48, PixelFormat.Format32bppPArgb);
                    using (var bg = Graphics.FromImage(bmp))
                    {
                        var bp = new Painter { G = bg, T = p.T };
                        bg.SmoothingMode = SmoothingMode.AntiAlias;
                        bp.FillRound(new RectangleF(2, 2, 44, 44), 12, p.T.FillHover);
                        bp.TextCenter(a.Name.Length > 0 ? a.Name.Substring(0, 1).ToUpperInvariant() : "?", Ink.Semi(22f), p.T.Text, 24, 24);
                    }
                }
                dockFlyerImgs.Add(bmp);
                const int cols = 4, rows = 4;
                float dw = r.Width / cols, dh = r.Height / rows;
                float sw = (float)bmp.Width / cols, sh = (float)bmp.Height / rows;
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        float nx = (x + 0.5f) / cols - 0.5f;
                        float ny = (y + 0.5f) / rows - 0.5f;
                        float hash = ((x * 7 + y * 13) % 9) / 9f - 0.5f;
                        dockFlyers.Add(new DockFlyer
                        {
                            Img = bmp,
                            Src = Rectangle.Round(new RectangleF(x * sw, y * sh, sw, sh)),
                            From = new RectangleF(r.X + x * dw, r.Y + y * dh, dw, dh),
                            Vx = nx * 170 + hash * 45,
                            Vy = ny * 110 - 115 - Math.Abs(hash) * 55,
                            Spin = nx * 300 + hash * 120,
                            Delay = Math.Abs(nx) * 0.035f
                        });
                    }
                }
                dockFlyUtc = DateTime.UtcNow;
            }

            void DrawDockFlyers(DateTime now)
            {
                if (dockFlyers.Count == 0) return;
                double t0 = (now - dockFlyUtc).TotalSeconds;
                bool alive = false;
                var keepMode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.Bilinear;
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix();
                    foreach (var f in dockFlyers)
                    {
                        float t = (float)((t0 - f.Delay) / DockScatterSecs);
                        if (t >= 1) continue;
                        alive = true;
                        t = Math.Max(0, t);
                        float x = f.From.X + f.Vx * t * (float)DockScatterSecs;
                        float y = f.From.Y + (f.Vy * t + 420 * t * t) * (float)DockScatterSecs;
                        float alpha = (1 - t) * (1 - t);
                        cm.Matrix33 = alpha;
                        ia.SetColorMatrix(cm);
                        var dst = new RectangleF(x, y, f.From.Width, f.From.Height);
                        var st = g.Save();
                        g.TranslateTransform(dst.X + dst.Width / 2, dst.Y + dst.Height / 2);
                        g.RotateTransform(f.Spin * t);
                        var pts = new[] { new PointF(-dst.Width / 2, -dst.Height / 2), new PointF(dst.Width / 2, -dst.Height / 2), new PointF(-dst.Width / 2, dst.Height / 2) };
                        g.DrawImage(f.Img, pts, f.Src, GraphicsUnit.Pixel, ia);
                        g.Restore(st);
                    }
                }
                g.InterpolationMode = keepMode;
                if (!alive) ClearDockFlyers();
            }
        }
    }
}
