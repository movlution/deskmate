using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Pill-drawn real Button for focus, readers
    class KitButton : Button
    {
        public bool Primary;
        bool hovered, pressed;

        public KitButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            KitInk.ByMouse = true;
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressed) { pressed = false; Invalidate(); }
        }
        protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            if (pressed) { pressed = false; }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            bool ringOn = Focused && ShowFocusCues && !KitInk.ByMouse;
            var kw = FindForm() as KitWindow;
            float s = kw != null ? kw.Ui : (Height > 0 ? Height / 32f : 1f);
            if (s <= 0) s = 1f;

            // Focused pill shrinks so its ring fits
            var r = ringOn ? new RectangleF(3.5f * s, 3.5f * s, Width - 7 * s, Height - 7 * s) : new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color bg;
            if (!Enabled)
            {
                bg = KitInk.Line;
            }
            else if (Primary)
            {
                bg = pressed ? (Ink.Light ? Color.FromArgb(255, 0, 0, 0) : Ink.Mix(KitInk.Text, KitInk.Window, 0.30f))
                             : (hovered ? Ink.Mix(KitInk.Text, KitInk.Window, 0.15f) : KitInk.Text);
            }
            else
            {
                bg = pressed ? (Ink.Light ? Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.20f) : Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.25f))
                             : (hovered ? KitInk.Hover : KitInk.Line);
            }

            if (pressed && Enabled) r.Offset(0, 0.75f * s);

            using (var path = Ink.Round(r, r.Height / 2f))
            using (var b = new SolidBrush(bg)) g.FillPath(b, path);

            KitInk.Draw(g, Text, Font, !Enabled ? KitInk.Faint : Primary ? KitInk.Window : KitInk.Text, r, ContentAlignment.MiddleCenter);
            if (ringOn)
            {
                var ring = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
                Color ringColor = KitInk.Accent;
                if (Island.Contrast(ringColor, bg) < 3.0 || (Primary && (ringColor == KitInk.Text || ringColor == bg)))
                    ringColor = Ink.Light ? Color.FromArgb(255, 22, 104, 232) : Color.FromArgb(255, 59, 130, 246);
                using (var path = Ink.Round(ring, ring.Height / 2f))
                using (var pen = new Pen(ringColor, 1.8f)) g.DrawPath(pen, path);
            }
        }
    }

    // Check row: pill with a drawn tick, so no native CheckBox box leaks in
    class KitCheck : KitButton
    {
        bool on, hovered;

        public event EventHandler CheckedChanged;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

        public bool Checked { get { return on; } set { if (on == value) return; on = value; var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); Invalidate(); } }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !on; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            bool ringOn = Focused && ShowFocusCues && !KitInk.ByMouse;
            var r = ringOn ? new RectangleF(3.5f, 3.5f, Width - 7, Height - 7) : new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Ink.Round(r, r.Height / 2f))
            using (var b = new SolidBrush(hovered ? KitInk.Hover : KitInk.Line)) g.FillPath(b, path);
            float box = Math.Min(16f, r.Height - 8f), cy = r.Y + r.Height / 2f, cx = r.X + 7f + box / 2f;
            var tick = new RectangleF(cx - box / 2, cy - box / 2, box, box);
            using (var pen = new Pen(on ? KitInk.Text : KitInk.Faint, 1.4f))
                using (var path = Ink.Round(tick, 4)) g.DrawPath(pen, path);
            if (on)
                using (var pen = new Pen(KitInk.Text, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(cx - box * 0.3f, cy), new PointF(cx - box * 0.06f, cy + box * 0.22f), new PointF(cx + box * 0.32f, cy - box * 0.22f) });
            KitInk.Draw(g, Text, Font, KitInk.Text, new RectangleF(r.X + 7f + box + 8f, r.Y, r.Width - box - 22f, r.Height), ContentAlignment.MiddleLeft);
            if (ringOn)
            {
                var ring = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
                using (var path = Ink.Round(ring, ring.Height / 2f))
                using (var pen = new Pen(KitInk.Text, 1.6f)) g.DrawPath(pen, path);
            }
        }
    }

    // List choice: button opening styled menu
    class KitPopup : KitButton
    {
        public string[] Items = new string[0];
        int selected;
        public event Action<int> Changed;

        public int Selected { get { return selected; } set { selected = value; Text = (value >= 0 && value < Items.Length ? Items[value] : "") + "  ▾"; } }

        public void Pick(int i)
        {
            if (i < 0 || i >= Items.Length || i == selected) return;
            Selected = i;
            var h = Changed;
            if (h != null) h(i);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var m = new ContextMenuStrip();
            for (int i = 0; i < Items.Length; i++)
            {
                int at = i;
                m.Items.Add(new ToolStripMenuItem(Items[i], null, delegate
                {
                    Selected = at;
                    var h = Changed;
                    if (h != null) h(at);
                }) { Checked = i == selected });
            }
            MenuLook.Apply(m);
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(this, new Point(0, Height));
        }
    }

    // Rounded group of rows with hairline separators
    class KitGroup : Panel
    {
        public KitGroup()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = KitInk.Group;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(Parent != null ? Parent.BackColor : KitInk.Window);
            var w = FindForm() as KitWindow;
            float s = w != null ? w.Ui : 1;
            using (var path = Ink.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12 * s))
            using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
            using (var pen = new Pen(KitInk.Line, 1f))
                for (int i = 1; i < Controls.Count; i++)
                {
                    int y = Controls[i].Top;
                    var row = Controls[i] as KitRow;
                    float from = row != null && row.Glyph != '\0' ? (56 + row.Lead) * s : 16 * s;
                    g.DrawLine(pen, from, y, Width - 16 * s, y);
                }
        }
    }

    // Setting row: title, detail, control right
    class KitRow : Panel
    {
        public string Title = "", Detail = "";
        // Tiny tail after the title, e.g. ".0.2"
        public string Suffix = "";
        public Control Field;
        // Icon tile before the title
        public char Glyph;
        public const char MarkGlyph = '\u0001';
        // Room left of the icon, e.g. a grip
        public float Lead;
        // Room kept right of the text, e.g. a chevron
        public float Trail;
        public bool Divider;

        public KitRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var w = FindForm() as KitWindow;
            float s = w != null ? w.Ui : 1;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float right = Field != null ? Field.Left - 12 * s : Width - (16 + Trail) * s;
            float x = (Glyph != '\0' ? 56 : 16) * s + Lead * s;
            if (Glyph != '\0') DrawGlyph(g, s);
            // Hairline between grip and icon
            if (Divider)
                using (var pen = new Pen(Ink.Mix(KitInk.Line, KitInk.Sub, 0.45f), Math.Max(1f, s)))
                    g.DrawLine(pen, (Lead - 2) * s, Height * 0.3f, (Lead - 2) * s, Height * 0.7f);
            using (var f = new Font(Ink.MenuFamily, 13.5f * s, GraphicsUnit.Pixel))
            using (var d = new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel))
            {
                bool two = Detail.Length > 0;
                var tr = new RectangleF(x, two ? 7 * s : 0, right - x, two ? 22 * s : Height);
                KitInk.Draw(g, Title, f, Enabled ? KitInk.Text : KitInk.Faint, tr, ContentAlignment.MiddleLeft);
                if (Suffix.Length > 0) DrawSuffix(g, f, tr, s);
                if (two) KitInk.Draw(g, Detail, d, KitInk.Sub, new RectangleF(x, 28 * s, right - x, 20 * s), ContentAlignment.MiddleLeft);
            }
        }

        void DrawGlyph(Graphics g, float s)
        {
            KitInk.Smooth(g);
            // Tall rows keep the tile beside the title
            var tile = new RectangleF((14 + Lead) * s, Height > 80 * s ? 16 * s : (Height - 30 * s) / 2, 30 * s, 30 * s);
            if (Glyph == MarkGlyph)
            {
                g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(tile));
                return;
            }
            using (var path = Ink.Round(tile, 8 * s))
            using (var b = new SolidBrush(KitInk.Hover)) g.FillPath(b, path);
            using (var icons = new Font(Ink.IconFamily, 15f * s, GraphicsUnit.Pixel))
                KitInk.Draw(g, Glyph.ToString(), icons, Enabled ? KitInk.Text : KitInk.Faint, tile, ContentAlignment.MiddleCenter);
        }

        // Small text on the title's baseline
        void DrawSuffix(Graphics g, Font f, RectangleF tr, float s)
        {
            const TextFormatFlags tight = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            using (var small = new Font(Ink.MenuFamily, 9.5f * s, GraphicsUnit.Pixel))
            {
                int padded = TextRenderer.MeasureText(g, Title, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                int bare = TextRenderer.MeasureText(g, Title, f, Size.Empty, tight).Width;
                float x = tr.X + (padded - bare) / 2f + bare + 1 * s;
                float baseline = tr.Y + (tr.Height - f.Height) / 2f + Ascent(f);
                TextRenderer.DrawText(g, Suffix, small, new Point((int)Math.Round(x), (int)Math.Round(baseline - Ascent(small))), KitInk.Sub, tight);
            }
        }

        static float Ascent(Font f) { return f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style); }
    }
}
