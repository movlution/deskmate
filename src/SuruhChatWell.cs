using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Rounded well around the text box
        class ChatWell : Panel
        {
            public bool Lit;

            public ChatWell()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Window);
                var w = FindForm() as KitWindow;
                float s = w != null ? w.Ui : 1;
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (var path = Ink.Round(r, Math.Min(20 * s, r.Height / 2)))
                {
                    using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                    // Focus: a stronger rim
                    using (var pen = new Pen(Lit ? KitInk.Sub : KitInk.Line, Math.Max(1f, 1.2f * s))) g.DrawPath(pen, path);
                }
            }
        }
    }
}
