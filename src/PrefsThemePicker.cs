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

            static readonly Color DarkTop = Color.FromArgb(255, 28, 31, 37);
            static readonly Color DarkBottom = Color.FromArgb(255, 14, 16, 20);
            static readonly Color LightTop = Color.FromArgb(255, 250, 251, 253);
            static readonly Color LightBottom = Color.FromArgb(255, 231, 235, 241);
            static readonly Color DarkWindow = Color.FromArgb(228, 40, 44, 51);
            static readonly Color LightWindow = Color.FromArgb(236, 255, 255, 255);
            static readonly Color DarkWindowLine = Color.FromArgb(96, 255, 255, 255);
            static readonly Color LightWindowLine = Color.FromArgb(72, 22, 26, 34);

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
                g.Clear(KitInk.Under(this));
                float s = Ui;
                var labelFont = Ink.Text(10.8f * s);
                var selectedFont = Ink.Semi(10.8f * s);

                for (int i = 0; i < Labels.Length; i++)
                {
                    var tile = TileRect(i);
                    bool sel = i == SelectedIndex;
                    DrawThumb(g, tile, i, s);

                    if (sel)
                    {
                        using (var pen = new Pen(KitInk.Accent, 2f * s))
                        using (var path = Ink.Round(new RectangleF(tile.X + 1.25f * s, tile.Y + 1.25f * s,
                                                                   tile.Width - 2.5f * s, tile.Height - 2.5f * s), 9f * s))
                            g.DrawPath(pen, path);
                        DrawCheck(g, tile, s);
                    }
                    else if (i == hover)
                    {
                        using (var pen = new Pen(Ink.A(KitInk.Faint, 0.85f), 1f * s))
                        using (var path = Ink.Round(new RectangleF(tile.X + 0.5f * s, tile.Y + 0.5f * s,
                                                                   tile.Width - s, tile.Height - s), 9.5f * s))
                            g.DrawPath(pen, path);
                    }

                    Color label = sel || i == hover ? KitInk.Text : KitInk.Sub;
                    var labelRect = LabelRect(i);
                    KitInk.Draw(g, Lang.T(Labels[i]), sel ? selectedFont : labelFont, label, labelRect, ContentAlignment.MiddleCenter);
                    if (Focused && ShowFocusCues && !KitInk.ByMouse && sel)
                    {
                        using (var pen = new Pen(KitInk.Text, 1f * s) { DashStyle = DashStyle.Dot })
                        using (var path = Ink.Round(new RectangleF(labelRect.X + 4 * s, labelRect.Y + 0.5f * s,
                                                                   labelRect.Width - 8 * s, labelRect.Height - s), 3f * s))
                            g.DrawPath(pen, path);
                    }
                }
            }

            // A selected tile gets one small accent seal, not a double outline.
            void DrawCheck(Graphics g, RectangleF tile, float s)
            {
                float d = 14 * s;
                var badge = new RectangleF(tile.Right - d - 5 * s, tile.Top + 5 * s, d, d);
                using (var b = new SolidBrush(KitInk.Accent)) g.FillEllipse(b, badge);
                using (var pen = new Pen(KitInk.OnAccent, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float x = badge.X + badge.Width * 0.29f, y = badge.Y + badge.Height * 0.52f;
                    g.DrawLine(pen, x, y, x + badge.Width * 0.16f, y + badge.Height * 0.17f);
                    g.DrawLine(pen, x + badge.Width * 0.16f, y + badge.Height * 0.17f, x + badge.Width * 0.44f, y - badge.Height * 0.20f);
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
                        float half = tile.Width / 2f;
                        DrawDesktop(g, new RectangleF(tile.X, tile.Y, half, tile.Height), true, s, true);
                        DrawDesktop(g, new RectangleF(tile.X + half, tile.Y, half, tile.Height), false, s, true);
                        using (var pen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f * s))
                            g.DrawLine(pen, tile.X + half, tile.Y, tile.X + half, tile.Bottom);
                    }
                    else
                    {
                        DrawDesktop(g, tile, kind == 2, s, false);
                    }
                    g.Restore(state);
                }
                using (var pen = new Pen(Color.FromArgb(20, Ink.Light ? Color.Black : Color.White), 1f * s))
                using (var border = Ink.Round(tile, 10 * s))
                    g.DrawPath(pen, border);
            }

            void DrawDesktop(Graphics g, RectangleF r, bool dark, float s, bool compact)
            {
                using (var b = new LinearGradientBrush(r, dark ? DarkTop : LightTop, dark ? DarkBottom : LightBottom, 90f))
                    g.FillRectangle(b, r);

                // A quiet accent wash gives each thumbnail a desktop horizon.
                Color accent = KitInk.Accent;
                using (var b = new LinearGradientBrush(r, Color.FromArgb(dark ? 30 : 18, accent), Color.FromArgb(0, accent), 22f))
                    g.FillRectangle(b, r);

                // Mini island, window, and dock: enough to read as a desktop,
                // without the stray accent dot that used to look like a defect.
                DrawIsland(g, r, s);
                DrawWindow(g, r, dark, s, compact);
                DrawDock(g, r, dark, s);
            }

            void DrawIsland(Graphics g, RectangleF r, float s)
            {
                float w = Math.Max(10 * s, r.Width * 0.34f);
                float h = Math.Max(2.5f * s, 3.6f * s);
                var pill = new RectangleF(r.X + (r.Width - w) / 2f, r.Y + Math.Max(3 * s, 4 * s), w, h);
                using (var path = Ink.Round(pill, h / 2f))
                using (var b = new SolidBrush(Color.FromArgb(238, 8, 9, 11)))
                    g.FillPath(b, path);
            }

            void DrawWindow(Graphics g, RectangleF r, bool dark, float s, bool compact)
            {
                float ww = Math.Max(16 * s, r.Width * (compact ? 0.58f : 0.62f));
                float wh = Math.Max(11 * s, r.Height * (compact ? 0.30f : 0.42f));
                var win = new RectangleF(r.X + (r.Width - ww) / 2f, r.Y + r.Height * (compact ? 0.40f : 0.29f), ww, wh);
                float radius = (compact ? 2.4f : 3.6f) * s;

                using (var shadow = Ink.Round(new RectangleF(win.X, win.Y + 1.5f * s, win.Width, win.Height), radius))
                using (var b = new SolidBrush(Color.FromArgb(dark ? 60 : 25, 0, 0, 0)))
                    g.FillPath(b, shadow);
                using (var path = Ink.Round(win, radius))
                {
                    using (var b = new SolidBrush(dark ? DarkWindow : LightWindow)) g.FillPath(b, path);
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    float topH = Math.Max(5 * s, win.Height * 0.24f);
                    using (var b = new SolidBrush(dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(245, 245, 246, 248)))
                        g.FillRectangle(b, win.X, win.Y, win.Width, topH);

                    float dot = Math.Max(1.1f * s, 1.2f * s);
                    using (var b = new SolidBrush(dark ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(110, 55, 60, 68)))
                    {
                        for (int i = 0; i < 3; i++)
                            g.FillEllipse(b, win.X + 3.5f * s + i * (dot + 1.4f * s), win.Y + topH / 2f - dot / 2f, dot, dot);
                    }

                    float lx = win.X + 5 * s;
                    float lw = Math.Max(4 * s, win.Width - 10 * s);
                    float lh = Math.Max(1.1f * s, 1.4f * s);
                    using (var b = new SolidBrush(dark ? DarkWindowLine : LightWindowLine))
                    {
                        g.FillRectangle(b, lx, win.Y + topH + 4.5f * s, lw, lh);
                        g.FillRectangle(b, lx, win.Y + topH + 8.5f * s, lw * 0.65f, lh);
                    }
                    using (var b = new SolidBrush(Ink.A(KitInk.Accent, 0.8f)))
                        g.FillRectangle(b, win.Right - 12 * s, win.Bottom - 5 * s, 8 * s, 2.2f * s);
                    g.Restore(state);
                }
                using (var pen = new Pen(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(24, 24, 28, 34), 1f * s))
                using (var border = Ink.Round(win, radius))
                    g.DrawPath(pen, border);
            }

            void DrawDock(Graphics g, RectangleF r, bool dark, float s)
            {
                float w = Math.Max(12 * s, r.Width * 0.42f);
                float h = Math.Max(3.2f * s, 4.4f * s);
                var dock = new RectangleF(r.X + (r.Width - w) / 2f, r.Bottom - h - 4 * s, w, h);
                using (var path = Ink.Round(dock, h / 2f))
                {
                    using (var b = new SolidBrush(dark ? Color.FromArgb(175, 22, 24, 29) : Color.FromArgb(185, 255, 255, 255)))
                        g.FillPath(b, path);
                    using (var pen = new Pen(dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0), 1f * s))
                        g.DrawPath(pen, path);
                    float dot = Math.Max(1.1f * s, 1.2f * s);
                    using (var b = new SolidBrush(dark ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(90, 55, 60, 68)))
                    {
                        for (int i = 0; i < 3; i++)
                            g.FillEllipse(b, dock.X + dock.Width / 2f + (i - 1) * (dot + 1.5f * s) - dot / 2f,
                                           dock.Y + dock.Height / 2f - dot / 2f, dot, dot);
                    }
                }
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

            static readonly GlassKind[] Kinds = { GlassKind.Clear, GlassKind.Liquid, GlassKind.Frost, GlassKind.Solid };
            static readonly string[] Labels = { "Bening", "Liquid", "Frost", "Solid" };

            float Gap { get { return 5f * Ui; } }

            RectangleF CellRect(int i)
            {
                float w = (Width - Gap * 3) / 4f;
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
                g.Clear(KitInk.Under(this));
                float s = Ui;
                var labelFont = Ink.Text(10.5f * s);
                var selectedFont = Ink.Semi(10.5f * s);

                for (int i = 0; i < Kinds.Length; i++)
                {
                    var cell = CellRect(i);
                    bool sel = i == SelectedIndex;
                    if (sel || i == hover)
                    {
                        using (var path = Ink.Round(new RectangleF(cell.X + 0.5f * s, cell.Y + 1.5f * s,
                                                                   cell.Width - s, cell.Height - 3 * s), 9f * s))
                        using (var b = new SolidBrush(sel ? Ink.Mix(KitInk.Hover, KitInk.Accent, 0.10f) : KitInk.Hover))
                            g.FillPath(b, path);
                    }

                    var sample = new RectangleF(cell.X + 6 * s, cell.Y + 6 * s, cell.Width - 12 * s, 30 * s);
                    DrawSample(g, sample, Kinds[i], s);

                    var label = new RectangleF(cell.X, sample.Bottom + 2 * s, cell.Width, 15 * s);
                    KitInk.Draw(g, Lang.T(Labels[i]), sel ? selectedFont : labelFont,
                        sel || i == hover ? KitInk.Text : KitInk.Sub, label, ContentAlignment.MiddleCenter);

                    if (sel)
                    {
                        using (var pen = new Pen(KitInk.Accent, 1.4f * s))
                        using (var path = Ink.Round(new RectangleF(cell.X + 0.75f * s, cell.Y + 1.75f * s,
                                                                   cell.Width - 1.5f * s, cell.Height - 3.5f * s), 9.2f * s))
                            g.DrawPath(pen, path);
                    }
                    if (Focused && ShowFocusCues && !KitInk.ByMouse && sel)
                    {
                        using (var pen = new Pen(KitInk.Text, 1f * s) { DashStyle = DashStyle.Dot })
                        using (var path = Ink.Round(new RectangleF(label.X + 4 * s, label.Y + 0.5f * s,
                                                                   label.Width - 8 * s, label.Height - s), 3f * s))
                            g.DrawPath(pen, path);
                    }
                }
            }

            void DrawSample(Graphics g, RectangleF r, GlassKind kind, float s)
            {
                float radius = 8 * s;
                using (var path = Ink.Round(r, radius))
                {
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);

                    // A fixed, colourful desktop sits behind the pane so the four
                    // materials stay legible even when the chosen accent is Grafit.
                    // It is a representative preview, not the live wallpaper.
                    Color blue = Color.FromArgb(255, 54, 112, 232);
                    Color coral = Color.FromArgb(255, 236, 112, 94);
                    Color a = Ink.Light ? Ink.Mix(blue, Color.White, 0.22f) : Ink.Mix(blue, Color.Black, 0.34f);
                    Color b = Ink.Light ? Ink.Mix(coral, Color.White, 0.22f) : Ink.Mix(coral, Color.Black, 0.34f);
                    using (var brush = new LinearGradientBrush(r, a, b, 24f)) g.FillRectangle(brush, r);
                    using (var brush = new LinearGradientBrush(r, Color.FromArgb(105, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillRectangle(brush, new RectangleF(r.X, r.Y, r.Width, r.Height * 0.56f));

                    Color previewVeil = Color.FromArgb(255, 48, 102, 205);
                    Color tint = GlassRecipe.Tint(kind, previewVeil, Ink.Light, true, false);
                    using (var brush = new SolidBrush(tint)) g.FillRectangle(brush, r);

                    if (kind != GlassKind.Solid)
                    {
                        int sheen = GlassRecipe.Sheen(kind, Ink.Light);
                        if (sheen > 0)
                            using (var brush = new SolidBrush(Color.FromArgb(sheen, 255, 255, 255)))
                                g.FillRectangle(brush, r.X, r.Y, r.Width, Math.Max(1f, 1.4f * s));

                        // A soft band after the tint keeps Liquid glossy and Frost
                        // matte; the line inside the chip never reads as a button.
                        int gloss = kind == GlassKind.Liquid ? 72 : kind == GlassKind.Clear ? 46 : 20;
                        using (var brush = new LinearGradientBrush(
                            new RectangleF(r.X, r.Y, r.Width, Math.Max(1f, r.Height * 0.62f)),
                            Color.FromArgb(gloss, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                            g.FillRectangle(brush, r.X, r.Y, r.Width, r.Height * 0.62f);
                    }
                    g.Restore(state);
                }

                using (var pen = new Pen(Color.FromArgb(GlassRecipe.Border(kind, Ink.Light), kind == GlassKind.Solid ? KitInk.Faint : Color.White), 1f * s))
                using (var border = Ink.Round(r, radius))
                    g.DrawPath(pen, border);
                if (kind == GlassKind.Solid)
                {
                    using (var pen = new Pen(Ink.A(KitInk.Faint, 0.75f), 1f * s))
                    using (var border = Ink.Round(new RectangleF(r.X + 1 * s, r.Y + 1 * s, r.Width - 2 * s, r.Height - 2 * s), radius - 1 * s))
                        g.DrawPath(pen, border);
                }
            }

        }
    }
}
