using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Deskmate
{
    static class NotePalette
    {
        public static readonly string[] Keys = new[] { "", "amber", "emerald", "sky", "violet", "rose" };

        public static Color ColorOf(string name)
        {
            if (string.Equals(name, "amber", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(245, 158, 11);
            if (string.Equals(name, "emerald", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(16, 185, 129);
            if (string.Equals(name, "sky", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(14, 165, 233);
            if (string.Equals(name, "violet", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(139, 92, 246);
            if (string.Equals(name, "rose", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(244, 63, 94);
            return Color.Empty;
        }

        public static string NameOf(string key, bool forFilter)
        {
            if (string.IsNullOrEmpty(key)) return Lang.T(forFilter ? "Semua warna" : "Bawaan");
            if (string.Equals(key, "amber", StringComparison.OrdinalIgnoreCase)) return Lang.T("Amber");
            if (string.Equals(key, "emerald", StringComparison.OrdinalIgnoreCase)) return Lang.T("Zamrud");
            if (string.Equals(key, "sky", StringComparison.OrdinalIgnoreCase)) return Lang.T("Biru");
            if (string.Equals(key, "violet", StringComparison.OrdinalIgnoreCase)) return Lang.T("Ungu");
            if (string.Equals(key, "rose", StringComparison.OrdinalIgnoreCase)) return Lang.T("Mawar");
            return key;
        }

        public static string FormatTags(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim();
                if (t.Length == 0) continue;
                list.Add(t.StartsWith("#", StringComparison.Ordinal) ? t : "#" + t);
            }
            return string.Join(" ", list.ToArray());
        }

        public static string CleanTags(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string t = parts[i].Trim().TrimStart('#').Trim();
                if (t.Length == 0 || !seen.Add(t)) continue;
                list.Add(t);
            }
            return string.Join(", ", list.ToArray());
        }
    }

    // Clean spaced color filter dots matching KitPalette
    sealed class NoteColorStrip : KitControl
    {
        public string Selected = "";
        public event Action<string> Picked;
        public Func<Color> Back;
        readonly ToolTip tips;
        int hovered = -1;
        int tipIndex = -1;

        public NoteColorStrip(ToolTip tips)
        {
            this.tips = tips;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = Lang.T("Filter warna catatan");
            Cursor = Cursors.Hand;
        }

        int SelectedIndex()
        {
            for (int i = 0; i < NotePalette.Keys.Length; i++)
                if (string.Equals(Selected, NotePalette.Keys[i], StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        PointF Center(int i)
        {
            int count = NotePalette.Keys.Length;
            float step = Math.Min(21f * Ui, (Width - 6f * Ui) / Math.Max(1, count));
            float totalW = step * count;
            float startX = Width - totalW + step / 2f - 1f * Ui;
            return new PointF(startX + i * step, Height / 2f);
        }

        int HitTest(Point pt)
        {
            float r = 10f * Ui;
            for (int i = 0; i < NotePalette.Keys.Length; i++)
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
            if (hit != hovered)
            {
                hovered = hit;
                Invalidate();
            }
            if (tips != null && hit != tipIndex)
            {
                tipIndex = hit;
                tips.SetToolTip(this, hit >= 0 ? NotePalette.NameOf(NotePalette.Keys[hit], true) : "");
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = -1;
            tipIndex = -1;
            if (tips != null) tips.SetToolTip(this, "");
            base.OnMouseLeave(e);
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
            if (i < 0 || i >= NotePalette.Keys.Length) return;
            string next = NotePalette.Keys[i];
            if (i > 0 && string.Equals(Selected, next, StringComparison.OrdinalIgnoreCase)) next = "";
            Selected = next;
            Invalidate();
            if (Picked != null) Picked(Selected);
        }

        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || base.IsInputKey(key); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int idx = SelectedIndex();
            if (e.KeyCode == Keys.Left) Pick(Math.Max(0, idx - 1));
            else if (e.KeyCode == Keys.Right) Pick(Math.Min(NotePalette.Keys.Length - 1, idx + 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            Color back = Back != null ? Back() : KitInk.Window;
            g.Clear(back);
            float s = Ui;
            float r = 5.5f * s;
            int sel = SelectedIndex();

            for (int i = 0; i < NotePalette.Keys.Length; i++)
            {
                var pt = Center(i);
                string key = NotePalette.Keys[i];
                Color dot = NotePalette.ColorOf(key);
                bool isAll = dot == Color.Empty;
                Color ringCol = isAll ? KitInk.Sub : dot;

                if (i == sel)
                {
                    float rr = r + 2.8f * s;
                    using (var pen = new Pen(isAll ? KitInk.Text : dot, 1.8f * s))
                        g.DrawEllipse(pen, pt.X - rr, pt.Y - rr, rr * 2, rr * 2);
                }
                else if (i == hovered)
                {
                    float rr = r + 2.4f * s;
                    using (var pen = new Pen(Ink.Mix(back, ringCol, 0.5f), 1.4f * s))
                        g.DrawEllipse(pen, pt.X - rr, pt.Y - rr, rr * 2, rr * 2);
                }

                if (isAll)
                {
                    using (var b = new SolidBrush(KitInk.Group))
                        g.FillEllipse(b, pt.X - r, pt.Y - r, r * 2, r * 2);
                    using (var pen = new Pen(i == sel ? KitInk.Text : KitInk.Sub, 1.4f * s))
                        g.DrawEllipse(pen, pt.X - r, pt.Y - r, r * 2, r * 2);
                }
                else
                {
                    using (var b = new SolidBrush(dot))
                        g.FillEllipse(b, pt.X - r, pt.Y - r, r * 2, r * 2);
                }
            }
            Ring(g, new RectangleF(1, 1, Width - 2, Height - 2), 8 * s);
        }
    }

    // Clickable tag pill in editor toolbar
    sealed class NoteTagChip : KitControl
    {
        public string Tags = "";
        public Func<Color> Back;
        bool pressed;

        public NoteTagChip()
        {
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
        }

        public int PreferredWidth(int maxWidth)
        {
            float s = Ui;
            string label = DisplayText();
            int tw = TextRenderer.MeasureText(label, Ink.Small(12f * s), new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int w = tw + (int)(42 * s);
            return Math.Max((int)(90 * s), Math.Min(maxWidth, w));
        }

        string DisplayText()
        {
            string formatted = NotePalette.FormatTags(Tags);
            return formatted.Length > 0 ? formatted : Lang.T("Tambah tag");
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
            Color back = Back != null ? Back() : KitInk.Group;
            g.Clear(back);
            float s = Ui;
            bool hasTags = !string.IsNullOrWhiteSpace(Tags);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = pressed
                ? Ink.Mix(back, KitInk.Text, 0.14f)
                : (Hovered ? Ink.Mix(back, KitInk.Text, 0.10f) : Ink.Mix(back, KitInk.Text, hasTags ? 0.065f : 0.04f));
            TaskInk.Pill(g, r, r.Height / 2f, fill);
            Color fg = hasTags ? KitInk.Text : (Hovered ? KitInk.Text : KitInk.Sub);
            KitInk.Draw(g, "\uE8EC", Ink.Icons(12f * s), hasTags ? KitInk.Accent : fg, new RectangleF(r.X + 8 * s, r.Y, 16 * s, r.Height), ContentAlignment.MiddleCenter);
            KitInk.Draw(g, DisplayText(), Ink.Small(12f * s), fg, new RectangleF(r.X + 27 * s, r.Y, Math.Max(10, r.Width - 36 * s), r.Height), ContentAlignment.MiddleLeft);
            Ring(g, r, r.Height / 2f);
        }
    }

    // Owner-drawn sidebar list with hover actions and smooth scrollbar
    sealed class NoteListView : KitControl, IWheelTarget
    {
        public const int CNone = 0, CPin = 1, CDelete = 2;

        sealed class Rw
        {
            public NoteInfo Note;
            public int Y, H;
        }

        readonly List<Rw> rows = new List<Rw>();
        string selPath;
        string hoverPath;
        int hoverRow = -1, hoverCell = CNone;
        float total, scroll, scrollTarget;
        readonly Dictionary<string, float> hot = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly Timer anim = new Timer { Interval = 15 };
        DateTime lastStep;
        bool dragBar;
        float dragGrab;

        public event Action<NoteInfo> SelectedChanged;
        public event Action<NoteInfo> TogglePinItem;
        public event Action<NoteInfo> DeleteItem;
        public event Action<NoteInfo, Point> ItemMenu;

        public NoteListView()
        {
            AccessibleRole = AccessibleRole.List;
            AccessibleName = Lang.T("Daftar catatan");
            anim.Tick += delegate { Step(); };
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }

        public void SetData(List<NoteInfo> items, string selectedPath)
        {
            float s = Ui;
            selPath = selectedPath;
            rows.Clear();
            float y = 6 * s;
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var n = items[i];
                    int h = (int)((n.Preview.Length > 0 ? 72 : 56) * s);
                    rows.Add(new Rw { Note = n, Y = (int)y, H = h });
                    y += h;
                }
            }
            total = y + 6 * s;
            ClampScroll();
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Reorder, 0);
        }

        int SelIndex()
        {
            if (selPath == null) return -1;
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].Note.Path, selPath, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void Kick()
        {
            if (Anim.Reduced) return;
            lastStep = DateTime.UtcNow;
            if (!anim.Enabled) anim.Start();
        }

        void Step()
        {
            var now = DateTime.UtcNow;
            float dt = (float)Math.Min(60, (now - lastStep).TotalMilliseconds);
            lastStep = now;
            bool busy = false;
            foreach (var k in new List<string>(hot.Keys))
            {
                float target = string.Equals(k, hoverPath, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
                float v = hot[k];
                v += Math.Sign(target - v) * dt / 130f;
                if ((target > v && hot[k] > target) || (target < v && hot[k] < target) || Math.Abs(target - v) < 0.001f) v = target;
                if (v <= 0f && target == 0f) hot.Remove(k); else { hot[k] = v; if (v != target) busy = true; }
            }
            if (Math.Abs(scrollTarget - scroll) > 0.4f)
            {
                scroll += (scrollTarget - scroll) * (1f - (float)Math.Exp(-dt / 55f));
                if (Math.Abs(scrollTarget - scroll) <= 0.4f) scroll = scrollTarget; else busy = true;
            }
            Invalidate();
            if (!busy) anim.Stop();
        }

        float HotAmt(string path)
        {
            float v;
            if (hot.TryGetValue(path, out v)) return v;
            return string.Equals(path, hoverPath, StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
        }

        void SetHover(string path, int row, int cell)
        {
            if (!string.Equals(path, hoverPath, StringComparison.OrdinalIgnoreCase))
            {
                if (hoverPath != null && !hot.ContainsKey(hoverPath)) hot[hoverPath] = 1f;
                if (path != null && !hot.ContainsKey(path)) hot[path] = 0f;
                hoverPath = path;
                Kick();
                if (Anim.Reduced) hot.Clear();
            }
            if (row != hoverRow || cell != hoverCell) { hoverRow = row; hoverCell = cell; Invalidate(); }
        }

        void ClampScroll()
        {
            float max = Math.Max(0, total - Height);
            scrollTarget = Math.Max(0, Math.Min(max, scrollTarget));
            scroll = Math.Max(0, Math.Min(max, scroll));
        }

        public void Wheel(int delta)
        {
            scrollTarget -= delta / 120f * 64 * Ui;
            ClampScroll();
            if (Anim.Reduced) scroll = scrollTarget; else Kick();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ClampScroll(); }

        RectangleF Bar()
        {
            float s = Ui, max = total - Height;
            if (max <= 1 || Height <= 0) return RectangleF.Empty;
            float th = Math.Max(32 * s, Height * Height / total);
            float y = scroll / max * (Height - th - 8 * s) + 4 * s;
            float w = (dragBar || (Hovered && PointToClient(Cursor.Position).X > Width - 16 * s)) ? 6 * s : 3.5f * s;
            return new RectangleF(Width - w - 4 * s, y, w, th);
        }

        void Reveal(int i)
        {
            if (i < 0 || i >= rows.Count) return;
            var r = rows[i];
            float s = Ui;
            if (r.Y < scrollTarget + 4 * s) scrollTarget = r.Y - 4 * s;
            else if (r.Y + r.H > scrollTarget + Height - 4 * s) scrollTarget = r.Y + r.H - Height + 8 * s;
            ClampScroll();
            if (Anim.Reduced) scroll = scrollTarget; else Kick();
            Invalidate();
        }

        int RowAt(float y)
        {
            float c = y + scroll;
            int lo = 0, hi = rows.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var r = rows[mid];
                if (c < r.Y) hi = mid - 1;
                else if (c >= r.Y + r.H) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        void ActionRects(Rw r, float top, out RectangleF pinRect, out RectangleF delRect)
        {
            float s = Ui;
            float sz = 24 * s;
            float rx = Width - 12 * s;
            float ay = top + 7 * s;
            delRect = new RectangleF(rx - sz, ay, sz, sz);
            pinRect = new RectangleF(delRect.Left - sz - 2 * s, ay, sz, sz);
        }

        int CellAt(int index, Point p)
        {
            if (index < 0 || index >= rows.Count) return CNone;
            var r = rows[index];
            RectangleF pinRect, delRect;
            ActionRects(r, r.Y - scroll, out pinRect, out delRect);
            if (delRect.Contains(p)) return CDelete;
            if (pinRect.Contains(p)) return CPin;
            return CNone;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Window);
            float s = Ui;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                float top = r.Y - scroll;
                if (top + r.H < e.ClipRectangle.Top) continue;
                if (top > e.ClipRectangle.Bottom) break;
                DrawRow(g, r, top, i);
            }
            var bar = Bar();
            if (!bar.IsEmpty) TaskInk.Pill(g, bar, bar.Width / 2f, TaskInk.Fade(KitInk.Window, KitInk.Text, dragBar ? 0.4f : 0.22f));
            int at = SelIndex();
            if (at >= 0)
            {
                var r = rows[at];
                Ring(g, new RectangleF(6 * s, r.Y - scroll + 2 * s, Width - 12 * s, r.H - 4 * s), Ink.RadiusControl * s);
            }
        }

        void DrawRow(Graphics g, Rw r, float top, int index)
        {
            float s = Ui;
            var n = r.Note;
            bool sel = string.Equals(n.Path, selPath, StringComparison.OrdinalIgnoreCase);
            float amt = HotAmt(n.Path);
            var pill = new RectangleF(6 * s, top + 2 * s, Width - 12 * s, r.H - 4 * s);
            Color baseBg = sel ? TaskInk.RowSelected : TaskInk.Fade(KitInk.Window, TaskInk.RowHover, amt);
            Color cardTint = NotePalette.ColorOf(n.Color);
            if (cardTint != Color.Empty)
                baseBg = Ink.Mix(baseBg, cardTint, sel ? (Ink.Light ? 0.14f : 0.18f) : (Ink.Light ? 0.08f : 0.12f));
            if (baseBg != KitInk.Window)
                TaskInk.Pill(g, pill, Ink.RadiusControl * s, baseBg);
            if (cardTint != Color.Empty)
                TaskInk.Pill(g, new RectangleF(pill.X + 4 * s, pill.Y + 8 * s, 3 * s, pill.Height - 16 * s), 1.5f * s, cardTint);
            else if (sel)
                TaskInk.Pill(g, new RectangleF(pill.X + 4 * s, pill.Y + 10 * s, 3 * s, pill.Height - 20 * s), 1.5f * s, KitInk.Accent);

            RectangleF pinRect, delRect;
            ActionRects(r, top, out pinRect, out delRect);

            bool shown = amt > 0.02f;
            bool cellHere = hoverRow == index;
            float titleRight = pill.Right - 10 * s;

            if (shown)
            {
                bool hp = cellHere && hoverCell == CPin;
                bool hd = cellHere && hoverCell == CDelete;
                if (hp) TaskInk.Pill(g, pinRect, 8 * s, KitInk.Line);
                if (hd) TaskInk.Pill(g, delRect, 8 * s, KitInk.Line);
                Color fg = TaskInk.Fade(baseBg, KitInk.Sub, amt);
                KitInk.Draw(g, "\uE718", Ink.Icons(12.5f * s), n.Pin ? KitInk.Accent : (hp ? KitInk.Text : fg), pinRect, ContentAlignment.MiddleCenter);
                KitInk.Draw(g, "\uE74D", Ink.Icons(12.5f * s), hd ? Ink.Base.Negative : fg, delRect, ContentAlignment.MiddleCenter);
                titleRight = pinRect.Left - 4 * s;
            }
            else if (n.Pin)
            {
                var pinBadge = new RectangleF(pill.Right - 24 * s, top + 8 * s, 18 * s, 20 * s);
                KitInk.Draw(g, "\uE718", Ink.Icons(11.5f * s), KitInk.Accent, pinBadge, ContentAlignment.MiddleCenter);
                titleRight = pinBadge.Left - 4 * s;
            }

            float left = pill.X + (cardTint != Color.Empty || sel ? 14 * s : 12 * s);
            float contentW = Math.Max(20, pill.Right - 10 * s - left);
            float titleW = Math.Max(20, titleRight - left);

            KitInk.Draw(g, n.Title, Ink.Semi(13.5f * s), KitInk.Text, new RectangleF(left, top + 7 * s, titleW, 22 * s), ContentAlignment.MiddleLeft);

            if (n.Preview.Length > 0)
            {
                KitInk.Draw(g, n.Preview, Ink.Text(12f * s), KitInk.Sub, new RectangleF(left, top + 28 * s, contentW, 18 * s), ContentAlignment.MiddleLeft);
                DrawMetaLine(g, n, left, top + 47 * s, contentW, s);
            }
            else
            {
                DrawMetaLine(g, n, left, top + 30 * s, contentW, s);
            }
        }

        static void DrawMetaLine(Graphics g, NoteInfo n, float x, float y, float w, float s)
        {
            string meta = n.Time.ToString("d MMM", CultureInfo.CurrentCulture);
            if (n.CheckTotal > 0) meta += "  \u00B7  \u2713 " + n.CheckDone + "/" + n.CheckTotal;
            string tags = NotePalette.FormatTags(n.Labels);
            if (tags.Length > 0) meta += "  \u00B7  " + tags;
            KitInk.Draw(g, meta, Ink.Small(11f * s), KitInk.Faint, new RectangleF(x, y, w, 17 * s), ContentAlignment.MiddleLeft);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragBar)
            {
                float max = total - Height, s = Ui, th = Bar().Height;
                float frac = (e.Y - dragGrab - 4 * s) / Math.Max(1, Height - th - 8 * s);
                scrollTarget = scroll = Math.Max(0, Math.Min(max, frac * max));
                Invalidate();
                return;
            }
            int i = RowAt(e.Y);
            int cell = CellAt(i, e.Location);
            string path = i >= 0 ? rows[i].Note.Path : null;
            SetHover(path, i, cell);
            bool onBar = !Bar().IsEmpty && e.X > Width - 16 * Ui;
            Cursor = onBar ? Cursors.Default : (i >= 0 ? Cursors.Hand : Cursors.Default);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            SetHover(null, -1, CNone);
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var bar = Bar();
            if (e.Button == MouseButtons.Left && !bar.IsEmpty && e.X > Width - 16 * Ui)
            {
                if (!bar.Contains(e.Location)) dragGrab = bar.Height / 2;
                else dragGrab = e.Y - bar.Y;
                dragBar = true;
                Capture = true;
                OnMouseMove(e);
                return;
            }
            int i = RowAt(e.Y);
            if (i < 0) return;
            var n = rows[i].Note;
            if (e.Button == MouseButtons.Right)
            {
                SelectRow(i);
                if (ItemMenu != null) ItemMenu(n, PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int cell = CellAt(i, e.Location);
            if (cell == CPin) { if (TogglePinItem != null) TogglePinItem(n); return; }
            if (cell == CDelete) { if (DeleteItem != null) DeleteItem(n); return; }
            SelectRow(i);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragBar) { dragBar = false; Capture = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        void SelectRow(int i)
        {
            if (i < 0 || i >= rows.Count) return;
            var n = rows[i].Note;
            if (string.Equals(n.Path, selPath, StringComparison.OrdinalIgnoreCase)) return;
            selPath = n.Path;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Focus, i);
            AccessibilityNotifyClients(AccessibleEvents.Selection, i);
            if (SelectedChanged != null) SelectedChanged(n);
        }

        protected override bool IsInputKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown:
                case Keys.Home: case Keys.End: case Keys.Delete:
                    return true;
            }
            return base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (rows.Count == 0) return;
            int at = SelIndex();
            int to = -1;
            switch (e.KeyCode)
            {
                case Keys.Down: to = at < 0 ? 0 : Math.Min(rows.Count - 1, at + 1); break;
                case Keys.Up: to = at < 0 ? rows.Count - 1 : Math.Max(0, at - 1); break;
                case Keys.PageDown: to = at < 0 ? 0 : Math.Min(rows.Count - 1, at + 5); break;
                case Keys.PageUp: to = at < 0 ? 0 : Math.Max(0, at - 5); break;
                case Keys.Home: to = 0; break;
                case Keys.End: to = rows.Count - 1; break;
                case Keys.Delete:
                    if (at >= 0 && DeleteItem != null) DeleteItem(rows[at].Note);
                    break;
                case Keys.Apps:
                    if (at >= 0 && ItemMenu != null)
                    {
                        var r = rows[at];
                        ItemMenu(r.Note, PointToScreen(new Point((int)(Width * 0.5f), (int)(r.Y - scroll + r.H))));
                    }
                    break;
                case Keys.F10:
                    if (e.Shift && at >= 0 && ItemMenu != null)
                    {
                        var r = rows[at];
                        ItemMenu(r.Note, PointToScreen(new Point((int)(Width * 0.5f), (int)(r.Y - scroll + r.H))));
                    }
                    break;
            }
            if (to >= 0 && to < rows.Count) { SelectRow(to); Reveal(to); }
            if (e.KeyCode != Keys.Tab) { e.Handled = true; e.SuppressKeyPress = true; }
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

        class Access : ControlAccessibleObject
        {
            readonly NoteListView owner;
            public Access(NoteListView owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return owner.rows.Count; }
            public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.rows.Count ? new Item(owner, i) : null; }
            public override AccessibleObject GetFocused() { int i = owner.SelIndex(); return owner.Focused && i >= 0 ? GetChild(i) : null; }
            public override AccessibleObject GetSelected() { int i = owner.SelIndex(); return i >= 0 ? GetChild(i) : null; }
        }

        class Item : AccessibleObject
        {
            readonly NoteListView owner;
            readonly int index;
            public Item(NoteListView owner, int index) { this.owner = owner; this.index = index; }
            Rw R { get { return owner.rows[index]; } }
            public override string Name { get { return R.Note.Title; } }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get
                {
                    var st = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (index == owner.SelIndex()) st |= AccessibleStates.Selected;
                    return st;
                }
            }
            public override Rectangle Bounds { get { var r = R; return owner.RectangleToScreen(new Rectangle(0, (int)(r.Y - owner.scroll), owner.Width, r.H)); } }
            public override string DefaultAction { get { return Lang.T("Pilih"); } }
            public override void DoDefaultAction() { owner.SelectRow(index); }
        }
    }
}
