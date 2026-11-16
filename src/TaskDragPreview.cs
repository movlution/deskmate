using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Independent of the drop target so the preview stays visible between columns.
    sealed class TaskDragPreview : Form
    {
        readonly string title;
        readonly char glyph;
        readonly float scale;
        readonly Timer timer = new Timer { Interval = 15 };
        readonly Stopwatch age = new Stopwatch();

        public TaskDragPreview(string title, char glyph, float scale)
        {
            this.title = title;
            this.glyph = glyph;
            this.scale = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            ClientSize = new Size((int)(224 * scale), (int)(42 * scale));
            using (var path = Ink.Round(ClientRectangle, 9 * scale)) Region = new Region(path);
            BackColor = Ink.Mix(KitInk.Group, KitInk.Accent, 0.17f);
            Opacity = Anim.Reduced ? 0.96 : 0.78;
            FollowPointer();
            age.Start();
            timer.Tick += delegate
            {
                FollowPointer();
                if (Opacity < 0.96) Opacity = Math.Min(0.96, 0.78 + age.ElapsedMilliseconds * 0.0018);
            };
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // No activation, no taskbar entry, and input passes through to the board.
                cp.ExStyle |= 0x08000000 | 0x80 | 0x20;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            base.WndProc(ref m);
        }

        void FollowPointer()
        {
            Point cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor).WorkingArea;
            int offset = (int)(15 * scale);
            int x = cursor.X + offset, y = cursor.Y + offset;
            if (x + Width > screen.Right) x = cursor.X - Width - offset;
            if (y + Height > screen.Bottom) y = cursor.Y - Height - offset;
            var at = new Point(x, y);
            if (Location != at) Location = at;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            using (var outline = Ink.Round(new RectangleF(1, 1, Width - 2, Height - 2), 8 * scale))
            using (var pen = new Pen(KitInk.Accent, Math.Max(1, scale))) g.DrawPath(pen, outline);
            KitInk.Draw(g, glyph.ToString(), Ink.Icons(15 * scale), KitInk.Accent,
                new RectangleF(9 * scale, 0, 22 * scale, Height), ContentAlignment.MiddleCenter);
            KitInk.Draw(g, title, Ink.Semi(14 * scale), KitInk.Text,
                new RectangleF(38 * scale, 0, Width - 50 * scale, Height), ContentAlignment.MiddleLeft);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
