using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Windows keeps appearance choices as three small desktop previews:
        // System sits first, then Light and Dark. The label lives outside the
        // thumbnail so a split preview never fights its own text.
        class ThemeCardPicker : KitControl
        {
            public int SelectedIndex;
            public event Action<int> Picked;
            int hover = -1;

            static readonly string[] Labels = { "Sistem", "Terang", "Gelap" };

            static readonly Color DarkCanvas = Color.FromArgb(255, 32, 32, 32);
            static readonly Color LightCanvas = Color.FromArgb(255, 243, 243, 243);
            static readonly Color DarkWindow = Color.FromArgb(255, 43, 43, 43);
            static readonly Color LightWindow = Color.FromArgb(255, 255, 255, 255);
            static readonly Color DarkTitle = Color.FromArgb(255, 58, 58, 58);
            static readonly Color LightTitle = Color.FromArgb(255, 237, 237, 237);
            static readonly Color DarkLine = Color.FromArgb(255, 82, 82, 82);
            static readonly Color LightLine = Color.FromArgb(255, 209, 209, 209);

            float LabelH { get { return 14f * Ui; } }
            float LabelGap { get { return 4f * Ui; } }

            RectangleF TileRect(int i)
            {
                float s = Ui;
                float gap = 6 * s;
                float w = (Width - gap * 2) / 3f;
                float h = Math.Max(1f, Height - LabelH - LabelGap);
                return new RectangleF(i * (w + gap), 0, w, h);
            }

            RectangleF LabelRect(int i)
            {
                var t = TileRect(i);
                return new RectangleF(t.X, t.Bottom + LabelGap, t.Width, Math.Max(1f, Height - t.Bottom - LabelGap));
            }

            RectangleF HitRect(int i)
            {
                var t = TileRect(i);
                return new RectangleF(t.X, 0, t.Width, Height);
            }

            void Choose(int i)
            {
                i = Math.Max(0, Math.Min(Labels.Length - 1, i));
                bool changed = SelectedIndex != i;
                SelectedIndex = i;
                Invalidate();
                if (changed)
                {
                    AccessibilityNotifyClients(AccessibleEvents.Selection, i);
                    var h = Picked;
                    if (h != null) h(i);
                }
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

            class Access : ControlAccessibleObject
            {
                readonly ThemeCardPicker owner;
                public Access(ThemeCardPicker owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
                public override int GetChildCount() { return Labels.Length; }
                public override AccessibleObject GetChild(int i) { return i >= 0 && i < Labels.Length ? new Item(owner, i) : null; }
                public override AccessibleObject GetSelected() { return GetChild(owner.SelectedIndex); }
                public override AccessibleObject GetFocused() { return owner.Focused ? GetSelected() : null; }
            }

            class Item : AccessibleObject
            {
                readonly ThemeCardPicker owner;
                readonly int index;
                public Item(ThemeCardPicker owner, int index) { this.owner = owner; this.index = index; }
                public override string Name { get { return Lang.T(Labels[index]); } }
                public override string Description { get { return index == 0 ? Lang.T("Mengikuti mode aplikasi Windows") : null; } }
                public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
                public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                public override AccessibleStates State
                {
                    get
                    {
                        return AccessibleStates.Selectable | AccessibleStates.Focusable |
                            (!owner.Enabled ? AccessibleStates.Unavailable : 0) |
                            (owner.Focused && index == owner.SelectedIndex ? AccessibleStates.Focused : 0) |
                            (index == owner.SelectedIndex ? AccessibleStates.Selected | AccessibleStates.Checked : 0);
                    }
                }
                public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.HitRect(index))); } }
                public override string DefaultAction { get { return Lang.T("Pilih"); } }
                public override void DoDefaultAction() { if (owner.Enabled) owner.Choose(index); }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < Labels.Length; i++) if (HitRect(i).Contains(e.Location)) h = i;
                if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Cursor = Cursors.Default; Invalidate(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (!Enabled || e.Button != MouseButtons.Left) return;
                Focus();
                for (int i = 0; i < Labels.Length; i++)
                {
                    if (HitRect(i).Contains(e.Location))
                    {
                        Choose(i);
                        return;
                    }
                }
            }

            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Home:
                    case Keys.End:
                        return true;
                    default:
                        return base.IsInputKey(keyData);
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (!Enabled) { base.OnKeyDown(e); return; }
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) { Choose(SelectedIndex - 1); e.Handled = true; return; }
                if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) { Choose(SelectedIndex + 1); e.Handled = true; return; }
                if (e.KeyCode == Keys.Home) { Choose(0); e.Handled = true; return; }
                if (e.KeyCode == Keys.End) { Choose(Labels.Length - 1); e.Handled = true; return; }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Under(this));
                float s = Ui;
                var labelFont = Ink.Text(11f * s);
                var selectedFont = Ink.Semi(11f * s);

                for (int i = 0; i < Labels.Length; i++)
                {
                    var tile = TileRect(i);
                    bool sel = i == SelectedIndex;
                    DrawThumb(g, tile, i, s);

                    Color label = sel || i == hover ? KitInk.Text : KitInk.Sub;
                    KitInk.Draw(g, Lang.T(Labels[i]), sel ? selectedFont : labelFont, label, LabelRect(i), ContentAlignment.MiddleCenter);

                    if (sel)
                    {
                        using (var pen = new Pen(KitInk.Accent, 2f * s))
                        using (var path = Ink.Round(new RectangleF(tile.X + s, tile.Y + s, tile.Width - 2 * s, tile.Height - 2 * s), 9f * s))
                            g.DrawPath(pen, path);
                    }
                    else if (i == hover)
                    {
                        using (var pen = new Pen(Ink.A(KitInk.Faint, 0.9f), 1f * s))
                        using (var path = Ink.Round(new RectangleF(tile.X + 0.5f * s, tile.Y + 0.5f * s, tile.Width - s, tile.Height - s), 9.5f * s))
                            g.DrawPath(pen, path);
                    }

                    if (Focused && ShowFocusCues && !KitInk.ByMouse && sel)
                    {
                        var ring = Inside(RectangleF.Inflate(tile, -2 * s, -2 * s), Width, Height);
                        using (var path = Ink.Round(ring, 8f * s))
                        using (var pen = new Pen(KitInk.Text, 1.5f * s))
                            g.DrawPath(pen, path);
                    }
                }
            }

            void DrawThumb(Graphics g, RectangleF tile, int kind, float s)
            {
                using (var clip = Ink.Round(tile, 10 * s))
                {
                    var state = g.Save();
                    g.SetClip(clip, CombineMode.Intersect);

                    if (kind == 0)
                    {
                        using (var b = new SolidBrush(DarkCanvas)) g.FillRectangle(b, tile.X, tile.Y, tile.Width / 2f, tile.Height);
                        using (var b = new SolidBrush(LightCanvas)) g.FillRectangle(b, tile.X + tile.Width / 2f, tile.Y, tile.Width / 2f, tile.Height);
                        float pad = 7 * s, gap = 4 * s;
                        float w = (tile.Width - pad * 2 - gap) / 2f;
                        float h = tile.Height - pad * 2;
                        Mini(g, new RectangleF(tile.X + pad, tile.Y + pad, w, h), true, s * 0.9f);
                        Mini(g, new RectangleF(tile.X + pad + w + gap, tile.Y + pad, w, h), false, s * 0.9f);
                    }
                    else
                    {
                        using (var b = new SolidBrush(kind == 1 ? LightCanvas : DarkCanvas)) g.FillRectangle(b, tile);
                        float pad = 8 * s;
                        Mini(g, new RectangleF(tile.X + pad, tile.Y + pad, tile.Width - pad * 2, tile.Height - pad * 2), kind == 2, s);
                    }

                    g.Restore(state);
                }
            }

            void Mini(Graphics g, RectangleF r, bool dark, float s)
            {
                float radius = Math.Min(4 * s, Math.Min(r.Width, r.Height) / 2f);
                using (var win = Ink.Round(r, radius))
                {
                    using (var b = new SolidBrush(dark ? DarkWindow : LightWindow)) g.FillPath(b, win);
                    var state = g.Save();
                    g.SetClip(win, CombineMode.Intersect);
                    using (var b = new SolidBrush(dark ? DarkTitle : LightTitle))
                        g.FillRectangle(b, r.X, r.Y, r.Width, Math.Min(r.Height, 8 * s));
                    float dot = Math.Max(2f, 2.5f * s);
                    using (var b = new SolidBrush(KitInk.Accent))
                        g.FillEllipse(b, r.X + 5 * s, r.Y + 3 * s, dot, dot);
                    g.Restore(state);

                    float lx = r.X + 6 * s, lw = Math.Max(2f, r.Width - 12 * s);
                    float lh = Math.Max(1.5f, 2f * s);
                    using (var b = new SolidBrush(dark ? DarkLine : LightLine))
                    {
                        g.FillRectangle(b, lx, r.Y + 14 * s, lw, lh);
                        g.FillRectangle(b, lx, r.Y + 19 * s, Math.Max(2f, lw * 0.65f), lh);
                    }
                }
                using (var pen = new Pen(dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0), 1f * s))
                using (var border = Ink.Round(r, radius))
                    g.DrawPath(pen, border);
            }
        }
    }
}
