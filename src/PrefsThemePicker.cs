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

            // Previews paint the real default tones, so the cards always match the windows they choose
            static readonly Tone NightTone = Tone.Dark(), PaperTone = Tone.Paper();

            float LabelH { get { return 15f * Ui; } }
            float LabelGap { get { return 4f * Ui; } }

            RectangleF TileRect(int i)
            {
                float s = Ui;
                float gap = 7 * s;
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
                var under = KitInk.Under(this);
                g.Clear(under);
                float s = Ui, radius = Ink.RadiusControl * s;
                var labelFont = Ink.Text(10.8f * s);
                var selectedFont = Ink.Semi(10.8f * s);

                for (int i = 0; i < Labels.Length; i++)
                {
                    var tile = TileRect(i);
                    bool sel = i == SelectedIndex;
                    DrawThumb(g, tile, i, radius, s);

                    if (sel)
                    {
                        float inset = 1.25f * s;
                        KitInk.StrokeRound(g, RectangleF.Inflate(tile, -inset, -inset), radius - inset, KitInk.Accent, 2f * s);
                        DrawCheck(g, tile, s);
                    }
                    else if (i == hover && Enabled)
                        KitInk.StrokeRound(g, RectangleF.Inflate(tile, -0.5f * s, -0.5f * s), radius, KitInk.Faint, 1f * s);

                    KitInk.Draw(g, Lang.T(Labels[i]), sel ? selectedFont : labelFont, sel || i == hover ? KitInk.Text : KitInk.Sub, LabelRect(i), ContentAlignment.MiddleCenter);
                    if (sel && Focused && ShowFocusCues && !KitInk.ByMouse)
                    {
                        var ring = KitControl.Inside(RectangleF.Inflate(tile, 2.5f * s, 2.5f * s), Width, Height);
                        KitInk.FocusRing(g, ring, radius + 2.5f * s, under, s);
                    }
                }
            }

            // A selected tile gets one small accent seal, not a double outline.
            void DrawCheck(Graphics g, RectangleF tile, float s)
            {
                float d = 14 * s;
                var badge = new RectangleF(tile.Right - d - 5 * s, tile.Top + 5 * s, d, d);
                g.FillEllipse(KitInk.BrushOf(KitInk.Accent), badge);
                var pen = KitInk.PenOf(KitInk.OnAccent, 1.8f * s);
                float x = badge.X + badge.Width * 0.29f, y = badge.Y + badge.Height * 0.52f;
                g.DrawLine(pen, x, y, x + badge.Width * 0.16f, y + badge.Height * 0.17f);
                g.DrawLine(pen, x + badge.Width * 0.16f, y + badge.Height * 0.17f, x + badge.Width * 0.44f, y - badge.Height * 0.20f);
            }

            void DrawThumb(Graphics g, RectangleF tile, int kind, float radius, float s)
            {
                var state = g.Save();
                g.SetClip(KitInk.Shape(tile, radius), CombineMode.Intersect);
                if (kind == 0)
                {
                    float half = tile.Width / 2f;
                    DrawDesktop(g, new RectangleF(tile.X, tile.Y, half, tile.Height), NightTone, s, true);
                    DrawDesktop(g, new RectangleF(tile.X + half, tile.Y, half, tile.Height), PaperTone, s, true);
                }
                else DrawDesktop(g, tile, kind == 2 ? NightTone : PaperTone, s, false);
                g.Restore(state);
                // Hairline edge so the paper card holds its shape on a paper window
                KitInk.StrokeRound(g, tile, radius, Ink.Light ? Ink.Scrim(22) : Ink.Sheen(20), 1f);
            }

            // Canvas, a card window, the island pill, and the dock, all in the tone's own colours
            void DrawDesktop(Graphics g, RectangleF r, Tone t, float s, bool compact)
            {
                bool dark = t.IsDark;
                g.FillRectangle(KitInk.BrushOf(dark ? Ink.Mix(t.Bg, Color.Black, 0.18f) : Ink.Mix(t.Bg, t.Text, 0.05f)), r);
                // A quiet accent horizon along the bottom of the desktop
                g.FillRectangle(KitInk.BrushOf(Ink.A(KitInk.Accent, dark ? 0.16f : 0.10f)), r.X, r.Y + r.Height * 0.62f, r.Width, r.Height * 0.38f);

                float w = Math.Max(10 * s, r.Width * 0.34f), h = 3.6f * s;
                KitInk.FillRound(g, new RectangleF(r.X + (r.Width - w) / 2f, r.Y + 4 * s, w, h), h / 2f, Ink.A(Tone.Night, 0.93f));

                float ww = Math.Max(16 * s, r.Width * (compact ? 0.58f : 0.62f));
                float wh = Math.Max(11 * s, r.Height * (compact ? 0.30f : 0.42f));
                var win = new RectangleF(r.X + (r.Width - ww) / 2f, r.Y + r.Height * (compact ? 0.40f : 0.29f), ww, wh);
                float wr = (compact ? 2.4f : 3.6f) * s;
                KitInk.FillRound(g, new RectangleF(win.X, win.Y + 1.5f * s, win.Width, win.Height), wr, Color.FromArgb(dark ? 70 : 26, Tone.Plum));
                KitInk.FillRound(g, win, wr, dark ? Ink.Mix(t.Bg, t.Text, 0.09f) : Ink.Mix(t.Bg, Color.White, 0.85f));
                float topH = Math.Max(5 * s, win.Height * 0.24f);
                float dot = 1.2f * s;
                var ink = KitInk.BrushOf(Ink.A(t.Faint, 0.8f));
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(ink, win.X + 3.5f * s + i * (dot + 1.4f * s), win.Y + topH / 2f - dot / 2f, dot, dot);
                float lx = win.X + 5 * s, lw = Math.Max(4 * s, win.Width - 10 * s), lh = 1.4f * s;
                var lines = KitInk.BrushOf(Ink.A(t.Faint, 0.55f));
                g.FillRectangle(lines, lx, win.Y + topH + 4.5f * s, lw, lh);
                g.FillRectangle(lines, lx, win.Y + topH + 8.5f * s, lw * 0.65f, lh);
                KitInk.FillRound(g, new RectangleF(win.Right - 12 * s, win.Bottom - 5 * s, 8 * s, 2.2f * s), 1.1f * s, KitInk.Accent);

                float dw = Math.Max(12 * s, r.Width * 0.42f), dh = 4.4f * s;
                var dock = new RectangleF(r.X + (r.Width - dw) / 2f, r.Bottom - dh - 4 * s, dw, dh);
                KitInk.FillRound(g, dock, dh / 2f, dark ? Ink.Mix(t.Bg, t.Text, 0.14f) : Color.White);
                var dots = KitInk.BrushOf(Ink.A(t.Faint, 0.7f));
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(dots, dock.X + dock.Width / 2f + (i - 1) * (dot + 1.5f * s) - dot / 2f, dock.Y + dock.Height / 2f - dot / 2f, dot, dot);
            }
        }

        // Four actual material samples, using the same recipe as the island and
        // dock. The previews are derived from the recipe, not from a parallel
        // palette, so tuning a material never leaves Settings out of date.
        class MaterialPicker : KitControl
        {
            public int SelectedIndex;
            public string[] Notes = new string[0];
            public event Action<int> Picked;
            int hover = -1;

            static readonly GlassKind[] Kinds = { GlassKind.Clear, GlassKind.Liquid, GlassKind.Frost, GlassKind.Calm, GlassKind.Solid };
            static readonly string[] Labels = { "Bening", "Liquid", "Frost", "Kalem", "Solid" };

            float Gap { get { return 5f * Ui; } }

            RectangleF CellRect(int i)
            {
                float w = (Width - Gap * (Kinds.Length - 1)) / Kinds.Length;
                return new RectangleF(i * (w + Gap), 0, w, Height);
            }

            void Choose(int i)
            {
                i = Math.Max(0, Math.Min(Kinds.Length - 1, i));
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
                readonly MaterialPicker owner;
                public Access(MaterialPicker owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
                public override int GetChildCount() { return Labels.Length; }
                public override AccessibleObject GetChild(int i) { return i >= 0 && i < Labels.Length ? new Item(owner, i) : null; }
                public override AccessibleObject GetSelected() { return GetChild(owner.SelectedIndex); }
                public override AccessibleObject GetFocused() { return owner.Focused ? GetSelected() : null; }
            }

            class Item : AccessibleObject
            {
                readonly MaterialPicker owner;
                readonly int index;
                public Item(MaterialPicker owner, int index) { this.owner = owner; this.index = index; }
                public override string Name { get { return Lang.T(Labels[index]); } }
                public override string Description
                {
                    get
                    {
                        if (owner.Notes != null && index < owner.Notes.Length) return owner.Notes[index];
                        return index == 0 ? Lang.T("Bening: blur paling kuat, warna latar paling terlihat") : null;
                    }
                }
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
                public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.CellRect(index))); } }
                public override string DefaultAction { get { return Lang.T("Pilih"); } }
                public override void DoDefaultAction() { if (owner.Enabled) owner.Choose(index); }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int h = -1;
                for (int i = 0; i < Kinds.Length; i++) if (CellRect(i).Contains(e.Location)) h = i;
                if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Cursor = Cursors.Default; Invalidate(); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (!Enabled || e.Button != MouseButtons.Left) return;
                Focus();
                for (int i = 0; i < Kinds.Length; i++)
                {
                    if (CellRect(i).Contains(e.Location))
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
                if (e.KeyCode == Keys.Left) { Choose(SelectedIndex - 1); e.Handled = true; return; }
                if (e.KeyCode == Keys.Right) { Choose(SelectedIndex + 1); e.Handled = true; return; }
                if (e.KeyCode == Keys.Home) { Choose(0); e.Handled = true; return; }
                if (e.KeyCode == Keys.End) { Choose(Kinds.Length - 1); e.Handled = true; return; }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                var under = KitInk.Under(this);
                g.Clear(under);
                float s = Ui, radius = Ink.RadiusControl * s;
                var labelFont = Ink.Text(10.5f * s);
                var selectedFont = Ink.Semi(10.5f * s);

                for (int i = 0; i < Kinds.Length; i++)
                {
                    var cell = CellRect(i);
                    var card = new RectangleF(cell.X + 0.75f * s, cell.Y + 1.75f * s, cell.Width - 1.5f * s, cell.Height - 3.5f * s);
                    bool sel = i == SelectedIndex;
                    if (sel) KitInk.FillRound(g, card, radius, Ink.Mix(under, KitInk.Accent, Ink.Light ? 0.10f : 0.14f));
                    else if (i == hover && Enabled) KitInk.FillRound(g, card, radius, KitInk.Hover);

                    var sample = new RectangleF(cell.X + 6 * s, cell.Y + 6 * s, cell.Width - 12 * s, 30 * s);
                    DrawSample(g, sample, Kinds[i], s);

                    var label = new RectangleF(cell.X, sample.Bottom + 2 * s, cell.Width, 15 * s);
                    KitInk.Draw(g, Lang.T(Labels[i]), sel ? selectedFont : labelFont, sel || i == hover ? KitInk.Text : KitInk.Sub, label, ContentAlignment.MiddleCenter);

                    if (sel) KitInk.StrokeRound(g, card, radius, KitInk.Accent, 1.5f * s);
                    if (sel && Focused && ShowFocusCues && !KitInk.ByMouse)
                    {
                        var ring = KitControl.Inside(RectangleF.Inflate(card, 2f * s, 2f * s), Width, Height);
                        KitInk.FocusRing(g, ring, radius + 2f * s, under, s);
                    }
                }
            }

            void DrawSample(Graphics g, RectangleF r, GlassKind kind, float s)
            {
                float radius = 8 * s;
                var state = g.Save();
                g.SetClip(KitInk.Shape(r, radius), CombineMode.Intersect);

                // A fixed, softly coloured desktop sits behind the pane so the four
                // materials stay legible even when the chosen accent is Grafit.
                // It is a representative preview, not the live wallpaper.
                Color a = Ink.Label("sky", false), b = Ink.Label("rose", false);
                if (!Ink.Light) { a = Ink.Mix(a, Tone.Night, 0.45f); b = Ink.Mix(b, Tone.Night, 0.45f); }
                using (var brush = new LinearGradientBrush(r, a, b, 24f)) g.FillRectangle(brush, r);

                // The pane: the same recipe and veil the island uses right now
                g.FillRectangle(KitInk.BrushOf(GlassRecipe.Tint(kind, Ink.Veil, Ink.Light, true, false)), r);
                if (kind != GlassKind.Solid)
                {
                    int sheen = GlassRecipe.Sheen(kind, Ink.Light);
                    if (sheen > 0) g.FillRectangle(KitInk.BrushOf(Ink.Sheen(sheen)), r.X, r.Y, r.Width, Math.Max(1f, 1.4f * s));
                    // A soft band keeps Liquid glossy and Frost matte
                    // Calm has no band at all: an even pane under one hairline
                    int gloss = kind == GlassKind.Liquid ? 40 : kind == GlassKind.Clear ? 26 : kind == GlassKind.Calm ? 0 : 10;
                    if (gloss > 0) g.FillRectangle(KitInk.BrushOf(Ink.Sheen(gloss)), r.X, r.Y, r.Width, r.Height * 0.45f);
                }
                g.Restore(state);

                KitInk.StrokeRound(g, r, radius, kind == GlassKind.Solid ? Color.FromArgb(GlassRecipe.Border(kind, Ink.Light), KitInk.Faint) : Ink.Sheen(GlassRecipe.Border(kind, Ink.Light)), 1f * s);
            }

        }
    }
}
