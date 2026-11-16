using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Deskmate
{
    // The task list itself: owner-drawn rows, no child controls. Only rows in view are painted,
    // so 500+ tasks cost the same as ten. Hover, completion and scrolling animate on one timer
    sealed class TaskView : KitControl, IWheelTarget
    {
        const int KTask = 0, KDoneHead = 1, KEmpty = 2;
        public const int CNone = 0, CCheck = 1, CStar = 2, CEdit = 3, CDelete = 4, CChevron = 5;

        sealed class Rw
        {
            public int Kind;
            public TodoItem Item;
            public TaskRow Info;
            public int Y, H;
        }

        struct Geo
        {
            public RectangleF Chevron, Circle, Star, Edit, Del;
            public float TitleLeft, Right;
        }

        readonly List<Rw> rows = new List<Rw>();
        int doneCount;
        bool doneOpen, showList, plainEmpty;
        string emptyTitle = "", emptySub = "";
        float total, scroll, scrollTarget;
        string selId, hoverId;
        bool selHead;
        int hoverRow = -1, hoverCell;
        readonly Dictionary<string, float> hot = new Dictionary<string, float>();
        readonly Dictionary<string, float> check = new Dictionary<string, float>();
        readonly Timer anim = new Timer { Interval = 15 };
        DateTime lastStep;
        bool dragBar;
        float dragGrab;
        Font strike, strikeBase;

        public event Action<TodoItem> ToggleDone, ToggleStar, EditItem, DeleteItem, ToggleCollapse;
        public event Action<TodoItem, Point> ItemMenu;
        public event Action ToggleDoneSection;
        // true = up
        public event Action<TodoItem, bool> MoveItem;
        public event Action SelectionChanged;

        public TaskView()
        {
            AccessibleRole = AccessibleRole.List;
            anim.Tick += delegate { Step(); };
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                anim.Dispose();
                if (strike != null) strike.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------- data ----------

        public TodoItem Selected
        {
            get
            {
                if (selHead || selId == null) return null;
                foreach (var r in rows) if (r.Kind == KTask && r.Item.Id == selId) return r.Item;
                return null;
            }
        }

        public void Select(string id)
        {
            selId = id; selHead = false;
            Invalidate();
        }

        public int RowCount { get { return rows.Count; } }

        public void SetData(List<TaskRow> open, List<TodoItem> done, bool doneOpen, bool showList, string emptyTitle, string emptySub)
        {
            float s = Ui;
            this.doneOpen = doneOpen; this.showList = showList; this.emptyTitle = emptyTitle; this.emptySub = emptySub;
            doneCount = done.Count;
            rows.Clear();
            float y = 6 * s;
            foreach (var tr in open)
            {
                int h = (int)(HeightOf(tr.Item) * s);
                rows.Add(new Rw { Kind = KTask, Item = tr.Item, Info = tr, Y = (int)y, H = h });
                y += h;
            }
            plainEmpty = open.Count == 0 && done.Count == 0;
            if (open.Count == 0 && done.Count > 0)
            {
                rows.Add(new Rw { Kind = KEmpty, Y = (int)y, H = (int)(120 * s) });
                y += 120 * s;
            }
            if (done.Count > 0)
            {
                y += 8 * s;
                rows.Add(new Rw { Kind = KDoneHead, Y = (int)y, H = (int)(40 * s) });
                y += 40 * s;
                if (doneOpen)
                    foreach (var t in done)
                    {
                        int h = (int)(HeightOf(t) * s);
                        rows.Add(new Rw { Kind = KTask, Item = t, Y = (int)y, H = h });
                        y += h;
                    }
            }
            total = y + 12 * s;
            if (selId != null && !selHead && Find(selId) < 0) selId = null;
            if (selHead && doneCount == 0) selHead = false;
            ClampScroll();
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Reorder, 0);
        }

        static float HeightOf(TodoItem t) { return !t.Done && TaskInk.FirstLine(t.Notes).Length > 0 ? 62 : 50; }

        int Find(string id)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Kind == KTask && rows[i].Item.Id == id) return i;
            return -1;
        }

        int SelIndex()
        {
            if (selHead) { for (int i = 0; i < rows.Count; i++) if (rows[i].Kind == KDoneHead) return i; return -1; }
            return selId == null ? -1 : Find(selId);
        }

        // ---------- animation ----------

        // The check fill runs from the old state to the new one
        public void StartCheck(TodoItem t, bool wasDone)
        {
            if (Anim.Reduced) { check.Remove(t.Id); Invalidate(); return; }
            check[t.Id] = wasDone ? 1f : 0f;
            Kick();
        }

        float CheckAmt(TodoItem t)
        {
            float v;
            return check.TryGetValue(t.Id, out v) ? v : (t.Done ? 1f : 0f);
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
                float target = (k == hoverId || (k == selId && !selHead)) ? 1f : 0f;
                float v = hot[k];
                v += Math.Sign(target - v) * dt / 130f;
                if ((target > v && hot[k] > target) || (target < v && hot[k] < target) || Math.Abs(target - v) < 0.001f) v = target;
                if (v <= 0f && target == 0f) hot.Remove(k); else { hot[k] = v; if (v != target) busy = true; }
            }
            foreach (var k in new List<string>(check.Keys))
            {
                int i = Find(k);
                float target = i >= 0 ? (rows[i].Item.Done ? 1f : 0f) : 1f;
                float v = check[k];
                v += Math.Sign(target - v) * dt / 240f;
                if (Math.Abs(target - v) < 0.001f || (target > check[k] && v > target) || (target < check[k] && v < target)) { check.Remove(k); }
                else { check[k] = v; busy = true; }
            }
            if (Math.Abs(scrollTarget - scroll) > 0.4f)
            {
                scroll += (scrollTarget - scroll) * (1f - (float)Math.Exp(-dt / 55f));
                if (Math.Abs(scrollTarget - scroll) <= 0.4f) scroll = scrollTarget; else busy = true;
            }
            Invalidate();
            if (!busy) anim.Stop();
        }

        float HotAmt(string id)
        {
            float v;
            if (hot.TryGetValue(id, out v)) return v;
            return (id == hoverId || (id == selId && !selHead)) ? 1f : 0f;
        }

        void SetHover(string id, int row, int cell)
        {
            if (id != hoverId)
            {
                if (hoverId != null && !hot.ContainsKey(hoverId)) hot[hoverId] = 1f;
                if (id != null && !hot.ContainsKey(id)) hot[id] = 0f;
                hoverId = id;
                Kick();
                if (Anim.Reduced) { hot.Clear(); }
            }
            if (row != hoverRow || cell != hoverCell) { hoverRow = row; hoverCell = cell; Invalidate(); }
        }

        // ---------- scrolling ----------

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
            float w = (dragBar || (Hovered && PointToClient(Cursor.Position).X > Width - 18 * s)) ? 7 * s : 4 * s;
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

        // ---------- geometry ----------

        Geo Geometry(Rw r, float top)
        {
            float s = Ui;
            var g = new Geo();
            float indent = r.Info != null ? r.Info.Depth * 32 * s : 0;
            float first = top + (r.Kind == KTask && !r.Item.Done && TaskInk.FirstLine(r.Item.Notes).Length > 0 ? 22 * s : r.H / 2f);
            float d = 22 * s;
            g.Chevron = new RectangleF(8 * s + indent, first - 11 * s, 20 * s, 22 * s);
            g.Circle = new RectangleF(32 * s + indent, first - d / 2, d, d);
            g.TitleLeft = g.Circle.Right + 12 * s;
            float right = Width - 16 * s;
            float slot = 28 * s;
            g.Del = new RectangleF(right - slot, first - 14 * s, slot, 28 * s);
            g.Edit = new RectangleF(g.Del.Left - slot, g.Del.Top, slot, 28 * s);
            g.Star = new RectangleF(g.Edit.Left - slot, g.Del.Top, slot, 28 * s);
            g.Right = g.Star.Left - 8 * s;
            return g;
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

        int CellAt(int index, Point p)
        {
            if (index < 0 || rows[index].Kind != KTask) return CNone;
            var r = rows[index];
            var g = Geometry(r, r.Y - scroll);
            if (r.Info != null && r.Info.HasChildren && g.Chevron.Contains(p)) return CChevron;
            var hit = RectangleF.Inflate(g.Circle, 6 * Ui, 6 * Ui);
            if (hit.Contains(p)) return CCheck;
            float amt = HotAmt(r.Item.Id);
            if (g.Del.Contains(p)) return CDelete;
            if (g.Edit.Contains(p)) return CEdit;
            if (g.Star.Contains(p)) return CStar;
            return CNone;
        }

        // ---------- painting ----------

        Font Strike(Font f)
        {
            if (strike == null || !ReferenceEquals(strikeBase, f))
            {
                if (strike != null) strike.Dispose();
                strike = new Font(f, FontStyle.Strikeout);
                strikeBase = f;
            }
            return strike;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            KitInk.Smooth(g);
            g.Clear(KitInk.Window);
            float s = Ui;
            using (var cardPath = Ink.Round(new RectangleF(0, 0, Width, Height), Ink.RadiusControl * s))
            {
                using (var b = new SolidBrush(TaskInk.Card)) g.FillPath(b, cardPath);
                g.SetClip(cardPath);
                if (plainEmpty)
                {
                    DrawEmpty(g, new RectangleF(0, 0, Width, Height), true);
                    g.ResetClip();
                    return;
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    float top = r.Y - scroll;
                    if (top + r.H < e.ClipRectangle.Top) continue;
                    if (top > e.ClipRectangle.Bottom) break;
                    if (r.Kind == KEmpty) DrawEmpty(g, new RectangleF(0, top, Width, r.H), false);
                    else if (r.Kind == KDoneHead) DrawDoneHead(g, r, top, i);
                    else DrawTask(g, r, top, i);
                }
                var bar = Bar();
                if (!bar.IsEmpty) TaskInk.Pill(g, bar, bar.Width / 2f, TaskInk.Fade(TaskInk.Card, KitInk.Text, dragBar ? 0.4f : 0.22f));
                int at = SelIndex();
                if (at >= 0)
                {
                    var r = rows[at];
                    Ring(g, new RectangleF(6 * s, r.Y - scroll + 2 * s, Width - 12 * s, r.H - 4 * s), 10 * s);
                }
                g.ResetClip();
            }
        }

        void DrawEmpty(Graphics g, RectangleF area, bool big)
        {
            float s = Ui;
            float box = (big ? 40 : 32) * s;
            float cx = area.X + area.Width / 2f;
            float cy = area.Y + (area.Height - (big ? 54 : 30) * s) / 2f;
            var tile = new RectangleF(cx - box / 2, cy - box / 2, box, box);
            TaskInk.Pill(g, tile, box * 0.3f, KitInk.Hover);
            float d = box * 0.46f;
            var ring = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            using (var pen = new Pen(KitInk.Faint, Math.Max(1.6f, box * 0.045f))) g.DrawEllipse(pen, ring);
            TaskInk.CheckMark(g, ring, KitInk.Faint, 1f, Math.Max(1.6f, box * 0.045f));
            float ty = tile.Bottom + 8 * s;
            KitInk.Draw(g, emptyTitle, Ink.Semi((big ? 15f : 13.5f) * s), KitInk.Text, new RectangleF(area.X + 16 * s, ty, area.Width - 32 * s, 24 * s), ContentAlignment.MiddleCenter);
            if (big && emptySub.Length > 0)
                KitInk.Draw(g, emptySub, Ink.Text(12.5f * s), KitInk.Sub, new RectangleF(area.X + 16 * s, ty + 24 * s, area.Width - 32 * s, 22 * s), ContentAlignment.MiddleCenter);
        }

        void DrawDoneHead(Graphics g, Rw r, float top, int index)
        {
            float s = Ui;
            bool sel = selHead, hov = hoverRow == index;
            var pill = new RectangleF(6 * s, top + 2 * s, Width - 12 * s, r.H - 4 * s);
            Color bg = sel ? TaskInk.RowSelected : hov ? TaskInk.RowHover : TaskInk.Card;
            if (bg != TaskInk.Card) TaskInk.Pill(g, pill, 10 * s, bg);
            KitInk.Draw(g, doneOpen ? "\uE70D" : "\uE76C", Ink.Icons(12f * s), KitInk.Sub, new RectangleF(10 * s, top, 24 * s, r.H), ContentAlignment.MiddleCenter);
            KitInk.Draw(g, Lang.T("Selesai") + " (" + doneCount.ToString(CultureInfo.InvariantCulture) + ")", Ink.Semi(13f * s), KitInk.Sub, new RectangleF(40 * s, top, Width - 60 * s, r.H), ContentAlignment.MiddleLeft);
        }

        void DrawTask(Graphics g, Rw r, float top, int index)
        {
            float s = Ui;
            var t = r.Item;
            bool sel = !selHead && t.Id == selId;
            float amt = HotAmt(t.Id);
            var pill = new RectangleF(6 * s, top + 2 * s, Width - 12 * s, r.H - 4 * s);
            Color bg = sel ? TaskInk.RowSelected : TaskInk.Fade(TaskInk.Card, TaskInk.RowHover, amt);
            if (bg != TaskInk.Card) TaskInk.Pill(g, pill, 10 * s, bg);
            var geo = Geometry(r, top);
            float first = geo.Circle.Y + geo.Circle.Height / 2f;
            bool cellHere = hoverRow == index;

            // Subtask chevron
            if (r.Info != null && r.Info.HasChildren)
            {
                bool open = !collapsedLookup(t.Id);
                KitInk.Draw(g, open ? "\uE70D" : "\uE76C", Ink.Icons(11f * s), cellHere && hoverCell == CChevron ? KitInk.Text : KitInk.Sub, geo.Chevron, ContentAlignment.MiddleCenter);
            }

            // Circle with a fill that grows from the middle
            float p = Ease.OutCubic(CheckAmt(t));
            bool onCircle = cellHere && hoverCell == CCheck;
            var circle = geo.Circle;
            Color ring = TaskInk.Fade(onCircle ? KitInk.Sub : KitInk.Faint, KitInk.Accent, p);
            float stroke = 1.8f * s;
            var rc = RectangleF.Inflate(circle, -stroke / 2, -stroke / 2);
            if (p < 1f) using (var pen = new Pen(ring, stroke)) g.DrawEllipse(pen, rc);
            if (p > 0f)
            {
                float k = circle.Width / 2f * p;
                using (var b = new SolidBrush(KitInk.Accent)) g.FillEllipse(b, circle.X + circle.Width / 2f - k, circle.Y + circle.Height / 2f - k, k * 2, k * 2);
                TaskInk.CheckMark(g, circle, KitInk.OnAccent, Math.Max(0f, (p - 0.35f) / 0.65f), 2f * s);
            }
            else if (onCircle) TaskInk.CheckMark(g, circle, KitInk.Faint, 1f, 1.8f * s);

            // Right edge: star, edit, delete
            bool shown = amt > 0.02f;
            Color fg = TaskInk.Fade(bg, KitInk.Sub, amt);
            if (shown || t.Star)
            {
                bool hs = cellHere && hoverCell == CStar;
                if (hs) TaskInk.Pill(g, RectangleF.Inflate(geo.Star, -1 * s, -1 * s), 9 * s, KitInk.Line);
                Color sc = t.Star ? KitInk.Accent : (hs ? KitInk.Text : fg);
                KitInk.Draw(g, t.Star ? "\uE735" : "\uE734", Ink.Icons(15f * s), sc, geo.Star, ContentAlignment.MiddleCenter);
            }
            if (shown)
            {
                bool he = cellHere && hoverCell == CEdit, hd = cellHere && hoverCell == CDelete;
                if (he) TaskInk.Pill(g, RectangleF.Inflate(geo.Edit, -1 * s, -1 * s), 9 * s, KitInk.Line);
                if (hd) TaskInk.Pill(g, RectangleF.Inflate(geo.Del, -1 * s, -1 * s), 9 * s, KitInk.Line);
                KitInk.Draw(g, "\uE70F", Ink.Icons(15f * s), he ? KitInk.Text : fg, geo.Edit, ContentAlignment.MiddleCenter);
                KitInk.Draw(g, "\uE74D", Ink.Icons(15f * s), hd ? KitInk.Text : fg, geo.Del, ContentAlignment.MiddleCenter);
            }

            // Due chip, then the list label when several lists share the view
            float right = geo.Right;
            var fChip = Ink.Small(11.5f * s);
            if (t.Due.HasValue)
            {
                bool late = TaskInk.Overdue(t);
                string ds = TaskInk.DueText(t.Due.Value);
                float w = TextRenderer.MeasureText(ds, fChip, new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 18 * s;
                var chip = new RectangleF(right - w, first - 11 * s, w, 22 * s);
                Color fill, ink;
                if (t.Done) { fill = Ink.Mix(bg, KitInk.Text, 0.05f); ink = KitInk.Faint; }
                else if (late) { fill = Ink.Mix(bg, Ink.Base.Negative, 0.10f); ink = Ink.Base.Negative; }
                else if (t.Due.Value.Date == DateTime.Today) { fill = Ink.Mix(bg, KitInk.Text, 0.10f); ink = KitInk.Text; }
                else { fill = Ink.Mix(bg, KitInk.Text, 0.07f); ink = KitInk.Sub; }
                TaskInk.Pill(g, chip, 11 * s, fill);
                KitInk.Draw(g, ds, fChip, ink, chip, ContentAlignment.MiddleCenter);
                right = chip.Left - 8 * s;
            }
            if (showList)
            {
                string ln = TaskLists.NameOf(t);
                if (TaskLists.IsDefault(ln)) ln = Lang.T(TaskLists.Default);
                float w = Math.Min(120 * s, TextRenderer.MeasureText(ln, fChip, new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 6 * s);
                if (right - w > geo.TitleLeft + 80 * s)
                {
                    KitInk.Draw(g, ln, fChip, t.Done ? KitInk.Faint : KitInk.Sub, new RectangleF(right - w, first - 11 * s, w, 22 * s), ContentAlignment.MiddleRight);
                    right -= w + 10 * s;
                }
            }

            // Title and the first line of the details
            var fTitle = Ink.Text(14.5f * s);
            float tw = Math.Max(20, right - geo.TitleLeft);
            KitInk.Draw(g, t.Title, t.Done ? Strike(fTitle) : fTitle, t.Done ? KitInk.Faint : KitInk.Text, new RectangleF(geo.TitleLeft, first - 13 * s, tw, 26 * s), ContentAlignment.MiddleLeft);
            string sub = t.Done ? "" : TaskInk.FirstLine(t.Notes);
            if (sub.Length > 0)
                KitInk.Draw(g, sub, Ink.Text(12.5f * s), KitInk.Sub, new RectangleF(geo.TitleLeft, first + 8 * s, Math.Max(20, Width - geo.TitleLeft - 24 * s), 22 * s), ContentAlignment.MiddleLeft);
        }

        // Window owns the collapsed set; the view asks it
        public Func<string, bool> IsCollapsed;
        bool collapsedLookup(string id) { return IsCollapsed != null && IsCollapsed(id); }

        // ---------- mouse ----------

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
            string id = i >= 0 && rows[i].Kind == KTask ? rows[i].Item.Id : null;
            SetHover(id, i, cell);
            bool onBar = !Bar().IsEmpty && e.X > Width - 18 * Ui;
            Cursor = onBar ? Cursors.Default : (cell != CNone || (i >= 0 && rows[i].Kind == KDoneHead)) ? Cursors.Hand : Cursors.Default;
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
            if (e.Button == MouseButtons.Left && !bar.IsEmpty && e.X > Width - 18 * Ui)
            {
                if (!bar.Contains(e.Location)) { dragGrab = bar.Height / 2; }
                else dragGrab = e.Y - bar.Y;
                dragBar = true;
                Capture = true;
                OnMouseMove(e);
                return;
            }
            int i = RowAt(e.Y);
            if (i < 0) return;
            var r = rows[i];
            if (r.Kind == KDoneHead)
            {
                SelectRow(i);
                if (e.Button == MouseButtons.Left && ToggleDoneSection != null) ToggleDoneSection();
                return;
            }
            if (r.Kind != KTask) return;
            var t = r.Item;
            SelectRow(i);
            if (e.Button == MouseButtons.Right)
            {
                if (ItemMenu != null) ItemMenu(t, PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int cell = CellAt(i, e.Location);
            if (e.Clicks > 1 && cell == CNone) { if (EditItem != null) EditItem(t); return; }
            if (cell == CCheck && ToggleDone != null) ToggleDone(t);
            else if (cell == CStar && ToggleStar != null) ToggleStar(t);
            else if (cell == CEdit && EditItem != null) EditItem(t);
            else if (cell == CDelete && DeleteItem != null) DeleteItem(t);
            else if (cell == CChevron && ToggleCollapse != null) ToggleCollapse(t);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragBar) { dragBar = false; Capture = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        void SelectRow(int i)
        {
            var r = rows[i];
            bool head = r.Kind == KDoneHead;
            string id = r.Kind == KTask ? r.Item.Id : null;
            if (head == selHead && id == selId) return;
            if (selId != null && !hot.ContainsKey(selId)) hot[selId] = 1f;
            selHead = head; selId = id;
            Kick();
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Focus, i);
            AccessibilityNotifyClients(AccessibleEvents.Selection, i);
            if (SelectionChanged != null) SelectionChanged();
        }

        // ---------- keyboard ----------

        protected override bool IsInputKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.PageUp: case Keys.PageDown:
                case Keys.Home: case Keys.End: case Keys.Enter: case Keys.Delete: case Keys.Space: case Keys.F2:
                    return true;
            }
            return base.IsInputKey(key);
        }

        // From the add box: first row gets the selection
        public void FocusFirst()
        {
            Focus();
            if (SelIndex() < 0) for (int i = 0; i < rows.Count; i++) if (rows[i].Kind != KEmpty) { SelectRow(i); Reveal(i); break; }
        }

        int Step(int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < rows.Count; i += dir) if (rows[i].Kind != KEmpty) return i;
            return from;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int at = SelIndex();
            var cur = at >= 0 ? rows[at] : null;
            var t = cur != null && cur.Kind == KTask ? cur.Item : null;
            int to = -2;
            if (e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                if (t != null && MoveItem != null) MoveItem(t, e.KeyCode == Keys.Up);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            switch (e.KeyCode)
            {
                case Keys.Down: to = at < 0 ? Step(-1, 1) : Step(at, 1); break;
                case Keys.Up: to = at < 0 ? Step(rows.Count, -1) : Step(at, -1); break;
                case Keys.PageDown: to = at < 0 ? 0 : Math.Min(rows.Count - 1, at + 6); break;
                case Keys.PageUp: to = at < 0 ? 0 : Math.Max(0, at - 6); break;
                case Keys.Home: to = Step(-1, 1); break;
                case Keys.End: to = Step(rows.Count, -1); break;
                case Keys.Left:
                    if (t != null && cur.Info != null && cur.Info.HasChildren && !collapsedLookup(t.Id)) { if (ToggleCollapse != null) ToggleCollapse(t); }
                    else if (t != null && cur.Info != null && cur.Info.Depth > 0) for (int i = at - 1; i >= 0; i--) if (rows[i].Info != null && rows[i].Info.Depth == 0) { to = i; break; }
                    break;
                case Keys.Right:
                    if (t != null && cur.Info != null && cur.Info.HasChildren && collapsedLookup(t.Id) && ToggleCollapse != null) ToggleCollapse(t);
                    break;
                case Keys.Space:
                    if (t != null && ToggleDone != null) ToggleDone(t);
                    else if (cur != null && cur.Kind == KDoneHead && ToggleDoneSection != null) ToggleDoneSection();
                    break;
                case Keys.Enter:
                case Keys.F2:
                    if (t != null && EditItem != null) EditItem(t);
                    else if (cur != null && cur.Kind == KDoneHead && ToggleDoneSection != null) ToggleDoneSection();
                    break;
                case Keys.Delete:
                    if (t != null && DeleteItem != null) DeleteItem(t);
                    break;
                case Keys.Apps:
                    if (t != null && ItemMenu != null) ItemMenu(t, PointToScreen(new Point((int)(60 * Ui), (int)(cur.Y - scroll + cur.H))));
                    break;
                case Keys.F10:
                    if (e.Shift && t != null && ItemMenu != null) ItemMenu(t, PointToScreen(new Point((int)(60 * Ui), (int)(cur.Y - scroll + cur.H))));
                    break;
            }
            if (to >= 0 && to < rows.Count) { SelectRow(to); Reveal(to); }
            if (e.KeyCode != Keys.Tab) { e.Handled = true; e.SuppressKeyPress = true; }
        }

        // ---------- accessibility ----------

        protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

        class Access : ControlAccessibleObject
        {
            readonly TaskView owner;
            public Access(TaskView owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return owner.rows.Count; }
            public override AccessibleObject GetChild(int i) { return i >= 0 && i < owner.rows.Count ? new Item(owner, i) : null; }
            public override AccessibleObject GetFocused() { int i = owner.SelIndex(); return owner.Focused && i >= 0 ? GetChild(i) : null; }
            public override AccessibleObject GetSelected() { int i = owner.SelIndex(); return i >= 0 ? GetChild(i) : null; }
        }

        class Item : AccessibleObject
        {
            readonly TaskView owner;
            readonly int index;
            public Item(TaskView owner, int index) { this.owner = owner; this.index = index; }
            Rw R { get { return owner.rows[index]; } }
            public override string Name
            {
                get
                {
                    var r = R;
                    if (r.Kind == KDoneHead) return Lang.T("Selesai") + " (" + owner.doneCount + ")";
                    if (r.Kind == KEmpty) return owner.emptyTitle;
                    string n = r.Item.Title;
                    if (r.Item.Due.HasValue) n += ", " + TaskInk.DueText(r.Item.Due.Value);
                    if (r.Item.Star) n += ", " + Lang.T("berbintang");
                    return n;
                }
            }
            public override AccessibleRole Role { get { return R.Kind == KTask ? AccessibleRole.CheckButton : R.Kind == KDoneHead ? AccessibleRole.ButtonDropDown : AccessibleRole.StaticText; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override AccessibleStates State
            {
                get
                {
                    var r = R;
                    var st = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (index == owner.SelIndex()) st |= AccessibleStates.Selected;
                    if (r.Kind == KTask && r.Item.Done) st |= AccessibleStates.Checked;
                    if (r.Kind == KDoneHead) st |= owner.doneOpen ? AccessibleStates.Expanded : AccessibleStates.Collapsed;
                    return st;
                }
            }
            public override Rectangle Bounds { get { var r = R; return owner.RectangleToScreen(new Rectangle(0, (int)(r.Y - owner.scroll), owner.Width, r.H)); } }
            public override string DefaultAction { get { return R.Kind == KTask ? Lang.T("Tandai selesai") : Lang.T("Buka"); } }
            public override void DoDefaultAction()
            {
                var r = R;
                if (r.Kind == KTask && owner.ToggleDone != null) owner.ToggleDone(r.Item);
                else if (r.Kind == KDoneHead && owner.ToggleDoneSection != null) owner.ToggleDoneSection();
            }
        }
    }
}
