using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace Deskmate
{
    sealed class TaskBoardLane : KitControl, IWheelTarget
    {
        public readonly string ListName;
        readonly LinkLabel add = new LinkLabel();
        readonly LinkLabel completed = new LinkLabel();
        readonly TaskIconButton options = new TaskIconButton { Glyph = '\uE712' };
        bool showCompleted;
        int completedCount;
        List<TodoItem> cards = new List<TodoItem>();
        int scroll, selected = -1, hover = -1, insertion = -1;
        bool dragOver;
        const string ListDragFormat = "Deskmate.TaskBoard.List";
        readonly Timer landingTimer = new Timer { Interval = 16 };
        readonly Stopwatch landingAge = new Stopwatch();
        string draggingId, landedId;
        bool pressedList, draggingList, landedList;
        int listEdge;
        TodoItem pressed;
        public bool CanMoveList;
        Rectangle dragThreshold;
        // Dashed outline of a lifted card; the shared Kit pen must keep solid strokes
        System.Drawing.Pen liftPen;

        public event Action<TodoItem> Edit;
        public event Action<TodoItem, Point> Menu;
        public event Action<string, string, string, bool> CardMoved;
        public event Action<string, string, bool> ListMoved;
        public event Action<TodoItem> ToggleDone;
        public event Action<string> AddTask;
        public event Action<string, Point> ListMenu;
        public event Action LayoutChanged;
        public event Action<int> DragAtScreenX;
        public event Func<string, TodoItem> Resolve;

        int Header { get { return (int)(72 * Ui); } }
        int Pitch { get { return (int)(44 * Ui); } }
        int Footer { get { return completedCount > 0 ? (int)(33 * Ui) : 0; } }
        int BodyBottom { get { return Height - Footer - (int)(6 * Ui); } }
        int MaxScroll { get { return Math.Max(0, Header + cards.Count * Pitch - BodyBottom); } }
        public int PreferredLaneHeight { get { return Header + Math.Max(cards.Count * Pitch, (int)(cards.Count == 0 && completedCount == 0 ? 108 * Ui : 6 * Ui)) + Footer + (int)(8 * Ui); } }

        public TaskBoardLane(string listName)
        {
            ListName = listName;
            AccessibleName = TaskLists.IsDefault(listName) ? Lang.T(listName) : listName;
            AccessibleRole = AccessibleRole.List;
            AllowDrop = true;
            landingTimer.Tick += delegate
            {
                if (landingAge.ElapsedMilliseconds >= 260) landingTimer.Stop();
                Invalidate();
            };
            add.Text = "+  " + Lang.T("Tambah tugas…");
            add.LinkBehavior = LinkBehavior.HoverUnderline;
            add.TextAlign = ContentAlignment.MiddleLeft;
            add.LinkClicked += delegate { if (AddTask != null) AddTask(ListName); };
            completed.LinkBehavior = LinkBehavior.HoverUnderline;
            completed.TextAlign = ContentAlignment.MiddleLeft;
            completed.LinkClicked += delegate
            {
                showCompleted = !showCompleted;
                if (LayoutChanged != null) LayoutChanged();
            };
            options.AccessibleName = Lang.T("Opsi daftar");
            options.Back = delegate { return TaskInk.Wash(Surface, 0.07f); };
            options.Click += delegate { if (ListMenu != null) ListMenu(ListName, PointToScreen(new Point(options.Left, options.Bottom))); };
            Controls.AddRange(new Control[] { add, options, completed });
        }

        public void ShowLanding(string id)
        {
            landedId = id;
            landedList = id == null;
            landingAge.Restart();
            if (!Anim.Reduced) landingTimer.Start();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { landingTimer.Dispose(); if (liftPen != null) liftPen.Dispose(); }
            base.Dispose(disposing);
        }

        // Calm tinted band behind the lane title
        int Band { get { return (int)(44 * Ui); } }

        Color Surface { get { return dragOver ? Ink.Mix(KitInk.Group, KitInk.Accent, 0.12f) : KitInk.Group; } }

        public void SetCards(List<TodoItem> open, List<TodoItem> done, string keepId)
        {
            if (keepId == null && selected >= 0 && selected < cards.Count) keepId = cards[selected].Id;
            cards = open;
            completedCount = done.Count;
            if (showCompleted) cards.AddRange(done);
            completed.Text = (showCompleted ? "▾  " : "▸  ") + Lang.T("Selesai") + " (" + completedCount + ")";
            completed.Visible = completedCount > 0;
            selected = cards.FindIndex(t => t.Id == keepId);
            hover = -1;
            LayoutActions();
            SetScroll(scroll);
            if (Focused) Reveal();
            Invalidate(true);
            AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
        }

        void LayoutActions()
        {
            float s = Ui;
            int pad = (int)(14 * s);
            add.SetBounds(pad, Band + (int)(Ink.Sp4 * s), Math.Max(1, Width - 2 * pad), (int)(24 * s));
            options.SetBounds(Width - (int)(38 * s), (int)(8 * s), (int)(28 * s), (int)(28 * s));
            completed.SetBounds(pad, BodyBottom + (int)(3 * s), Math.Max(1, Width - 2 * pad), Math.Max(1, Footer));
            add.Font = completed.Font = Ink.Text(13 * s);
            add.BackColor = completed.BackColor = Surface;
            add.LinkColor = add.ActiveLinkColor = KitInk.Accent;
            completed.LinkColor = completed.ActiveLinkColor = KitInk.Text;
        }

        public bool FocusCard(string id)
        {
            int index = cards.FindIndex(t => t.Id == id);
            if (index < 0) return false;
            selected = index;
            Focus();
            Reveal();
            return true;
        }

        RectangleF Card(int i)
        {
            float s = Ui;
            return new RectangleF(10 * s, Header + i * Pitch - scroll, Width - 20 * s, Pitch - 8 * s);
        }

        int Hit(Point p)
        {
            if (p.Y < Header || p.Y >= BodyBottom) return -1;
            int i = (p.Y - Header + scroll) / Pitch;
            return i < cards.Count && Card(i).Contains(p) ? i : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 70 * Ui || Height <= Header + 8 * Ui) return;
            var g = e.Graphics;
            float s = Ui;
            g.Clear(KitInk.Window);
            KitInk.Smooth(g);
            var lane = new RectangleF(0, 0, Width - 1, Height - 1);
            float laneR = Ink.RadiusCard * s;
            Fill(g, lane, laneR, Surface);
            // Header band: the lane's own surface with a breath of accent, clipped to the lane's corners
            var outer = g.Save();
            g.SetClip(KitInk.Shape(lane, laneR));
            g.FillRectangle(KitInk.BrushOf(TaskInk.Wash(Surface, 0.07f)), 0, 0, Width, Band);
            g.Restore(outer);
            float settle = !Anim.Reduced && landingTimer.Enabled ? 1f - Math.Min(1f, landingAge.ElapsedMilliseconds / 260f) : 0f;
            var header = new RectangleF(8 * s, 6 * s, Width - 49 * s, 32 * s);
            if (pressedList || draggingList || (landedList && settle > 0))
                Fill(g, header, TaskInk.RowRadius(8 * s, s), Ink.Mix(Surface, KitInk.Accent, pressedList || draggingList ? 0.24f : 0.26f * settle * settle));
            if (CanMoveList)
                KitInk.Draw(g, "\uE76F", Ink.Icons(12 * s), KitInk.Faint,
                    new RectangleF(12 * s, 4 * s, 15 * s, 36 * s), ContentAlignment.MiddleCenter);
            int titleX = CanMoveList ? 32 : 14;
            KitInk.Draw(g, AccessibleName, Ink.Semi(14.5f * s), draggingList ? KitInk.Sub : KitInk.Text,
                new RectangleF(titleX * s, 4 * s, Width - (42 + titleX) * s, 36 * s), ContentAlignment.MiddleLeft);

            var state = g.Save();
            g.SetClip(RectangleF.FromLTRB(0, Header, Width, Math.Max(Header, BodyBottom)));
            if (cards.Count == 0 && completedCount == 0)
                KitInk.Draw(g, Lang.T("Belum ada tugas"), Ink.Text(13 * s), KitInk.Faint,
                    new RectangleF(14 * s, Header, Width - 28 * s, 84 * s), ContentAlignment.MiddleCenter);
            float cardR = TaskInk.RowRadius(10 * s, s);
            for (int i = scroll / Pitch; i < cards.Count; i++)
            {
                var r = Card(i);
                if (r.Top >= BodyBottom) break;
                var t = cards[i];
                bool held = pressed != null && pressed.Id == t.Id;
                bool lifted = draggingId == t.Id;
                if (held || lifted || (landedId == t.Id && settle > 0))
                {
                    if (held && !Anim.Reduced) r.Inflate(-s, -s);
                    float accent = lifted ? 0.11f : held ? 0.24f : 0.30f * settle * settle;
                    Fill(g, r, cardR, Ink.Mix(Surface, KitInk.Accent, accent));
                    if (lifted)
                    {
                        if (liftPen == null) liftPen = new System.Drawing.Pen(KitInk.Accent, s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                        liftPen.Color = Ink.Mix(KitInk.Sub, KitInk.Accent, 0.42f);
                        liftPen.Width = s;
                        g.DrawPath(liftPen, KitInk.Shape(r, cardR));
                    }
                }
                else if (i == hover || (Focused && i == selected)) Fill(g, r, cardR, Ink.Mix(Surface, KitInk.Text, 0.07f));
                var check = new RectangleF(r.Left + 5 * s, r.Top + 10 * s, 14 * s, 14 * s);
                if (t.Done)
                {
                    g.FillEllipse(KitInk.BrushOf(KitInk.Accent), check);
                    TaskInk.CheckMark(g, check, KitInk.OnAccent, 1f, 1.5f * s);
                }
                else g.DrawEllipse(KitInk.PenOf(KitInk.Sub, 1.2f * s), check);
                float left = r.Left + 29 * s + (string.IsNullOrEmpty(t.ParentId) ? 0 : 10 * s);
                float width = r.Right - left - (t.Star ? 21 : 4) * s;
                // Same due wording as the list view; overdue in the calm coral
                bool late = TaskInk.Overdue(t);
                string detail = t.Due.HasValue ? TaskInk.DueText(t.Due.Value) : TaskInk.FirstLine(t.Notes);
                float titleHeight = detail.Length == 0 ? r.Height : 21 * s;
                KitInk.Draw(g, t.Title, Ink.Text(13.5f * s), t.Done || lifted ? KitInk.Sub : KitInk.Text,
                    new RectangleF(left, r.Top, width, titleHeight), ContentAlignment.MiddleLeft);
                if (detail.Length > 0)
                    KitInk.Draw(g, detail, Ink.Small(11.5f * s), late ? Ink.Base.Negative : KitInk.Sub,
                        new RectangleF(left, r.Top + 19 * s, width, 16 * s), ContentAlignment.MiddleLeft);
                if (t.Star) KitInk.Draw(g, "\uE735", Ink.Icons(12 * s), KitInk.Accent,
                    new RectangleF(r.Right - 18 * s, r.Top, 16 * s, r.Height), ContentAlignment.MiddleCenter);
                if (i == selected) Ring(g, RectangleF.Inflate(r, -2 * s, -2 * s), Math.Max(2 * s, cardR - 2 * s));
            }
            if (insertion >= 0)
            {
                float y = Math.Max(Header + 1, Card(insertion).Top - 4 * s);
                y = Math.Min(BodyBottom - 2 * s, y);
                g.DrawLine(KitInk.PenOf(KitInk.Accent, 2 * s), 13 * s, y, Width - 13 * s, y);
                g.FillEllipse(KitInk.BrushOf(KitInk.Accent), 10 * s, y - 3 * s, 6 * s, 6 * s);
            }
            if (MaxScroll > 0 && BodyBottom > Header + 8 * s)
            {
                float track = BodyBottom - Header - 4 * s;
                float thumb = Math.Min(track, Math.Max(18 * s, track * track / (cards.Count * Pitch)));
                float y = Header + (track - thumb) * scroll / MaxScroll;
                Fill(g, new RectangleF(Width - 5 * s, y, 3 * s, thumb), 1.5f * s, KitInk.Faint);
            }
            g.Restore(state);
            if (listEdge != 0)
            {
                float x = listEdge < 0 ? 2 * s : Width - 3 * s;
                g.DrawLine(KitInk.PenOf(KitInk.Accent, 3 * s), x, 6 * s, x, Height - 6 * s);
            }
        }

        void SetScroll(int value)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, value));
            Invalidate();
        }

        void Reveal()
        {
            if (selected < 0) return;
            var r = Card(selected);
            if (r.Top < Header) SetScroll(selected * Pitch);
            else if (r.Bottom > BodyBottom) SetScroll(scroll + (int)(r.Bottom - BodyBottom));
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (add != null) LayoutActions();
            SetScroll(scroll);
        }

        public void Wheel(int delta) { SetScroll(scroll - delta * Pitch / 120); }
        protected override void OnMouseWheel(MouseEventArgs e) { Wheel(e.Delta); }

        bool OnListTitle(Point p)
        {
            return CanMoveList && p.Y >= 5 * Ui && p.Y < 37 * Ui && p.X < Width - 40 * Ui;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = Hit(e.Location);
            if (i < 0 && !(e.Button == MouseButtons.Left && OnListTitle(e.Location))) return;
            if (i >= 0) selected = i;
            Focus();
            if (i >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, i + 1);
            Invalidate();
            if (e.Button == MouseButtons.Right && Menu != null) Menu(cards[i], PointToScreen(e.Location));
            if (e.Button != MouseButtons.Left) return;
            if (i >= 0 && e.X < Card(i).Left + 25 * Ui && ToggleDone != null)
            {
                ToggleDone(cards[i]);
                return;
            }
            pressedList = i < 0;
            pressed = i < 0 ? null : cards[i];
            var size = SystemInformation.DragSize;
            dragThreshold = new Rectangle(e.X - size.Width / 2, e.Y - size.Height / 2, size.Width, size.Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = Hit(e.Location);
            if (hover != i) { hover = i; Invalidate(); }
            Cursor = i >= 0 || OnListTitle(e.Location) ? Cursors.Hand : Cursors.Default;
            if ((!pressedList && pressed == null) || e.Button != MouseButtons.Left || dragThreshold.Contains(e.Location)) return;

            draggingList = pressedList;
            draggingId = pressed == null ? null : pressed.Id;
            string title = draggingList ? AccessibleName : pressed.Title;
            var data = new DataObject();
            data.SetData(draggingList ? ListDragFormat : typeof(TaskBoardLane).FullName, false, draggingList ? ListName : draggingId);
            pressed = null;
            pressedList = false;
            Invalidate();
            Update();
            try
            {
                using (var preview = new TaskDragPreview(title, draggingList ? '\uE8A5' : '\uE762', Ui))
                {
                    preview.Show(FindForm());
                    DoDragDrop(data, DragDropEffects.Move);
                }
            }
            finally
            {
                draggingId = null;
                draggingList = false;
                hover = -1;
                Invalidate();
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) ClearPress();
            base.OnMouseCaptureChanged(e);
        }

        void ClearPress()
        {
            if (pressed == null && !pressedList) return;
            pressed = null;
            pressedList = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { ClearPress(); base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left && Edit != null) Edit(cards[i]);
        }

        TodoItem DropSource(DragEventArgs e)
        {
            string id = e.Data == null ? null : e.Data.GetData(typeof(TaskBoardLane).FullName, false) as string;
            return id == null || Resolve == null ? null : Resolve(id);
        }

        TodoItem DropAnchor(TodoItem source, Point p, out bool after)
        {
            int i = Math.Min(cards.Count - 1, Math.Max(0, (p.Y - Header + scroll) / Pitch));
            after = i >= 0 && p.Y >= Card(i).Top + Card(i).Height / 2;
            if (i < 0 || p.Y >= BodyBottom) return null;
            var target = cards[i];
            return source.Id != target.Id && !target.Done && (TaskLists.Same(TaskLists.NameOf(source), ListName)
                ? !source.Done && TaskTree.BoardSiblings(source, target)
                : string.IsNullOrEmpty(target.ParentId)) ? target : null;
        }

        string DraggedList(DragEventArgs e)
        {
            return e.Data == null ? null : e.Data.GetData(ListDragFormat, false) as string;
        }

        protected override void OnDragEnter(DragEventArgs e) { OnDragOver(e); }

        protected override void OnDragOver(DragEventArgs e)
        {
            var p = PointToClient(new Point(e.X, e.Y));
            string list = DraggedList(e);
            if (list != null)
            {
                listEdge = CanMoveList && !TaskLists.Same(list, ListName) ? (p.X < Width / 2 ? -1 : 1) : 0;
                e.Effect = listEdge == 0 ? DragDropEffects.None : DragDropEffects.Move;
                if (DragAtScreenX != null) DragAtScreenX(e.X);
                Invalidate();
                return;
            }
            var source = DropSource(e);
            if (source != null && p.Y >= Header)
            {
                if (p.Y > BodyBottom - 22 * Ui) SetScroll(scroll + Math.Max(1, Pitch / 6));
                else if (p.Y < Header + 20 * Ui) SetScroll(scroll - Math.Max(1, Pitch / 6));
            }
            bool after = false;
            var anchor = source == null ? null : DropAnchor(source, p, out after);
            dragOver = source != null && (!TaskLists.Same(TaskLists.NameOf(source), ListName) || anchor != null);
            insertion = anchor == null ? (dragOver && !source.Done ? 0 : -1) : cards.IndexOf(anchor) + (after ? 1 : 0);
            e.Effect = dragOver ? DragDropEffects.Move : DragDropEffects.None;
            if (source != null && DragAtScreenX != null) DragAtScreenX(e.X);
            add.BackColor = completed.BackColor = Surface;
            Invalidate(true);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            dragOver = false;
            listEdge = 0;
            insertion = -1;
            add.BackColor = completed.BackColor = Surface;
            Invalidate(true);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            string list = DraggedList(e);
            OnDragLeave(EventArgs.Empty);
            if (list != null)
            {
                if (CanMoveList && ListMoved != null && !TaskLists.Same(list, ListName))
                    ListMoved(list, ListName, PointToClient(new Point(e.X, e.Y)).X >= Width / 2);
                return;
            }
            var source = DropSource(e);
            if (source == null || CardMoved == null) return;
            bool after;
            var anchor = DropAnchor(source, PointToClient(new Point(e.X, e.Y)), out after);
            if (!TaskLists.Same(TaskLists.NameOf(source), ListName) || anchor != null) CardMoved(source.Id, ListName, anchor == null ? null : anchor.Id, after);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (cards.Count == 0) return;
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                selected = selected < 0 ? 0 : Math.Max(0, Math.Min(cards.Count - 1, selected + (e.KeyCode == Keys.Down ? 1 : -1)));
                Reveal();
                AccessibilityNotifyClients(AccessibleEvents.Focus, selected + 1);
            }
            else if (selected >= 0 && e.KeyCode == Keys.Enter && Edit != null) Edit(cards[selected]);
            else if (selected >= 0 && e.KeyCode == Keys.Space && ToggleDone != null) ToggleDone(cards[selected]);
            else if (selected >= 0 && (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10)) && Menu != null)
                Menu(cards[selected], PointToScreen(Point.Round(Card(selected).Location)));
            else return;
            e.SuppressKeyPress = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (selected < 0 && cards.Count > 0) selected = 0;
            Reveal();
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new LaneAccess(this); }

        sealed class LaneAccess : ControlAccessibleObject
        {
            readonly TaskBoardLane lane;
            public LaneAccess(TaskBoardLane lane) : base(lane) { this.lane = lane; }
            public override int GetChildCount() { return lane.cards.Count + 2 + (lane.completedCount > 0 ? 1 : 0); }
            public override AccessibleObject GetChild(int index)
            {
                if (index < 0) return null;
                if (index < lane.cards.Count) return new CardAccess(lane, index);
                index -= lane.cards.Count;
                if (index == 0) return lane.add.AccessibilityObject;
                if (index == 1) return lane.options.AccessibilityObject;
                return index == 2 && lane.completedCount > 0 ? lane.completed.AccessibilityObject : null;
            }
            public override AccessibleObject GetFocused() { return lane.Focused ? GetChild(lane.selected) : null; }
        }

        sealed class CardAccess : AccessibleObject
        {
            readonly TaskBoardLane lane;
            readonly TodoItem task;
            int Index { get { return lane.cards.FindIndex(t => t.Id == task.Id); } }
            public CardAccess(TaskBoardLane lane, int index) { this.lane = lane; task = lane.cards[index]; }
            public override AccessibleObject Parent { get { return lane.AccessibilityObject; } }
            public override string Name { get { return task.Title; } }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleStates State
            {
                get
                {
                    int i = Index;
                    if (i < 0 || Bounds.IsEmpty) return AccessibleStates.Offscreen;
                    var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (i == lane.selected) state |= AccessibleStates.Selected | (lane.Focused ? AccessibleStates.Focused : 0);
                    return state;
                }
            }
            public override Rectangle Bounds
            {
                get
                {
                    int i = Index;
                    if (i < 0) return Rectangle.Empty;
                    var r = Rectangle.Intersect(Rectangle.Round(lane.Card(i)), new Rectangle(0, lane.Header, lane.Width, Math.Max(0, lane.BodyBottom - lane.Header)));
                    return r.IsEmpty ? r : lane.RectangleToScreen(r);
                }
            }
            public override string DefaultAction { get { return Lang.T("Edit…"); } }
            public override void DoDefaultAction()
            {
                int i = Index;
                if (i >= 0 && lane.Edit != null) lane.Edit(lane.cards[i]);
            }
        }
    }
}
