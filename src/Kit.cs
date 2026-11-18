using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Opaque window colours from island tokens
    static class KitInk
    {
        // Last input was the mouse: no rings
        public static bool ByMouse;

        static Color O(Color c) { return Color.FromArgb(255, c.R, c.G, c.B); }
        // Calm surfaces from the tinted tones: canvas, navigation, cards, separators, hover.
        public static Color Window { get { return O(Ink.Base.Bg); } }
        public static Color Side { get { return O(Ink.Mix(Ink.Base.Bg, Color.Black, Ink.Light ? 0.035f : 0.055f)); } }
        public static Color Group { get { return O(Ink.Light ? Ink.Mix(Ink.Base.Bg, Color.White, 0.85f) : Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.07f)); } }
        public static Color Line { get { return O(Ink.Light ? Ink.Mix(Ink.Base.Bg, Color.Black, 0.10f) : Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.12f)); } }
        public static Color Hover { get { return O(Ink.Light ? Ink.Mix(Ink.Base.Bg, Color.Black, 0.06f) : Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.09f)); } }
        // Selected segment, lifted above its track
        public static Color Raised { get { return Ink.Light ? Color.White : O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.22f)); } }
        public static Color Text { get { return O(Ink.Base.Text); } }
        // Light Sub sits above Faint so secondary text keeps hierarchy and >= 4.5:1 on Line
        public static Color Sub { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.74f : 0.72f)); } }
        // 0.69 light / 0.66 dark guarantee >= 4.5:1 across Window, Side, Group, Hover, and Line
        public static Color Faint { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.69f : 0.66f)); } }
        // Disabled chrome (switch knob, slider fill): dimmer than tertiary text so
        // raising Faint for readability does not make disabled controls look enabled
        public static Color Disabled { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.30f)); } }
        // Grafit accent: switches and primary buttons use text colour
        public static Color Accent { get { return O(Ink.Base.Accent); } }
        public static Color OnAccent { get { return O(Ink.Base.OnAccent); } }
        // Keyboard focus: the accent where it reads on the canvas (>= 3:1), else primary ink
        public static Color Focus { get { var a = Accent; return Island.Contrast(a, Window) >= 3 ? a : Text; } }
        // Thumbs and knobs: paper white on light, ivory on night
        public static Color Knob { get { return Ink.Light ? Color.White : Text; } }
        // Canvas mixed toward primary ink: control fills that step evenly in both themes
        public static Color Tint(float k) { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, k)); }
        // A colour pushed away from the ink drawn on it, so hover and press only gain contrast
        public static Color Deepen(Color fill, Color ink, float k) { return Ink.Mix(fill, ink.GetBrightness() < 0.5f ? Color.White : Color.Black, k); }

        // One brush, pen and path per UI thread: Kit paints allocate nothing
        [ThreadStatic] static SolidBrush brush;
        [ThreadStatic] static Pen pen;
        [ThreadStatic] static GraphicsPath shape;

        public static SolidBrush BrushOf(Color c)
        {
            if (brush == null) brush = new SolidBrush(c); else brush.Color = c;
            return brush;
        }

        public static Pen PenOf(Color c, float width)
        {
            if (pen == null) pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            else { pen.Color = c; pen.Width = width; }
            return pen;
        }

        public static GraphicsPath Shape(RectangleF r, float radius)
        {
            if (shape == null) shape = new GraphicsPath(); else shape.Reset();
            Ink.AddRound(shape, r, radius);
            return shape;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c) { g.FillPath(BrushOf(c), Shape(r, radius)); }
        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float width) { g.DrawPath(PenOf(c, width), Shape(r, radius)); }

        // Elevation 1: contact shadow plus a short falloff below, never a halo
        public static void Lift(Graphics g, RectangleF r, float radius, float s)
        {
            var ink = Ink.Light ? Color.FromArgb(255, 0x2A, 0x20, 0x38) : Color.Black;
            int a = Ink.Light ? 26 : 90;
            FillRound(g, new RectangleF(r.X - 0.5f * s, r.Y + 1.5f * s, r.Width + s, r.Height + 0.5f * s), radius + 0.5f * s, Color.FromArgb(a / 2, ink));
            FillRound(g, new RectangleF(r.X, r.Y + 0.75f * s, r.Width, r.Height), radius, Color.FromArgb(a, ink));
        }

        // Keyboard focus: 2px ring in Focus with a hairline of the surface inside it,
        // so the ring reads against the canvas and against whatever fill it hugs
        public static void FocusRing(Graphics g, RectangleF ring, float radius, Color surface, float s)
        {
            float inset = 1.5f * s;
            StrokeRound(g, RectangleF.Inflate(ring, -inset, -inset), Math.Max(0, radius - inset), surface, 1.25f * s);
            StrokeRound(g, ring, radius, Focus, 2f * s);
        }

        // First opaque colour behind a control
        public static Color Under(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                if (p is KitGroup) return Group;
                if (p.BackColor.A == 255) return p.BackColor;
            }
            return Window;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        static StringFormat glyphFormat;

        public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, ContentAlignment align)
        {
            // Lone icon glyph: centre its ink, not its line box
            if (s != null && s.Length == 1 && align == ContentAlignment.MiddleCenter && f.Name == Ink.IconFamily)
            {
                if (glyphFormat == null)
                {
                    glyphFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
                    glyphFormat.FormatFlags |= StringFormatFlags.NoWrap;
                    glyphFormat.Alignment = StringAlignment.Center;
                    glyphFormat.LineAlignment = StringAlignment.Center;
                }
                var n = Ink.IconNudge(s[0], f.Size);
                float half = g.PixelOffsetMode == PixelOffsetMode.HighQuality || g.PixelOffsetMode == PixelOffsetMode.Half ? 0.5f : 0;
                float cx = r.X + r.Width / 2 + n.X + half, cy = r.Y + r.Height / 2 + n.Y + half;
                g.DrawString(s, f, KitInk.BrushOf(c), new RectangleF(cx - f.Size, cy - f.Size, f.Size * 2, f.Size * 2), glyphFormat);
                return;
            }
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            if (align == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            else if (align == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
            else flags |= TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, s, f, Rectangle.Round(r), c, flags);
        }
    }

    // Base: own HWND, focusable, window DPI
    // TextBox with Ctrl+Backspace and Ctrl+Delete
    class KitText : TextBox
    {
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && !e.Alt && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && !ReadOnly)
            {
                DeleteWord(e.KeyCode == Keys.Back);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        static int Kind(char c) { return char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2; }

        void DeleteWord(bool back)
        {
            if (SelectionLength == 0)
            {
                string x = Text;
                int at = SelectionStart, i = at;
                if (back)
                {
                    while (i > 0 && Kind(x[i - 1]) == 0) i--;
                    if (i > 0) { int k = Kind(x[i - 1]); while (i > 0 && Kind(x[i - 1]) == k) i--; }
                    Select(i, at - i);
                }
                else
                {
                    while (i < x.Length && Kind(x[i]) == 0) i++;
                    if (i < x.Length) { int k = Kind(x[i]); while (i < x.Length && Kind(x[i]) == k) i++; }
                    Select(at, i - at);
                }
            }
            // Keeps undo working
            SelectedText = "";
        }
    }

    abstract class KitControl : Control
    {
        protected KitControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = true;
            BackColor = Color.Transparent;
        }

        public float Ui { get { var w = FindForm() as KitWindow; return w != null ? w.Ui : 1f; } }
        protected bool Hovered;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { KitInk.ByMouse = true; base.OnMouseDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // Focus ring only for keyboard use, kept clear of the fill by a hairline of the surface
        protected void Ring(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues || KitInk.ByMouse) return;
            float s = Ui, grow = 2.5f * s;
            var ring = Inside(RectangleF.Inflate(r, grow, grow), Width, Height);
            KitInk.FocusRing(g, ring, Math.Min(radius + grow, ring.Height / 2), KitInk.Under(this), s);
        }

        // Rings stay whole inside the control
        public static RectangleF Inside(RectangleF r, int w, int h)
        {
            float x0 = Math.Max(r.Left, 1f), y0 = Math.Max(r.Top, 1f), x1 = Math.Min(r.Right, w - 1.5f), y1 = Math.Min(r.Bottom, h - 1.5f);
            return RectangleF.FromLTRB(x0, y0, Math.Max(x0 + 1, x1), Math.Max(y0 + 1, y1));
        }

        protected void Fill(Graphics g, RectangleF r, float radius, Color c) { KitInk.FillRound(g, r, radius, c); }
    }

    // iOS switch over CheckBox for readers
    class KitSwitch : CheckBox
    {
        bool hovered;
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { KitInk.ByMouse = true; base.OnMouseDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (KitInk.ByMouse) { KitInk.ByMouse = false; Invalidate(); } base.OnKeyUp(e); }

        float knob;
        readonly Timer anim = new Timer { Interval = 15 };

        public KitSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = "";
            anim.Tick += delegate
            {
                float want = Checked ? 1 : 0;
                knob += (want - knob) * (Anim.Reduced ? 1f : 0.35f);
                if (Math.Abs(want - knob) < 0.01f) { knob = want; anim.Stop(); }
                Invalidate();
            };
            CheckedChanged += delegate { anim.Start(); };
        }

        protected override void OnCreateControl() { base.OnCreateControl(); knob = Checked ? 1 : 0; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            var under = KitInk.Under(this);
            g.Clear(under);
            float s = Height / 24f;
            var track = new RectangleF(1, (Height - 22 * s) / 2, 40 * s, 22 * s);
            bool live = Enabled;
            // Off: quiet tinted track; on: accent. Disabled keeps a trace of the state.
            Color trackBg = live ? Ink.Mix(KitInk.Line, KitInk.Accent, knob) : Ink.Mix(KitInk.Line, KitInk.Accent, 0.3f * knob);
            if (hovered && live) trackBg = knob > 0.5f ? KitInk.Deepen(trackBg, KitInk.OnAccent, 0.08f) : Ink.Mix(trackBg, KitInk.Text, 0.07f);
            KitInk.FillRound(g, track, track.Height / 2, trackBg);

            float r = (hovered && live ? 8.5f : 8f) * s;
            float cx = track.X + 11 * s + knob * 18 * s, cy = track.Y + 11 * s;
            var dot = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            Color knobColor = live ? Ink.Mix(hovered ? Ink.Mix(KitInk.Sub, KitInk.Text, 0.35f) : KitInk.Sub, KitInk.OnAccent, knob) : KitInk.Disabled;
            if (live) KitInk.Lift(g, dot, r, s * 0.6f);
            KitInk.FillRound(g, dot, r, knobColor);

            if (Focused && ShowFocusCues && !KitInk.ByMouse)
            {
                var ring = KitControl.Inside(RectangleF.Inflate(track, 2.5f * s, 2.5f * s), Width, Height);
                KitInk.FocusRing(g, ring, ring.Height / 2, under, Math.Max(1f, s));
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    // Soft segmented choices; the thumb glides, arrows move between
    class KitSegmented : KitControl
    {
        public string[] Items = new string[0];
        int selected, hot = -1;
        // Thumb position in segments, gliding toward the pick; -1 until first paint
        float glide = -1;
        readonly Timer anim = new Timer { Interval = 15 };
        public event Action<int> Changed;

        public KitSegmented()
        {
            anim.Tick += delegate
            {
                glide += (selected - glide) * (Anim.Reduced ? 1f : 0.3f);
                if (Math.Abs(selected - glide) < 0.01f) { glide = selected; anim.Stop(); }
                Invalidate();
            };
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                if (IsHandleCreated && glide >= 0) anim.Start(); else glide = -1;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Selection, selected);
            }
        }

        RectangleF Cell(int i)
        {
            float w = (Width - 1f) / Math.Max(1, Items.Length);
            return new RectangleF(i * w, 0, w, Height - 1);
        }

        int HitCell(Point pt)
        {
            for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(pt)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int at = HitCell(e.Location);
            if (at != hot) { hot = at; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        void Pick(int i)
        {
            if (!Enabled || i < 0 || i >= Items.Length || i == selected) return;
            Selected = i;
            var h = Changed;
            if (h != null) h(i);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus();
            int at = HitCell(e.Location);
            if (at >= 0) Pick(at);
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Left) Pick(selected - 1);
            else if (e.KeyCode == Keys.Right) Pick(selected + 1);
            else if (e.KeyCode == Keys.Home) Pick(0);
            else if (e.KeyCode == Keys.End) Pick(Items.Length - 1);
            else return;
            KitInk.ByMouse = false;
            e.Handled = e.SuppressKeyPress = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Under(this));
            float s = Ui, radius = Ink.RadiusControl * s, inset = 3 * s;
            var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, box, radius, KitInk.Line);
            if (glide < 0) glide = selected;
            if (selected >= 0 && selected < Items.Length)
            {
                float w = (Width - 1f) / Items.Length;
                var thumb = new RectangleF(glide * w + inset, inset, w - 2 * inset, Height - 1 - 2 * inset);
                if (Enabled) KitInk.Lift(g, thumb, radius - inset, s);
                Fill(g, thumb, radius - inset, Enabled ? KitInk.Raised : KitInk.Group);
            }
            Font f = Ink.Text(12.5f * s), fs = Ink.Semi(12.5f * s);
            for (int i = 0; i < Items.Length; i++)
                KitInk.Draw(g, Items[i], i == selected ? fs : f, !Enabled ? KitInk.Faint : i == selected || i == hot ? KitInk.Text : KitInk.Sub, Cell(i), ContentAlignment.MiddleCenter);
            Ring(g, box, radius);
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

        class Access : ControlAccessibleObject
        {
            readonly KitSegmented owner;
            public Access(KitSegmented owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
            public override int GetChildCount() { return owner.Items.Length; }
            public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.Items.Length ? new Item(owner, i) : null; }
            public override AccessibleObject GetSelected() { return GetChild(owner.selected); }
            public override AccessibleObject GetFocused() { return owner.Focused ? GetChild(owner.selected) : null; }
        }

        class Item : AccessibleObject
        {
            readonly KitSegmented owner;
            readonly int index;
            public Item(KitSegmented owner, int index) { this.owner = owner; this.index = index; }
            public override string Name { get { return owner.Items[index]; } }
            public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get { return AccessibleStates.Selectable | AccessibleStates.Focusable | (!owner.Enabled ? AccessibleStates.Unavailable : 0) | (owner.Focused && index == owner.selected ? AccessibleStates.Focused : 0) | (index == owner.selected ? AccessibleStates.Selected | AccessibleStates.Checked : 0); }
            }
            public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
            public override string DefaultAction { get { return Lang.T("Pilih"); } }
            public override void DoDefaultAction() { owner.Pick(index); }
        }
    }

}
