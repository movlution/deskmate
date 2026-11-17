using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        class ThemeCardPicker : KitControl
        {
            public int SelectedIndex;
            public event Action<int> Picked;
            int hover = -1;

            static readonly string[] Labels = { "Gelap", "Terang", "Otomatis" };

            RectangleF CardRect(int i)
            {
                float s = Ui;
                float gap = 6 * s;
                float w = (Width - gap * 2) / 3f;
                return new RectangleF(i * (w + gap), 0, w, Height);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < 3; i++) if (CardRect(i).Contains(e.Location)) h = i;
                if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Cursor = Cursors.Default; Invalidate(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                for (int i = 0; i < 3; i++)
                {
                    if (CardRect(i).Contains(e.Location))
                    {
                        SelectedIndex = i;
                        Invalidate();
                        var h = Picked;
                        if (h != null) h(i);
                        return;
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Under(this));
                float s = Ui;
                using (var f = new Font(Ink.MenuFamily, 11f * s, GraphicsUnit.Pixel))
                    for (int i = 0; i < 3; i++)
                    {
                        var r = CardRect(i);
                        bool sel = i == SelectedIndex;
                        Color bg = i == 0 ? Color.FromArgb(28, 28, 30) : i == 1 ? Color.FromArgb(242, 242, 245) : Color.FromArgb(28, 28, 30);
                        Fill(g, r, 9 * s, bg);
                        if (i == 2)
                        {
                            var half = new RectangleF(r.X + r.Width / 2f, r.Y, r.Width / 2f, r.Height);
                            using (var b = new SolidBrush(Color.FromArgb(242, 242, 245)))
                            using (var clip = Ink.Round(r, 9 * s))
                            {
                                var old = g.Clip;
                                g.SetClip(clip);
                                g.FillRectangle(b, half);
                                g.Clip = old;
                            }
                        }
                        var pill = new RectangleF(r.X + r.Width * 0.25f, r.Y + 8 * s, r.Width * 0.5f, 5 * s);
                        Fill(g, pill, 2.5f * s, i == 1 ? Color.FromArgb(60, 60, 65) : Color.FromArgb(220, 220, 225));
                        Color txt = i == 1 ? Color.FromArgb(30, 30, 34) : i == 0 ? Color.FromArgb(235, 235, 240) : Color.FromArgb(140, 140, 148);
                        KitInk.Draw(g, Lang.T(Labels[i]), f, txt, new RectangleF(r.X, r.Bottom - 20 * s, r.Width, 18 * s), ContentAlignment.MiddleCenter);
                        if (sel)
                        {
                            using (var pen = new Pen(KitInk.Accent, 2f * s))
                            using (var path = Ink.Round(new RectangleF(r.X + s, r.Y + s, r.Width - 2 * s, r.Height - 2 * s), 8 * s))
                                g.DrawPath(pen, path);
                        }
                        else if (i == hover)
                        {
                            using (var pen = new Pen(KitInk.Faint, 1f * s))
                            using (var path = Ink.Round(new RectangleF(r.X + 0.5f * s, r.Y + 0.5f * s, r.Width - s, r.Height - s), 8.5f * s))
                                g.DrawPath(pen, path);
                        }
                    }
            }
        }
    }
}
