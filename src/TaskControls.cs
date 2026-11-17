using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Colours and text shared by the task controls. Everything derives from KitInk tokens
    static class TaskInk
    {
        public static Color Card { get { return KitInk.Group; } }
        public static Color RowHover { get { return Ink.Mix(KitInk.Group, KitInk.Hover, 0.55f); } }
        public static Color RowSelected { get { return KitInk.Hover; } }
        public static Color Field { get { return Ink.Mix(KitInk.Group, KitInk.Text, 0.05f); } }

        // Opaque blend: GDI text ignores alpha, so fades are mixed into the background instead
        public static Color Fade(Color bg, Color fg, float a) { return Ink.Mix(bg, fg, Math.Max(0f, Math.Min(1f, a))); }

        public static string FirstLine(string notes)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            foreach (var raw in notes.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length > 0) return l.Length > 200 ? l.Substring(0, 200) : l;
            }
            return "";
        }

        // "Hari ini", "Besok", or the date; the hour only when the task has one
        public static string DueText(DateTime d)
        {
            string day;
            if (d.Date == DateTime.Today) day = Lang.T("Hari ini");
            else if (d.Date == DateTime.Today.AddDays(1)) day = Lang.T("Besok");
            else if (d.Date == DateTime.Today.AddDays(-1)) day = Lang.T("Kemarin");
            else day = d.ToString(d.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
            return d.TimeOfDay == TimeSpan.Zero ? day : day + " " + d.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // Whole-day overdue, or past the hour for a timed task
        public static bool Overdue(TodoItem t)
        {
            if (t.Done || !t.Due.HasValue) return false;
            var d = t.Due.Value;
            return d.TimeOfDay == TimeSpan.Zero ? d.Date < DateTime.Today : d < DateTime.Now;
        }

        // Checkmark inside a box, drawn to progress p (0..1)
        public static void CheckMark(Graphics g, RectangleF box, Color c, float p, float width)
        {
            if (p <= 0) return;
            var a = new PointF(box.X + box.Width * 0.27f, box.Y + box.Height * 0.53f);
            var b = new PointF(box.X + box.Width * 0.44f, box.Y + box.Height * 0.69f);
            var d = new PointF(box.X + box.Width * 0.74f, box.Y + box.Height * 0.34f);
            float l1 = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            float l2 = (float)Math.Sqrt((d.X - b.X) * (d.X - b.X) + (d.Y - b.Y) * (d.Y - b.Y));
            float run = p * (l1 + l2);
            var pts = new List<PointF>();
            pts.Add(a);
            if (run <= l1) pts.Add(new PointF(a.X + (b.X - a.X) * run / l1, a.Y + (b.Y - a.Y) * run / l1));
            else
            {
                pts.Add(b);
                float k = Math.Min(1f, (run - l1) / l2);
                pts.Add(new PointF(b.X + (d.X - b.X) * k, b.Y + (d.Y - b.Y) * k));
            }
            using (var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, pts.ToArray());
        }

        // Rounded fill, the one place that builds the path
        public static void Pill(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var path = Ink.Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }
    }

    // Wheel that reaches a control under the cursor even while a text box has the focus
    interface IWheelTarget
    {
        void Wheel(int delta);
    }

    // Round icon button without chrome: hover pill, glyph from the icon font
    sealed class TaskIconButton : KitControl
    {
        public char Glyph = '\uE712';
        public float GlyphPx = 16;
        public Func<Color> Back;
        public bool Active;
        public Color ActiveColor;
        public Color DotColor;
        bool pressed;
        public TaskIconButton()
        {
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Enter || base.IsInputKey(key); }

        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseLeave(EventArgs e) { pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool fire = pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            pressed = false;
            Invalidate();
            if (fire) OnClick(EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.Handled = true; OnClick(EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            Color back = Back != null ? Back() : TaskInk.Card;
            g.Clear(back);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            Color actCol = ActiveColor.IsEmpty ? KitInk.Accent : ActiveColor;
            if (Enabled && (Hovered || pressed || Active))
            {
                Color fill = pressed ? KitInk.Line : (Hovered ? KitInk.Hover : Ink.Mix(back, actCol, 0.14f));
                TaskInk.Pill(g, r, r.Height / 2f, fill);
            }
            Color fg = !Enabled ? KitInk.Disabled : (Active ? actCol : (Hovered ? KitInk.Text : KitInk.Sub));
            KitInk.Draw(g, Glyph.ToString(), Ink.Icons(GlyphPx * Ui), fg, r, ContentAlignment.MiddleCenter);
            if (Enabled && !DotColor.IsEmpty)
            {
                float s = Ui;
                float ds = 6.5f * s;
                float dx = r.Right - ds - 3 * s;
                float dy = r.Bottom - ds - 3 * s;
                using (var ring = new SolidBrush(back))
                    g.FillEllipse(ring, dx - 1.5f * s, dy - 1.5f * s, ds + 3 * s, ds + 3 * s);
                using (var b = new SolidBrush(DotColor))
                    g.FillEllipse(b, dx, dy, ds, ds);
            }
            Ring(g, r, r.Height / 2f);
        }
    }

    // List title above the tasks. In a narrow window it doubles as the list switcher
    sealed class TaskTitle : KitControl
    {
        public string Title = "", Subtitle = "";
        bool compact;
        public Func<Color> Back;

        public TaskTitle() { SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false); TabStop = false; }

        public bool Compact
        {
            get { return compact; }
            set { compact = value; TabStop = value; Cursor = value ? Cursors.Hand : Cursors.Default; AccessibleRole = value ? AccessibleRole.PushButton : AccessibleRole.StaticText; Invalidate(); }
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Enter || base.IsInputKey(key); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (compact && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (compact && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { e.Handled = true; OnClick(EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(Back != null ? Back() : TaskInk.Card);
            float s = Ui;
            var ft = Ink.Display(24f * s);
            var fs = Ink.Text(12.5f * s);
            float titleH = 30 * s;
            int tw = TextRenderer.MeasureText(Title, ft, new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            float room = Width - (compact ? 28 * s : 0);
            float w = Math.Min(room, tw + 4 * s);
            KitInk.Draw(g, Title, ft, KitInk.Text, new RectangleF(0, 0, w, titleH), ContentAlignment.MiddleLeft);
            if (compact) KitInk.Draw(g, "\uE70D", Ink.Icons(12f * s), Hovered ? KitInk.Text : KitInk.Sub, new RectangleF(w + 4 * s, 0, 20 * s, titleH), ContentAlignment.MiddleCenter);
            if (Subtitle.Length > 0)
                KitInk.Draw(g, Subtitle, fs, KitInk.Sub, new RectangleF(0, 31 * s, Width, 20 * s), ContentAlignment.MiddleLeft);
            if (compact) Ring(g, new RectangleF(0, 0, w + 24 * s, titleH), 8 * s);
        }
    }

    // Quiet sync line: dot and text. Clicking an error tries again
    sealed class TaskSyncStatus : KitControl
    {
        public new string Text = "";
        public bool Error, Busy;
        public Func<Color> Back;

        public TaskSyncStatus()
        {
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
        }

        public void Set(string text, bool error, bool busy)
        {
            if (text == Text && error == Error && busy == Busy) return;
            Text = text; Error = error; Busy = busy;
            AccessibleName = text;
            TabStop = error;
            Cursor = error ? Cursors.Hand : Cursors.Default;
            AccessibleRole = error ? AccessibleRole.PushButton : AccessibleRole.StaticText;
            Invalidate();
        }

        public int PreferredWidth()
        {
            float s = Ui;
            return TextRenderer.MeasureText(Text, Ink.Text(12f * s), new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(22 * s);
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Enter || base.IsInputKey(key); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Error && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Error && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { e.Handled = true; OnClick(EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            Color back = Back != null ? Back() : KitInk.Window;
            g.Clear(back);
            if (Text.Length == 0) return;
            float s = Ui;
            Color c = Error ? Ink.Base.Negative : KitInk.Sub;
            float d = 7 * s, cy = Height / 2f;
            // A busy dot breathes; reduced motion keeps it steady
            Color dot = Busy && !Anim.Reduced ? TaskInk.Fade(back, c, 0.45f + 0.55f * (float)(0.5 + 0.5 * Math.Sin(Environment.TickCount / 220.0))) : (Error ? c : KitInk.Faint);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, 1 * s, cy - d / 2, d, d);
            KitInk.Draw(g, Text, Ink.Text(12f * s), c, new RectangleF(d + 8 * s, 0, Width - d - 8 * s, Height), ContentAlignment.MiddleLeft);
            Ring(g, new RectangleF(0, 0, Width - 1, Height - 1), 6 * s);
        }
    }

    // Sidebar of views and lists
    sealed class RailItem
    {
        public const int View = 0, List = 1, Heading = 2, New = 3;
        public int Kind;
        public string Key = "", Text = "";
        public int Count = -1;
        public char Glyph;
    }

    sealed class TaskRail : KitControl, IWheelTarget
    {
        public List<RailItem> Items = new List<RailItem>();
        public event Action<string> Picked;
        public event Action<string, Point> ItemMenu;
        public event Action NewList;
        string selected = "";
        int hover = -1;
        float scroll;

        public string Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.Selection, 0); }
        }

        public TaskRail() { AccessibleRole = AccessibleRole.List; }

        float RowH { get { return 36 * Ui; } }
        float HeadH { get { return 30 * Ui; } }
        float Gap { get { return 2 * Ui; } }

        float Height2(RailItem it) { return it.Kind == RailItem.Heading ? HeadH : RowH; }

        float Total()
        {
            float y = 4 * Ui;
            foreach (var it in Items) y += Height2(it) + Gap;
            return y + 4 * Ui;
        }

        RectangleF Cell(int i)
        {
            float y = 4 * Ui - scroll;
            for (int k = 0; k < i; k++) y += Height2(Items[k]) + Gap;
            return new RectangleF(4 * Ui, y, Width - 8 * Ui, Height2(Items[i]));
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Kind == RailItem.Heading) continue;
                if (Cell(i).Contains(p)) return i;
            }
            return -1;
        }

        int IndexOfKey(string key)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Kind != RailItem.Heading && Items[i].Kind != RailItem.New && Items[i].Key == key) return i;
            return -1;
        }

        public void Wheel(int delta)
        {
            float max = Math.Max(0, Total() - Height);
            scroll = Math.Max(0, Math.Min(max, scroll - delta / 120f * 48 * Ui));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != hover) { hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = IndexAt(e.Location);
            if (i < 0) return;
            var it = Items[i];
            if (e.Button == MouseButtons.Right)
            {
                if (it.Kind == RailItem.List && ItemMenu != null) ItemMenu(it.Key, PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            Activate(it);
        }

        void Activate(RailItem it)
        {
            if (it.Kind == RailItem.New) { if (NewList != null) NewList(); return; }
            if (it.Kind == RailItem.Heading) return;
            Selected = it.Key;
            if (Picked != null) Picked(it.Key);
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.Enter || base.IsInputKey(key); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int at = IndexOfKey(selected);
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                int step = e.KeyCode == Keys.Down ? 1 : -1;
                for (int i = at + step; i >= 0 && i < Items.Count; i += step)
                    if (Items[i].Kind == RailItem.View || Items[i].Kind == RailItem.List) { Activate(Items[i]); Reveal(i); break; }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                if (at >= 0 && Items[at].Kind == RailItem.List && ItemMenu != null) { var c = Cell(at); ItemMenu(Items[at].Key, PointToScreen(new Point((int)c.X + 20, (int)c.Bottom))); }
                e.Handled = true;
            }
        }

        void Reveal(int i)
        {
            var c = Cell(i);
            if (c.Top < 0) scroll += c.Top - 4 * Ui;
            else if (c.Bottom > Height) scroll += c.Bottom - Height + 4 * Ui;
            scroll = Math.Max(0, Math.Min(Math.Max(0, Total() - Height), scroll));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Window);
            float s = Ui;
            var fText = Ink.Text(13.5f * s);
            var fBold = Ink.Semi(13.5f * s);
            var fHead = Ink.SmallSemi(11f * s);
            var fCount = Ink.Small(11.5f * s);
            var fIcon = Ink.Icons(15f * s);
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                var c = Cell(i);
                if (c.Bottom < 0 || c.Top > Height) continue;
                if (it.Kind == RailItem.Heading)
                {
                    KitInk.Draw(g, it.Text.ToUpper(CultureInfo.CurrentCulture), fHead, KitInk.Sub, new RectangleF(c.X + 12 * s, c.Y + 6 * s, c.Width - 12 * s, c.Height - 6 * s), ContentAlignment.MiddleLeft);
                    continue;
                }
                bool sel = it.Kind != RailItem.New && it.Key == selected;
                Color bg = KitInk.Window;
                if (sel) bg = KitInk.Hover;
                else if (i == hover) bg = Ink.Mix(KitInk.Window, KitInk.Hover, 0.5f);
                if (sel || i == hover) TaskInk.Pill(g, c, 10 * s, bg);
                if (sel) TaskInk.Pill(g, new RectangleF(c.X + 2 * s, c.Y + 9 * s, 3 * s, c.Height - 18 * s), 1.5f * s, KitInk.Accent);
                Color fg = sel ? KitInk.Text : KitInk.Sub;
                if (it.Kind == RailItem.New && i == hover) fg = KitInk.Text;
                KitInk.Draw(g, it.Glyph.ToString(), fIcon, fg, new RectangleF(c.X + 10 * s, c.Y, 24 * s, c.Height), ContentAlignment.MiddleCenter);
                float right = c.Right - 12 * s;
                if (it.Count >= 0)
                {
                    string n = it.Count.ToString(CultureInfo.InvariantCulture);
                    int w = TextRenderer.MeasureText(n, fCount, new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    if (it.Count > 0) KitInk.Draw(g, n, fCount, KitInk.Sub, new RectangleF(right - w - 2 * s, c.Y, w + 4 * s, c.Height), ContentAlignment.MiddleRight);
                    right -= w + 10 * s;
                }
                KitInk.Draw(g, it.Text, sel ? fBold : fText, fg, new RectangleF(c.X + 42 * s, c.Y, Math.Max(10, right - (c.X + 42 * s)), c.Height), ContentAlignment.MiddleLeft);
            }
            int at = IndexOfKey(selected);
            if (at >= 0) Ring(g, Cell(at), 10 * s);
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

        class Access : ControlAccessibleObject
        {
            readonly TaskRail owner;
            public Access(TaskRail owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return owner.Items.Count; }
            public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.Items.Count ? new Row(owner, i) : null; }
        }

        class Row : AccessibleObject
        {
            readonly TaskRail owner;
            readonly int index;
            public Row(TaskRail owner, int index) { this.owner = owner; this.index = index; }
            RailItem Item { get { return owner.Items[index]; } }
            public override string Name { get { return Item.Count >= 0 ? Item.Text + ", " + Item.Count : Item.Text; } }
            public override AccessibleRole Role { get { return Item.Kind == RailItem.Heading ? AccessibleRole.StaticText : AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get { return Item.Kind == RailItem.Heading ? AccessibleStates.ReadOnly : AccessibleStates.Selectable | AccessibleStates.Focusable | (Item.Key == owner.selected && Item.Kind != RailItem.New ? AccessibleStates.Selected : 0); }
            }
            public override Rectangle Bounds { get { return owner.RectangleToScreen(Rectangle.Round(owner.Cell(index))); } }
            public override string DefaultAction { get { return Lang.T("Pilih"); } }
            public override void DoDefaultAction() { owner.Activate(Item); }
        }
    }

    // Borderless entry that hands the wheel to the task list and supports clean placeholders
    sealed class TaskAddBox : KitText
    {
        public IWheelTarget Wheel;
        public string Placeholder = "";
        int lastLen;

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if ((lastLen == 0) != (TextLength == 0)) Invalidate();
            lastLen = TextLength;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A && Wheel != null)
            {
                Wheel.Wheel((short)(((long)m.WParam >> 16) & 0xFFFF));
                return;
            }
            base.WndProc(ref m);
            if ((m.Msg == 0x000F || m.Msg == 0x0318) && TextLength == 0 && !string.IsNullOrEmpty(Placeholder) && IsHandleCreated)
            {
                var rect = Multiline ? new Rectangle(2, 1, Math.Max(1, Width - 4), Math.Max(1, Height - 2))
                                     : new Rectangle(2, 0, Math.Max(1, Width - 4), Math.Max(1, Height));
                var flags = Multiline
                    ? (TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak)
                    : (TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (m.Msg == 0x0318 && m.WParam != IntPtr.Zero)
                {
                    using (var g = Graphics.FromHdc(m.WParam))
                        TextRenderer.DrawText(g, Placeholder, Font, rect, KitInk.Sub, flags);
                }
                else
                {
                    using (var g = CreateGraphics())
                        TextRenderer.DrawText(g, Placeholder, Font, rect, KitInk.Sub, flags);
                }
            }
        }
    }
}
