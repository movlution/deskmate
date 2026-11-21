using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Soft squircle drawn over a real Button for focus, readers
    class KitButton : Button
    {
        public bool Primary;
        protected bool hovered, pressed;

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

        // Window scale, else derived from a 32px control height
        protected float UiScale
        {
            get
            {
                var kw = FindForm() as KitWindow;
                float s = kw != null ? kw.Ui : (Height > 0 ? Height / 32f : 1f);
                return s > 0 ? s : 1f;
            }
        }

        protected bool RingOn { get { return Focused && ShowFocusCues && !KitInk.ByMouse; } }

        // Focused body shrinks so the ring sits outside it, clear of the fill
        protected RectangleF Body(float s)
        {
            return RingOn ? new RectangleF(3.5f * s, 3.5f * s, Width - 7 * s, Height - 7 * s) : new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        }

        // Soft squircle at any height: 36 px keeps 12, compact 30 px controls get 10, never a capsule
        protected static float Radius(RectangleF r, float s) { return Math.Min(r.Height * 0.35f, Ink.RadiusControl * s); }

        protected void DrawRing(Graphics g, RectangleF body, float s)
        {
            if (!RingOn) return;
            float grow = 2.5f * s;
            KitInk.FocusRing(g, RectangleF.Inflate(body, grow, grow), Radius(body, s) + grow, KitInk.Under(this), s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = UiScale;
            var r = Body(s);
            Color bg, fg;
            if (!Enabled) { bg = KitInk.Line; fg = KitInk.Faint; }
            else if (Primary)
            {
                // Hover and press move away from the label ink: contrast only grows
                bg = KitInk.Deepen(KitInk.Accent, KitInk.OnAccent, pressed ? 0.2f : hovered ? 0.1f : 0f);
                fg = KitInk.OnAccent;
            }
            else
            {
                bg = pressed ? KitInk.Tint(0.22f) : hovered ? KitInk.Tint(0.17f) : KitInk.Line;
                fg = KitInk.Text;
            }

            if (pressed && Enabled) r.Offset(0, 0.75f * s);
            KitInk.FillRound(g, r, Radius(r, s), bg);
            KitInk.Draw(g, Text, Font, fg, r, ContentAlignment.MiddleCenter);
            DrawRing(g, r, s);
        }
    }

    // Check row: soft body with a drawn box, so no native CheckBox box leaks in
    class KitCheck : KitButton
    {
        bool on;

        public event EventHandler CheckedChanged;

        public bool Checked { get { return on; } set { if (on == value) return; on = value; var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); Invalidate(); } }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !on; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = UiScale;
            var r = Body(s);
            bool live = Enabled;
            KitInk.FillRound(g, r, Radius(r, s), live && pressed ? KitInk.Tint(0.22f) : live && hovered ? KitInk.Tint(0.17f) : KitInk.Line);
            float box = Math.Min(16f * s, r.Height - 8f * s), cy = r.Y + r.Height / 2f, cx = r.X + 9f * s + box / 2f;
            var tick = new RectangleF(cx - box / 2, cy - box / 2, box, box);
            float corner = 5f * s;
            if (on)
            {
                KitInk.FillRound(g, tick, corner, live ? KitInk.Accent : KitInk.Disabled);
                var pen = KitInk.PenOf(live ? KitInk.OnAccent : KitInk.Line, 1.8f * s);
                g.DrawLines(pen, new[] { new PointF(cx - box * 0.26f, cy + box * 0.01f), new PointF(cx - box * 0.06f, cy + box * 0.2f), new PointF(cx + box * 0.27f, cy - box * 0.19f) });
            }
            else KitInk.StrokeRound(g, RectangleF.Inflate(tick, -0.75f * s, -0.75f * s), corner, live ? KitInk.Sub : KitInk.Disabled, 1.5f * s);
            KitInk.Draw(g, Text, Font, live ? KitInk.Text : KitInk.Faint, new RectangleF(tick.Right + 8f * s, r.Y, r.Right - tick.Right - 14f * s, r.Height), ContentAlignment.MiddleLeft);
            DrawRing(g, r, s);
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

    // Soft card of rows with hairline separators; elevation by tone step, no border or shadow
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
            KitInk.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Ink.RadiusList * s, KitInk.Group);
            var pen = KitInk.PenOf(KitInk.Line, 1f);
            for (int i = 1; i < Controls.Count; i++)
            {
                int y = Controls[i].Top;
                var row = Controls[i] as KitRow;
                // Hairline starts where the text starts
                float from = row != null ? row.TextX(s) : 16 * s;
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
            float x = TextX(s);
            if (Glyph != '\0') DrawGlyph(g, s);
            // Hairline between grip and icon
            if (Divider)
                g.DrawLine(KitInk.PenOf(Ink.Mix(KitInk.Line, KitInk.Sub, 0.45f), Math.Max(1f, s)), (Lead - 2) * s, Height * 0.3f, (Lead - 2) * s, Height * 0.7f);
            Font f = Ink.Text(13f * s), d = Ink.Text(11.5f * s);
            bool two = Detail.Length > 0;
            // Two lines sit centred as one block; tall rows keep them beside the top-aligned glyph
            float top = Height > 80 * s ? 8 * s : Math.Max(0, (Height - 38 * s) / 2);
            var tr = new RectangleF(x, two ? top : 0, right - x, two ? 20 * s : Height);
            KitInk.Draw(g, Title, f, Enabled ? KitInk.Text : KitInk.Faint, tr, ContentAlignment.MiddleLeft);
            if (Suffix.Length > 0) DrawSuffix(g, f, tr, s);
            if (two) KitInk.Draw(g, Detail, d, KitInk.Sub, new RectangleF(x, top + 19 * s, right - x, 18 * s), ContentAlignment.MiddleLeft);
        }

        // Text column: after the glyph when there is one
        public float TextX(float s) { return ((Glyph != '\0' ? 44 : 16) + Lead) * s; }

        void DrawGlyph(Graphics g, float s)
        {
            KitInk.Smooth(g);
            // Tall rows keep the glyph beside the title
            float size = Glyph == MarkGlyph ? 24 : 20;
            var box = new RectangleF((14 + Lead) * s, Height > 80 * s ? 9 * s : (Height - size * s) / 2, size * s, size * s);
            if (Glyph == MarkGlyph)
            {
                g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(box));
                return;
            }
            // Rows stay monochrome: color belongs to the category tiles
            KitInk.Draw(g, Ink.Str(Glyph), Ink.Icons(15f * s), Enabled ? KitInk.Sub : KitInk.Faint, box, ContentAlignment.MiddleCenter);
        }

        // Small text on the title's baseline
        void DrawSuffix(Graphics g, Font f, RectangleF tr, float s)
        {
            const TextFormatFlags tight = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var small = Ink.Text(9.5f * s);
            int padded = TextRenderer.MeasureText(g, Title, f, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            int bare = TextRenderer.MeasureText(g, Title, f, Size.Empty, tight).Width;
            float x = tr.X + (padded - bare) / 2f + bare + 1 * s;
            float baseline = tr.Y + (tr.Height - f.Height) / 2f + Ascent(f);
            TextRenderer.DrawText(g, Suffix, small, new Point((int)Math.Round(x), (int)Math.Round(baseline - Ascent(small))), KitInk.Sub, tight);
        }

        static float Ascent(Font f) { return f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style); }
    }
}
