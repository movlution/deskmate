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
        public static Color Window { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.0f : 0.04f)); } }
        public static Color Side { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.05f : 0.07f)); } }
        public static Color Group { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.035f : 0.09f)); } }
        public static Color Line { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.12f : 0.15f)); } }
        public static Color Hover { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.09f : 0.16f)); } }
        // Selected segment, lifted above its track
        public static Color Raised { get { return Ink.Light ? Color.White : O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.3f)); } }
        public static Color Text { get { return O(Ink.Base.Text); } }
        // Light Sub sits above Faint (0.65) so secondary text keeps hierarchy and >= 4.5:1 on Line
        public static Color Sub { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.67f : 0.62f)); } }
        // 0.65 light / 0.59 dark guarantee >= 4.5:1 across Window, Side, Group, Hover, and Line
        public static Color Faint { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.65f : 0.59f)); } }
        // Disabled chrome (switch knob, slider fill): dimmer than tertiary text so
        // raising Faint for readability does not make disabled controls look enabled
        public static Color Disabled { get { return O(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.30f)); } }
        // Monochrome accent: switches use text colour
        public static Color Accent { get { return O(Ink.Base.Accent); } }
        public static Color OnAccent { get { return O(Ink.Base.OnAccent); } }

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
                using (var b = new SolidBrush(c)) g.DrawString(s, f, b, new RectangleF(cx - f.Size, cy - f.Size, f.Size * 2, f.Size * 2), glyphFormat);
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

        // Focus ring only for keyboard use
        protected void Ring(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues || KitInk.ByMouse) return;
            var ring = Inside(RectangleF.Inflate(r, 2, 2), Width, Height);
            using (var path = Ink.Round(ring, Math.Min(radius + 2, ring.Height / 2)))
            using (var pen = new Pen(KitInk.Text, 2f)) g.DrawPath(pen, path);
        }

        // Rings stay whole inside the control
        public static RectangleF Inside(RectangleF r, int w, int h)
        {
            float x0 = Math.Max(r.Left, 1f), y0 = Math.Max(r.Top, 1f), x1 = Math.Min(r.Right, w - 1.5f), y1 = Math.Min(r.Bottom, h - 1.5f);
            return RectangleF.FromLTRB(x0, y0, Math.Max(x0 + 1, x1), Math.Max(y0 + 1, y1));
        }

        protected void Fill(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var path = Ink.Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }
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
            g.Clear(KitInk.Under(this));
            float s = Height / 24f;
            var track = new RectangleF(1, (Height - 22 * s) / 2, 40 * s, 22 * s);
            Color trackBg = Enabled ? Ink.Mix(KitInk.Line, KitInk.Accent, knob) : KitInk.Line;
            if (hovered && Enabled) trackBg = Ink.Mix(trackBg, KitInk.Text, 0.06f);
            using (var path = Ink.Round(track, 11 * s))
            using (var b = new SolidBrush(trackBg)) g.FillPath(b, path);

            float r = (hovered && Enabled ? 9.5f : 9f) * s;
            float cx = track.X + 11 * s + knob * 18 * s, cy = track.Y + 11 * s;
            Color knobColor = Enabled ? Ink.Mix(KitInk.Text, KitInk.OnAccent, knob) : KitInk.Disabled;
            if (hovered && Enabled && knob == 0) knobColor = Ink.Mix(knobColor, KitInk.Window, 0.08f);
            using (var b = new SolidBrush(knobColor)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);

            if (Focused && ShowFocusCues && !KitInk.ByMouse)
            {
                var ring = KitControl.Inside(RectangleF.Inflate(track, 2, 2), Width, Height);
                using (var path = Ink.Round(ring, ring.Height / 2))
                using (var pen = new Pen(KitInk.Text, 1.6f)) g.DrawPath(pen, path);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    // Pill choices; arrows move between
    class KitSegmented : KitControl
    {
        public string[] Items = new string[0];
        int selected;
        public event Action<int> Changed;

        public int Selected
        {
            get { return selected; }
            set { if (value == selected) return; selected = value; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.Selection, selected); }
        }

        RectangleF Cell(int i)
        {
            // Active pill fills its segment fully
            float w = (Width - 1f) / Math.Max(1, Items.Length);
            return new RectangleF(i * w, 0, w, Height - 1);
        }

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
            for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(e.Location)) { Pick(i); return; }
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
            var box = new RectangleF(0, 0, Width - 1, Height - 1);
            Fill(g, box, Height / 2f, KitInk.Line);
            var f = new Font(Ink.MenuFamily, 12.5f * Ui, FontStyle.Regular, GraphicsUnit.Pixel);
            var fs = new Font(Ink.MenuFamily, 12.5f * Ui, FontStyle.Regular, GraphicsUnit.Pixel);
            for (int i = 0; i < Items.Length; i++)
            {
                var c = Cell(i);
                if (i == selected) Fill(g, c, c.Height / 2, Enabled ? KitInk.Raised : KitInk.Group);
                KitInk.Draw(g, Items[i], i == selected ? fs : f, !Enabled ? KitInk.Faint : i == selected ? KitInk.Text : KitInk.Sub, c, ContentAlignment.MiddleCenter);
            }
            f.Dispose();
            fs.Dispose();
            Ring(g, box, Height / 2f);
        }

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
