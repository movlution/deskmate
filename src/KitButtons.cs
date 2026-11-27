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
        float hoverT, pressT;
        int lastTick;
        Timer anim;

        public KitButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            UseVisualStyleBackColor = false;
        }

        void EnsureAnim()
        {
            if (anim != null) return;
            anim = new Timer { Interval = 15 };
            anim.Tick += delegate
            {
                int now = Environment.TickCount;
                float dt = Math.Max(0.004f, Math.Min(0.05f, unchecked((uint)(now - lastTick)) / 1000f));
                lastTick = now;
                if (Anim.Reduced)
                {
                    hoverT = hovered ? 1f : 0f;
                    pressT = pressed ? 1f : 0f;
                    anim.Stop();
                    Invalidate();
                    return;
                }
                float hWant = hovered ? 1f : 0f, pWant = pressed ? 1f : 0f;
                float hRate = Math.Min(1f, dt / Math.Max(0.03f, Anim.Response(0.09f)));
                float pRate = Math.Min(1f, dt / Math.Max(0.025f, Anim.Response(0.055f)));
                hoverT += (hWant - hoverT) * hRate;
                pressT += (pWant - pressT) * pRate;
                if (Math.Abs(hWant - hoverT) < 0.01f) hoverT = hWant;
                if (Math.Abs(pWant - pressT) < 0.01f) pressT = pWant;
                if (hoverT == hWant && pressT == pWant) anim.Stop();
                Invalidate();
            };
        }

        protected void KickStateAnim()
        {
            if (!IsHandleCreated || !Visible || Anim.Reduced)
            {
                hoverT = hovered ? 1f : 0f;
                pressT = pressed ? 1f : 0f;
                if (anim != null) anim.Stop();
                Invalidate();
                return;
            }
            EnsureAnim();
            if (!anim.Enabled)
            {
                lastTick = Environment.TickCount;
                anim.Start();
            }
            Invalidate();
        }

        protected float HoverFactor { get { return anim != null && anim.Enabled ? hoverT : (hovered ? 1f : 0f); } }
        protected float PressFactor { get { return anim != null && anim.Enabled ? pressT : (pressed ? 1f : 0f); } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; KickStateAnim(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            KitInk.ByMouse = true;
            if (e.Button == MouseButtons.Left) { pressed = true; KickStateAnim(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (pressed) { pressed = false; KickStateAnim(); }
        }
        protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            if (pressed) { pressed = false; }
            KickStateAnim();
        }

        protected override void Dispose(bool disposing) { if (disposing && anim != null) anim.Dispose(); base.Dispose(disposing); }

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
            float ht = HoverFactor, pt = PressFactor;
            Color bg, fg;
            if (!Enabled) { bg = KitInk.Line; fg = KitInk.Faint; }
            else if (Primary)
            {
                // Hover and press move away from the label ink: contrast only grows
                float depth = Math.Max(ht * 0.1f, pt * 0.2f);
                bg = KitInk.Deepen(KitInk.Accent, KitInk.OnAccent, depth);
                fg = KitInk.OnAccent;
            }
            else
            {
                Color restOrHover = Ink.Mix(KitInk.Line, KitInk.Tint(0.17f), ht);
                bg = Ink.Mix(restOrHover, KitInk.Tint(0.22f), pt);
                fg = KitInk.Text;
            }

            if (pt > 0.01f && Enabled) r.Offset(0, 0.75f * s * pt);
            KitInk.FillRound(g, r, Radius(r, s), bg);
            KitInk.Draw(g, Text, Font, fg, r, ContentAlignment.MiddleCenter);
            DrawRing(g, r, s);
        }
    }

    // Check row: soft body with a drawn box, so no native CheckBox box leaks in
    class KitCheck : KitButton
    {
        bool on;
        readonly Spring pop = new Spring(0, 1, 1, 0.004f);
        int checkTick;
        Timer checkAnim;

        public event EventHandler CheckedChanged;

        void EnsureCheckAnim()
        {
            if (checkAnim != null) return;
            checkAnim = new Timer { Interval = 15 };
            checkAnim.Tick += delegate
            {
                int now = Environment.TickCount;
                float dt = Math.Max(0.004f, Math.Min(0.05f, unchecked((uint)(now - checkTick)) / 1000f));
                checkTick = now;
                if (Anim.Reduced)
                {
                    pop.Snap(on ? 1f : 0f);
                    checkAnim.Stop();
                    Invalidate();
                    return;
                }
                pop.Step(dt);
                if (pop.Settled) { pop.Snap(pop.Target); checkAnim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                if (IsHandleCreated && Visible && !Anim.Reduced)
                {
                    Springs.Tune(pop, 0.22f, 0.74f);
                    pop.Target = on ? 1f : 0f;
                    checkTick = Environment.TickCount;
                    EnsureCheckAnim();
                    checkAnim.Start();
                }
                else
                {
                    pop.Snap(on ? 1f : 0f);
                    if (checkAnim != null) checkAnim.Stop();
                }
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !on; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = UiScale;
            var r = Body(s);
            bool live = Enabled;
            float ht = live ? HoverFactor : 0f, pt = live ? PressFactor : 0f;
            Color bg = Ink.Mix(Ink.Mix(KitInk.Line, KitInk.Tint(0.17f), ht), KitInk.Tint(0.22f), pt);
            KitInk.FillRound(g, r, Radius(r, s), bg);
            bool running = checkAnim != null && checkAnim.Enabled;
            float t = running ? pop.Value : (on ? 1f : 0f);
            float tClamp = Ease.Clamp01(t);
            // Subtle spring bounce scale on check
            float scale = running && !Anim.Reduced ? 1f + Math.Max(0f, (t - 0.85f) * 0.45f) : 1f;
            float box = Math.Min(16f * s, r.Height - 8f * s) * scale, cy = r.Y + r.Height / 2f, cx = r.X + 9f * s + Math.Min(16f * s, r.Height - 8f * s) / 2f;
            var tick = new RectangleF(cx - box / 2, cy - box / 2, box, box);
            float corner = 5f * s;
            if (tClamp > 0.02f)
            {
                Color fill = live ? KitInk.Accent : KitInk.Disabled;
                if (tClamp < 0.99f) fill = Ink.Mix(bg, fill, tClamp);
                KitInk.FillRound(g, tick, corner, fill);
                var pen = KitInk.PenOf(live ? KitInk.OnAccent : KitInk.Line, 1.8f * s);
                float x0 = cx - box * 0.26f, y0 = cy + box * 0.01f;
                float x1 = cx - box * 0.06f, y1 = cy + box * 0.2f;
                float x2 = cx + box * 0.27f, y2 = cy - box * 0.19f;
                if (tClamp >= 0.98f)
                {
                    g.DrawLine(pen, x0, y0, x1, y1);
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
                else
                {
                    float p1 = Ease.Clamp01(tClamp / 0.45f);
                    float p2 = Ease.Clamp01((tClamp - 0.4f) / 0.6f);
                    if (p1 > 0f) g.DrawLine(pen, x0, y0, Ease.Lerp(x0, x1, p1), Ease.Lerp(y0, y1, p1));
                    if (p2 > 0f) g.DrawLine(pen, x1, y1, Ease.Lerp(x1, x2, p2), Ease.Lerp(y1, y2, p2));
                }
            }
            if (tClamp < 0.98f)
                KitInk.StrokeRound(g, RectangleF.Inflate(tick, -0.75f * s, -0.75f * s), corner, Ink.A(live ? KitInk.Sub : KitInk.Disabled, 1f - tClamp), 1.5f * s);
            KitInk.Draw(g, Text, Font, live ? KitInk.Text : KitInk.Faint, new RectangleF(r.X + 9f * s + Math.Min(16f * s, r.Height - 8f * s) + 8f * s, r.Y, r.Width - (9f * s + Math.Min(16f * s, r.Height - 8f * s) + 23f * s), r.Height), ContentAlignment.MiddleLeft);
            DrawRing(g, r, s);
        }

        protected override void Dispose(bool disposing) { if (disposing && checkAnim != null) checkAnim.Dispose(); base.Dispose(disposing); }
    }

    // List choice: button opening styled menu
    class KitPopup : KitButton
    {
        public string[] Items = new string[0];
        int selected;
        public event Action<int> Changed;
        // Just before the menu opens: a chance to refresh Items and Selected
        public event Action Opening;

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
            var o = Opening;
            if (o != null) o();
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

    // Soft card of rows with crisp border and hairline separators
    class KitGroup : Panel
    {
        public Color AccentTile;

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
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            float rad = 13f * s;
            KitInk.FillRound(g, card, rad, KitInk.Group);
            Color edge = AccentTile.IsEmpty ? KitInk.Line : Ink.Mix(KitInk.Line, AccentTile, Ink.Light ? 0.22f : 0.26f);
            KitInk.StrokeRound(g, card, rad, edge, 1f * s);
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

    // Setting row: icon well, title, detail, control right; tactile hover & whole-row switch toggle
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
        public bool Divider;
        public Color AccentTile;
        bool hovered;
        bool pressedSwitch;

        public KitRow()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        bool IsClickableSwitch()
        {
            var sw = Field as KitSwitch;
            return Enabled && sw != null && sw.Enabled;
        }

        void UpdateHover(bool inside)
        {
            if (inside == hovered) return;
            hovered = inside;
            Cursor = hovered && IsClickableSwitch() ? Cursors.Hand : Cursors.Default;
            Invalidate(true);
        }

        void HookMouse(Control c)
        {
            if (c == null) return;
            c.MouseEnter += delegate { UpdateHover(true); };
            c.MouseLeave += delegate
            {
                if (!IsDisposed && IsHandleCreated)
                    UpdateHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
            };
            c.ControlAdded += delegate(object s, ControlEventArgs args) { HookMouse(args.Control); };
            foreach (Control child in c.Controls) HookMouse(child);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control == null) return;
            HookMouse(e.Control);
            var sw = e.Control as KitSwitch;
            if (sw != null) sw.CheckedChanged += delegate { if (!IsDisposed) Invalidate(); };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            UpdateHover(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!IsDisposed && IsHandleCreated)
                UpdateHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
            else
                UpdateHover(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && IsClickableSwitch())
            {
                pressedSwitch = true;
                Invalidate(true);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool fire = pressedSwitch && e.Button == MouseButtons.Left && IsClickableSwitch() && ClientRectangle.Contains(e.Location);
            pressedSwitch = false;
            if (fire)
            {
                var sw = (KitSwitch)Field;
                sw.Checked = !sw.Checked;
            }
            Invalidate(true);
        }

        // Prevent leaking an unparented Field (e.g. search-filtered rows)
        protected override void Dispose(bool disposing)
        {
            if (disposing && Field != null && !Field.IsDisposed && Field.Parent == null)
                Field.Dispose();
            base.Dispose(disposing);
        }

        Color EffectiveTile() { return AccentTile.IsEmpty ? KitInk.Accent : AccentTile; }

        public Color SurfaceColor()
        {
            if (!(hovered && Enabled)) return KitInk.Group;
            Color tile = EffectiveTile();
            return pressedSwitch
                ? Ink.Mix(KitInk.Group, tile, Ink.Light ? 0.09f : 0.14f)
                : Ink.Mix(KitInk.Group, tile, Ink.Light ? 0.045f : 0.08f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var w = FindForm() as KitWindow;
            float s = w != null ? w.Ui : 1;
            KitInk.Smooth(g);

            Color tile = EffectiveTile();
            if (hovered && Enabled)
            {
                float insetX = 5f * s, insetY = 3f * s;
                var hr = new RectangleF(insetX, insetY, Width - insetX * 2f, Height - insetY * 2f);
                KitInk.FillRound(g, hr, 9f * s, SurfaceColor());
            }

            float right = Field != null ? Field.Left - 12 * s : Width - 16 * s;
            float x = TextX(s);
            if (Glyph != '\0') DrawGlyph(g, s, tile);
            // Hairline between grip and icon
            if (Divider)
                g.DrawLine(KitInk.PenOf(Ink.Mix(KitInk.Line, KitInk.Sub, 0.45f), Math.Max(1f, s)), (Lead - 2) * s, Height * 0.3f, (Lead - 2) * s, Height * 0.7f);
            Font f = Ink.Semi(13f * s), d = Ink.Text(11.5f * s);
            bool two = Detail.Length > 0;
            // Two lines sit centred as one block; tall rows keep them beside the top-aligned glyph
            float top = Height > 80 * s ? 8 * s : Math.Max(0, (Height - 38 * s) / 2);
            var tr = new RectangleF(x, two ? top : 0, right - x, two ? 20 * s : Height);
            KitInk.Draw(g, Title, f, Enabled ? KitInk.Text : KitInk.Faint, tr, ContentAlignment.MiddleLeft);
            if (Suffix.Length > 0) DrawSuffix(g, f, tr, s);
            if (two) KitInk.Draw(g, Detail, d, KitInk.Sub, new RectangleF(x, top + 19 * s, right - x, 18 * s), ContentAlignment.MiddleLeft);
        }

        // Text column: after the icon well when there is one
        public float TextX(float s) { return ((Glyph != '\0' ? 50 : 16) + Lead) * s; }

        void DrawGlyph(Graphics g, float s, Color tile)
        {
            if (Glyph == MarkGlyph)
            {
                float mSize = 24f * s;
                var mBox = new RectangleF((14 + Lead) * s, Height > 80 * s ? 9 * s : (Height - mSize) / 2, mSize, mSize);
                g.DrawIcon(KitWindow.AppIcon, Rectangle.Round(mBox));
                return;
            }
            float wellSize = 28f * s;
            var well = new RectangleF((13 + Lead) * s, Height > 80 * s ? 9 * s : (Height - wellSize) / 2, wellSize, wellSize);
            var sw = Field as KitSwitch;
            bool activeOn = Enabled && sw != null && sw.Enabled && sw.Checked;
            if (activeOn)
            {
                Color wellBg = Ink.Mix(KitInk.Group, tile, Ink.Light ? 0.16f : 0.26f);
                Color wellEdge = Ink.Mix(KitInk.Line, tile, Ink.Light ? 0.32f : 0.42f);
                Color glyphInk = Ink.Light ? Ink.Mix(tile, Color.Black, 0.14f) : Ink.Mix(tile, Color.White, 0.24f);
                KitInk.FillRound(g, well, 8f * s, wellBg);
                KitInk.StrokeRound(g, well, 8f * s, wellEdge, 1f * s);
                KitInk.Draw(g, Ink.Str(Glyph), Ink.Icons(14f * s), glyphInk, well, ContentAlignment.MiddleCenter);
            }
            else
            {
                Color wellBg = hovered && Enabled
                    ? Ink.Mix(KitInk.Hover, tile, Ink.Light ? 0.08f : 0.14f)
                    : KitInk.Hover;
                Color glyphInk = Enabled ? KitInk.Text : KitInk.Faint;
                KitInk.FillRound(g, well, 8f * s, wellBg);
                KitInk.Draw(g, Ink.Str(Glyph), Ink.Icons(14f * s), glyphInk, well, ContentAlignment.MiddleCenter);
            }
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
