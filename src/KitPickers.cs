using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Curated accent swatch picker ala macOS System Settings
    class KitPalette : KitControl
    {
        public string[][] Presets = new string[0][];
        public string[][] PresetsLight = new string[0][];
        int selected = -1;
        int hovered = -1;
        public event Action<int, string> Picked;

        public KitPalette()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        public string SelectedHex
        {
            get { return selected >= 0 && selected < Presets.Length ? Presets[selected][1] : ""; }
            set
            {
                selected = -1;
                for (int i = 0; i < Presets.Length; i++)
                {
                    if (Presets[i][1] == value || (PresetsLight != null && i < PresetsLight.Length && PresetsLight[i][1] == value))
                    {
                        selected = i;
                        break;
                    }
                }
                Invalidate();
            }
        }

        float ItemStep { get { return Presets.Length > 0 ? Math.Min(31f * Ui, (Width - 4f * Ui) / Presets.Length) : 31f * Ui; } }

        PointF Center(int i)
        {
            float step = ItemStep;
            float totalW = step * Presets.Length;
            float startX = (Width - totalW) / 2f + step / 2f;
            return new PointF(startX + i * step, Height / 2f);
        }

        int HitTest(Point pt)
        {
            float r = 12f * Ui;
            for (int i = 0; i < Presets.Length; i++)
            {
                var c = Center(i);
                float dx = pt.X - c.X, dy = pt.Y - c.Y;
                if (dx * dx + dy * dy <= r * r) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            if (hit != hovered) { hovered = hit; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                int hit = HitTest(e.Location);
                if (hit >= 0) Pick(hit);
            }
        }

        void Pick(int i)
        {
            if (i < 0 || i >= Presets.Length) return;
            selected = i;
            Invalidate();
            var table = Ink.Light && PresetsLight != null && PresetsLight.Length == Presets.Length ? PresetsLight : Presets;
            var h = Picked;
            if (h != null) h(i, table[i][1]);
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || base.IsInputKey(key); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Pick(Math.Max(0, (selected >= 0 ? selected : 0) - 1));
            else if (e.KeyCode == Keys.Right) Pick(Math.Min(Presets.Length - 1, (selected >= 0 ? selected : 0) + 1));
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { if (selected >= 0) Pick(selected); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            var table = Ink.Light && PresetsLight != null && PresetsLight.Length == Presets.Length ? PresetsLight : Presets;
            float r = 8.5f * Ui;

            for (int i = 0; i < Presets.Length; i++)
            {
                var pt = Center(i);
                Color dotColor;
                string hex = table[i][1];
                if (string.IsNullOrEmpty(hex))
                {
                    dotColor = Ink.Light ? Color.FromArgb(255, 142, 142, 147) : Color.FromArgb(255, 160, 160, 165);
                }
                else
                {
                    try { dotColor = ColorTranslator.FromHtml(hex); }
                    catch { dotColor = KitInk.Text; }
                }

                if (i == selected)
                {
                    float ringR = r + 3.2f * Ui;
                    using (var pen = new Pen(dotColor, 2f * Ui))
                        g.DrawEllipse(pen, pt.X - ringR, pt.Y - ringR, ringR * 2, ringR * 2);
                }
                else if (i == hovered)
                {
                    float ringR = r + 3f * Ui;
                    using (var pen = new Pen(Color.FromArgb(90, dotColor), 1.5f * Ui))
                        g.DrawEllipse(pen, pt.X - ringR, pt.Y - ringR, ringR * 2, ringR * 2);
                }

                using (var b = new SolidBrush(dotColor))
                    g.FillEllipse(b, pt.X - r, pt.Y - r, r * 2, r * 2);
                if (i == selected)
                {
                    // A check makes the selection unmistakable without relying on
                    // colour alone, and keeps the swatch premium at small sizes.
                    Color check = dotColor.GetBrightness() > 0.62f ? Color.FromArgb(235, 20, 22, 27) : Color.White;
                    using (var pen = new Pen(check, 1.9f * Ui) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        float x = pt.X - 2.8f * Ui, y = pt.Y + 0.2f * Ui;
                        g.DrawLine(pen, x, y, x + 1.5f * Ui, y + 1.6f * Ui);
                        g.DrawLine(pen, x + 1.5f * Ui, y + 1.6f * Ui, x + 4.1f * Ui, y - 1.8f * Ui);
                    }
                }
            }

            if (Focused && ShowFocusCues && !KitInk.ByMouse)
            {
                var ring = KitControl.Inside(new RectangleF(0, 0, Width, Height), Width, Height);
                using (var path = Ink.Round(ring, 8 * Ui))
                using (var pen = new Pen(KitInk.Text, 1.5f)) g.DrawPath(pen, path);
            }
        }
    }

    // Slider: arrows, Home/End, drag
    class KitSlider : KitControl
    {
        public int Minimum, Maximum = 100, Step = 1, Value;
        public Func<int, string> Format = v => v.ToString();
        // Value text left of the track
        public bool ShowValue;
        public event Action<int> Changed;
        bool dragging;

        void Set(int v)
        {
            v = Math.Max(Minimum, Math.Min(Maximum, (int)Math.Round((double)(v - Minimum) / Step) * Step + Minimum));
            if (v == Value) return;
            Value = v;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            var h = Changed;
            if (h != null) h(v);
        }

        float LabelW { get { return ShowValue ? 84 * Ui : 0; } }

        RectangleF Track { get { float s = Ui, l = LabelW; return new RectangleF(l + 10 * s, Height / 2f - 3 * s, Width - l - 20 * s, 6 * s); } }

        int At(int x) { var t = Track; return Minimum + (int)Math.Round((Maximum - Minimum) * Ease.Clamp01((x - t.X) / Math.Max(1, t.Width))); }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); dragging = true; Capture = true; Set(At(e.X)); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) Set(At(e.X)); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }

        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Set(Value - Step);
            else if (e.KeyCode == Keys.Right) Set(Value + Step);
            else if (e.KeyCode == Keys.Home) Set(Minimum);
            else if (e.KeyCode == Keys.End) Set(Maximum);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = Ui, f = (float)(Value - Minimum) / Math.Max(1, Maximum - Minimum);
            var t = Track;
            Fill(g, t, t.Height / 2, KitInk.Line);
            Fill(g, new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * f), t.Height), t.Height / 2, Enabled ? KitInk.Text : KitInk.Disabled);
            float r = ((dragging || Hovered) && Enabled ? 9 : 8) * s, cx = t.X + t.Width * f, cy = t.Y + t.Height / 2;
            using (var b = new SolidBrush(Enabled ? KitInk.Text : KitInk.Disabled)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            using (var pen = new Pen(Enabled ? KitInk.Line : KitInk.Disabled, 1f)) g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            if (ShowValue)
                using (var f2 = new Font(Ink.MenuFamily, 12f * s, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, Format(Value), f2, new Rectangle(0, 0, (int)LabelW, Height), Enabled ? KitInk.Sub : KitInk.Faint,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            Ring(g, new RectangleF(1, 1, Width - 3, Height - 3), Height / 2f);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

        class Access : ControlAccessibleObject
        {
            readonly KitSlider owner;
            public Access(KitSlider owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.Slider; } }
            public override string Value { get { return owner.Format(owner.Value); } set { int v; if (int.TryParse(value, out v)) owner.Set(v); } }
        }
    }

}
