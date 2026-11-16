using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Deskmate
{
    // Tasks and notes, one window. Tasks live in Island.todos; notes are Markdown files.
    sealed class WorkWindow : KitWindow
    {
        readonly Island owner;
        readonly KitSegmented tabs = new KitSegmented();

        // Tasks
        readonly TaskRail rail = new TaskRail();
        readonly TaskTitle title = new TaskTitle();
        readonly TaskAddBox addBox = new TaskAddBox();
        readonly TaskView taskView = new TaskView();
        readonly KitSegmented taskMode = new KitSegmented();
        readonly Panel board = new Panel { AutoScroll = true, TabStop = false };
        readonly List<TaskBoardLane> boardLanes = new List<TaskBoardLane>();
        readonly List<string> boardOrder = new List<string>();
        bool BoardOn { get { return taskMode.Selected == 1; } }
        readonly TaskSyncStatus syncStatus = new TaskSyncStatus();
        readonly TaskIconButton moreBtn = new TaskIconButton { Glyph = '\uE712', AccessibleName = Lang.T("Opsi daftar") };
        string activeRailKey = "all";
        string currentSort = TaskTree.SortManual;
        bool doneOpen;
        readonly HashSet<string> collapsed = new HashSet<string>();
        readonly List<string> listOrder = new List<string>();
        List<TodoItem> savedTasks;
        string baseline;
        bool saveFailed;
        readonly ToolTip noteTips = new ToolTip { OwnerDraw = true, UseAnimation = !Anim.Reduced };

        // Notes
        readonly TaskAddBox search = new TaskAddBox();
        readonly KitSegmented noteArchiveFilter = new KitSegmented();
        readonly NoteColorStrip colorStrip;
        readonly NoteListView noteList = new NoteListView();
        readonly KitButton newNoteBtn = new KitButton { Primary = true };
        readonly NoteTagChip noteTagChip = new NoteTagChip();
        readonly TaskIconButton checkBtn = new TaskIconButton { Glyph = '\uE762', GlyphPx = 15 };
        readonly TaskIconButton colorBtn = new TaskIconButton { Glyph = '\uE790', GlyphPx = 15 };
        readonly TaskIconButton pinBtn = new TaskIconButton { Glyph = '\uE718', GlyphPx = 15 };
        readonly TaskIconButton archiveBtn = new TaskIconButton { Glyph = '\uE7B8', GlyphPx = 15 };
        readonly TaskIconButton delBtn = new TaskIconButton { Glyph = '\uE74D', GlyphPx = 15 };
        readonly TaskAddBox noteTitleBox = new TaskAddBox();
        readonly TaskAddBox editor = new TaskAddBox();
        string colorFilter = "";
        string saveStatusText = "";
        Dictionary<string, NoteMeta> noteMeta = new Dictionary<string, NoteMeta>(StringComparer.OrdinalIgnoreCase);
        readonly Timer saveTimer = new Timer { Interval = 700 };
        readonly Timer noteStatsTimer = new Timer { Interval = 120 };
        readonly Timer searchTimer = new Timer { Interval = 75 };
        HashSet<string> pins;
        List<NoteInfo> notes = new List<NoteInfo>();
        string current;
        DateTime noteModified;
        string noteMetaLine = "";
        bool loading, dirty;

        Font fText, fSub, fSmall;

        int Tab { get { return tabs.Selected; } }
        internal int CurrentTab { get { return tabs.Selected; } }
        internal void FocusInput() { FocusMain(); }
        public WorkWindow(Island owner)
        {
            this.owner = owner;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Text = Lang.T("Tugas dan catatan");
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(640, 420);
            pins = NoteFiles.LoadPins();
            colorStrip = new NoteColorStrip(noteTips);
            tabs.Items = new[] { Lang.T("Tugas"), Lang.T("Catatan") };
            tabs.AccessibleName = Lang.T("Tugas dan catatan");
            tabs.Changed += delegate(int next)
            {
                if (next == 0)
                {
                    // Keep Notes selected while saving, including if saving opens a dialog.
                    tabs.Selected = 1;
                    SelectTab(0);
                    return;
                }
                ApplyTab();
            };

            if (owner != null && owner.settings != null)
                boardOrder.AddRange(owner.settings.Get("tasks.board_order", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            taskMode.Items = new[] { Lang.T("Daftar"), "Board" };
            taskMode.AccessibleName = Lang.T("Tampilan tugas");
            taskMode.Selected = owner != null && owner.settings != null && owner.settings.GetBool("tasks.board", false) ? 1 : 0;
            taskMode.Changed += delegate
            {
                if (owner != null && owner.settings != null)
                {
                    owner.settings.Set("tasks.board", BoardOn);
                    owner.settings.Save();
                }
                string keepId = taskView.Selected != null ? taskView.Selected.Id : null;
                ApplyTab();
                RefillTasks(keepId);
            };
            rail.AccessibleName = Lang.T("Daftar tugas");
            rail.Picked += delegate(string key)
            {
                activeRailKey = key;
                RefillTasks(null);
            };
            rail.ItemMenu += delegate(string key, Point pt)
            {
                ShowListItemMenu(key, pt);
            };
            rail.NewList += delegate
            {
                PromptNewList();
            };

            title.Back = delegate { return KitInk.Window; };
            title.Click += delegate
            {
                if (title.Compact) ShowListPickerMenu();
            };

            addBox.BorderStyle = BorderStyle.None;
            addBox.AccessibleName = Lang.T("Tambah tugas");
            addBox.Wheel = taskView;
            addBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddTask();
                }
                else if (e.KeyCode == Keys.Down)
                {
                    taskView.FocusFirst();
                    e.Handled = true;
                }
            };

            syncStatus.Back = delegate { return KitInk.Window; };
            syncStatus.Click += delegate
            {
                if (syncStatus.Error && owner.SyncGoogleWorkAction != null)
                    owner.SyncGoogleWorkAction(null);
            };

            if (owner != null && owner.settings != null) currentSort = TaskTree.NormalizeSort(owner.settings.Get("tasks.sort", TaskTree.SortManual));
            else currentSort = TaskTree.SortManual;
            moreBtn.Back = delegate { return KitInk.Window; };
            moreBtn.Click += delegate { ShowMoreOptionsMenu(); };
            noteTips.BackColor = KitInk.Window; noteTips.ForeColor = KitInk.Text;
            noteTips.Draw += delegate(object o, DrawToolTipEventArgs e)
            {
                e.DrawBackground();
                KitInk.Draw(e.Graphics, noteTips.GetToolTip(e.AssociatedControl), e.Font, KitInk.Text, e.Bounds, ContentAlignment.MiddleLeft);
            };
            taskView.AccessibleName = Lang.T("Daftar tugas");
            taskView.ToggleDone += delegate(TodoItem t)
            {
                taskView.StartCheck(t, t.Done);
                ToggleTaskDone(t);
            };
            taskView.ToggleStar += ToggleTaskStar;
            taskView.EditItem += delegate(TodoItem t)
            {
                EditTask(t);
            };
            taskView.DeleteItem += delegate(TodoItem t)
            {
                DeleteTask(t);
            };
            taskView.ToggleCollapse += delegate(TodoItem t)
            {
                if (collapsed.Contains(t.Id)) collapsed.Remove(t.Id);
                else collapsed.Add(t.Id);
                RefillTasks(t.Id);
            };
            taskView.IsCollapsed = delegate(string id)
            {
                return collapsed.Contains(id);
            };
            taskView.ToggleDoneSection += delegate
            {
                doneOpen = !doneOpen;
                RefillTasks(taskView.Selected != null ? taskView.Selected.Id : null);
            };
            taskView.MoveItem += delegate(TodoItem t, bool up)
            {
                if (t != null && TaskTree.Reorder(owner.todos, t, up))
                    Changed(t.Id);
            };
            taskView.ItemMenu += delegate(TodoItem t, Point pt)
            {
                ShowTaskContextMenu(t, pt);
            };

            owner.WorkSyncStatusChanged += OnWorkSyncStatusChanged;
            Disposed += delegate { owner.WorkSyncStatusChanged -= OnWorkSyncStatusChanged; };
            search.BorderStyle = BorderStyle.None;
            search.AccessibleName = Lang.T("Cari catatan");
            search.Wheel = noteList;
            searchTimer.Tick += delegate { searchTimer.Stop(); RefreshNotes(current); };
            search.TextChanged += delegate
            {
                searchTimer.Stop();
                if (string.IsNullOrEmpty(search.Text)) RefreshNotes(current);
                else searchTimer.Start();
            };

            newNoteBtn.Text = "+ " + Lang.T("Baru");
            newNoteBtn.AccessibleName = Lang.T("Buat catatan baru");
            newNoteBtn.Click += delegate { NewNote(); };

            noteMeta = NoteFiles.LoadMeta();
            noteArchiveFilter.Items = new[] { Lang.T("Aktif"), Lang.T("Arsip") };
            noteArchiveFilter.Selected = 0;
            noteArchiveFilter.Changed += delegate(int i) { OnArchiveFilterChanged(); };

            colorStrip.Back = delegate { return KitInk.Window; };
            colorStrip.Picked += delegate(string col)
            {
                colorFilter = col ?? "";
                RefreshNotes(current);
            };

            noteList.SelectedChanged += delegate(NoteInfo n) { if (!loading && n != null) OpenNote(n.Path); };
            noteList.TogglePinItem += delegate(NoteInfo n) { if (n != null) TogglePin(n.Path); };
            noteList.DeleteItem += delegate(NoteInfo n) { if (n != null) DeleteNote(n.Path); };
            noteList.ItemMenu += delegate(NoteInfo n, Point pt) { ShowNoteContextMenu(n, pt); };

            noteTagChip.Back = delegate { return EditorHeaderColor(); };
            noteTagChip.AccessibleName = Lang.T("Tag catatan");
            noteTagChip.Click += delegate { OpenTagDialog(); };
            noteTips.SetToolTip(noteTagChip, Lang.T("Edit tag catatan"));

            checkBtn.Back = delegate { return EditorHeaderColor(); };
            checkBtn.AccessibleName = Lang.T("Checklist");
            checkBtn.Click += delegate { ToggleChecklistLine(); };
            noteTips.SetToolTip(checkBtn, Lang.T("Checklist (Ctrl+Shift+L)"));

            colorBtn.Back = delegate { return EditorHeaderColor(); };
            colorBtn.AccessibleName = Lang.T("Warna catatan");
            colorBtn.Click += delegate { ShowNoteColorMenu(); };
            noteTips.SetToolTip(colorBtn, Lang.T("Warna catatan"));

            pinBtn.Back = delegate { return EditorHeaderColor(); };
            pinBtn.AccessibleName = Lang.T("Sematkan");
            pinBtn.Click += delegate { TogglePin(current); };
            noteTips.SetToolTip(pinBtn, Lang.T("Sematkan catatan ke atas"));

            archiveBtn.Back = delegate { return EditorHeaderColor(); };
            archiveBtn.AccessibleName = Lang.T("Arsipkan");
            archiveBtn.Click += delegate { ToggleArchive(current); };
            noteTips.SetToolTip(archiveBtn, Lang.T("Arsipkan"));

            delBtn.Back = delegate { return EditorHeaderColor(); };
            delBtn.AccessibleName = Lang.T("Hapus catatan");
            delBtn.Click += delegate { DeleteNote(); };
            noteTips.SetToolTip(delBtn, Lang.T("Hapus catatan"));

            noteTitleBox.BorderStyle = BorderStyle.None;
            noteTitleBox.AccessibleName = Lang.T("Judul catatan");
            noteTitleBox.Enabled = false;
            noteTitleBox.TextChanged += delegate { OnNoteInputChanged(); };
            noteTitleBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    editor.Focus();
                }
            };

            editor.AccessibleName = Lang.T("Isi catatan");
            editor.Enabled = false;
            editor.BorderStyle = BorderStyle.None;
            editor.Multiline = true;
            editor.AcceptsReturn = true;
            editor.WordWrap = true;
            editor.ScrollBars = ScrollBars.Vertical;
            editor.HideIdleScroll = true;
            editor.TextChanged += delegate { OnNoteInputChanged(); };
            editor.KeyDown += ContinueMarkdownList;
            editor.HandleCreated += delegate { Native.ThemeScroll(editor); editor.SyncScroll(); };
            editor.MouseDown += EditorMouseDown;
            noteStatsTimer.Tick += delegate { UpdateNoteStats(); };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveCurrent(false); };

            Controls.AddRange(new Control[] { tabs, taskMode, rail, title, addBox, taskView, board, syncStatus, moreBtn });
            Controls.AddRange(new Control[] { newNoteBtn, search, noteArchiveFilter, colorStrip, noteList, noteTagChip, checkBtn, colorBtn, pinBtn, archiveBtn, delBtn, noteTitleBox, editor });
            FormClosing += (s, e) =>
            {
                if (!SaveCurrent(true)) { e.Cancel = true; return; }
                saveTimer.Stop();
                // Keep one persistent work window. The dock launcher and hotkey
                // simply restore it; Windows shutdown and app exit still close it.
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    owner.SyncWorkDock();
                }
            };
            VisibleChanged += delegate { owner.SyncWorkDock(); };
            Activated += delegate
            {
                owner.SyncWorkDock();
                owner.SyncGoogleWorkIfStale(15);
                UpdateSyncStatus();
            };
            Deactivate += delegate { owner.SyncWorkDock(); };
            Resize += delegate
            {
                if (IsHandleCreated && WindowState != FormWindowState.Minimized) Relayout();
                owner.SyncWorkDock();
            };
        }

        public void SelectTab(int i)
        {
            i = Math.Max(0, Math.Min(1, i));
            if (tabs.Selected != i && i == 0 && !SaveCurrent(true)) return;
            tabs.Selected = i;
            ApplyTab();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ShellApp.SetWindowAppId(Handle, "Deskmate.Work");
            Relayout();
            if (owner != null && (owner.todos == null || owner.taskLists == null)) owner.EnsureWorkData();
            if (owner != null && owner.todos != null) owner.todos.RemoveAll(t => t == null);
            savedTasks = CopyTasks(owner != null ? owner.todos : null);
            RefillTasks(null);
            RefreshNotes(null);
            ApplyTab();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FocusMain();
            owner.SyncGoogleWorkIfStale(15);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { saveTimer.Dispose(); noteStatsTimer.Dispose(); searchTimer.Dispose(); noteTips.Dispose(); }
            base.Dispose(disposing);
            if (disposing)
                foreach (var f in new[] { fText, fSub, fSmall }) if (f != null) f.Dispose();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == (Keys.Control | Keys.D1)) { SelectTab(0); return true; }
            if (key == (Keys.Control | Keys.D2)) { SelectTab(1); return true; }
            if (key == (Keys.Control | Keys.S)) { SaveCurrent(true); return true; }
            if (key == (Keys.Control | Keys.N)) { if (Tab == 1) NewNote();
                else EditTask(null, BoardOn ? BoardListForNewTask() : null); return true; }
            if (key == (Keys.Control | Keys.Shift | Keys.L) && Tab == 1 && current != null) { ToggleChecklistLine(); return true; }
            if (key == (Keys.Control | Keys.F) && Tab == 1) { search.Focus(); search.SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, key);
        }

        void FocusMain()
        {
            if (Tab == 0)
            {
                if (BoardOn)
                {
                    if (boardLanes.Count > 0) boardLanes[0].Focus();
                }
                else addBox.Focus();
            }
            else if (current != null) { editor.Focus(); editor.SelectionStart = 0; editor.SelectionLength = 0; }
            else search.Focus();
        }

        bool GoogleWorkOn()
        {
            return owner != null && owner.IsGoogleConnected != null && owner.IsGoogleConnected() &&
                (owner.settings == null || owner.settings.GetBool("google.tasks", true) || owner.settings.GetBool("google.drive.notes", true));
        }

        void ApplyTab()
        {
            SuspendLayout();
            try { ApplyTabLayout(); }
            finally { ResumeLayout(); }
        }

        void ApplyTabLayout()
        {
            bool t = Tab == 0;
            if (!t && current == null && notes.Count > 0) { ShowNote(notes[0].Path); RefreshNotes(current); }
            bool gConn = GoogleWorkOn();

            rail.Visible = t;
            title.Visible = t && !BoardOn;
            addBox.Visible = t && !BoardOn;
            taskMode.Visible = t;
            taskView.Visible = t && !BoardOn;
            board.Visible = t && BoardOn;
            syncStatus.Visible = t && gConn && !BoardOn;
            moreBtn.Visible = t && !BoardOn;

            newNoteBtn.Visible = search.Visible = noteArchiveFilter.Visible = colorStrip.Visible = noteList.Visible = !t;
            ApplyNoteEditorVisibility();
            Relayout();
        }

        void ApplyNoteEditorVisibility()
        {
            bool t = Tab == 0;
            bool hasNote = !t && current != null;
            noteTagChip.Visible = checkBtn.Visible = colorBtn.Visible = pinBtn.Visible = archiveBtn.Visible = delBtn.Visible = noteTitleBox.Visible = editor.Visible = hasNote;
            UpdateNoteAvailability();
        }

        internal void UpdateNoteAvailability()
        {
            bool ready = !owner.DriveNotesLoading;
            newNoteBtn.Enabled = ready;
            noteTagChip.Enabled = checkBtn.Enabled = colorBtn.Enabled = pinBtn.Enabled = archiveBtn.Enabled = delBtn.Enabled = ready && current != null;
            noteTitleBox.ReadOnly = !ready || current == NoteFiles.QuickPath;
            editor.ReadOnly = !ready;
            if (IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        internal bool FlushNoteDraft() { return SaveCurrent(false); }

        internal bool FlushQuickNoteDraft()
        {
            return current != NoteFiles.QuickPath || SaveCurrent(true);
        }

        internal void RefreshQuickNote()
        {
            RefreshNotes(current);
            if (current == NoteFiles.QuickPath && !dirty) ShowNote(current);
        }

        // ---------- layout ----------

        Rectangle wellAdd, wellSideHead, wellSearch, wellEditor, editorHeaderRect, editorMetaRect;
        int editorDividerY, toolbarDividerY, sidebarDividerX, contentTop;
        void LayoutNoteTagChip()
        {
            if (!IsHandleCreated) return;
            float s = Ui;
            int maxChipW = Math.Max((int)(80 * s), checkBtn.Left - wellEditor.X - (int)(24 * s));
            noteTagChip.SetBounds(wellEditor.X + (int)(14 * s), wellEditor.Y + (int)(9 * s), noteTagChip.PreferredWidth(maxChipW), (int)(28 * s));
        }


        public override void Relayout()
        {
            if (!IsHandleCreated) return;
            float s = Ui;
            BackColor = KitInk.Window;
            noteTips.BackColor = KitInk.Window;
            noteTips.ForeColor = KitInk.Text;
            // Controls may retain an equivalent Font instance; never dispose a font
            // still assigned to a hidden tab before its native handle is created.
            if (fText == null)
            {
                fText = UiFont(Ink.Body.Size); fSub = UiFont(Ink.Callout.Size); fSmall = UiFont(Ink.Caption.Size);
            }
            int pad = (int)(16 * s), W = ClientSize.Width, H = ClientSize.Height;
            tabs.Font = fSub;
            float tw = 0;
            foreach (var it in tabs.Items) tw = Math.Max(tw, TextRenderer.MeasureText(it, fSub).Width);
            tabs.SetBounds(pad, pad, (int)((tw + 34 * s) * tabs.Items.Length), (int)(30 * s));
            toolbarDividerY = tabs.Bottom + (int)(12 * s);
            int y0 = contentTop = toolbarDividerY + (int)(16 * s);
            int modeWidth = 2 * (Math.Max(TextRenderer.MeasureText(taskMode.Items[0], fSub).Width,
                TextRenderer.MeasureText(taskMode.Items[1], fSub).Width) + (int)(24 * s));
            taskMode.SetBounds(W - pad - modeWidth, tabs.Top, modeWidth, tabs.Height);
            int gap = (int)(12 * s), columnGap = (int)(32 * s);

            // Tasks
            bool compact = W < (int)(580 * s);
            title.Compact = compact;
            int rx, rw;
            if (compact)
            {
                rail.Visible = false;
                rx = pad;
                rw = W - 2 * pad;
            }
            else
            {
                rail.Visible = Tab == 0;
                int railW = (int)(200 * s);
                rail.SetBounds(pad, y0, railW, H - pad - y0);
                rx = pad + railW + columnGap;
                rw = W - pad - rx;
            }

            sidebarDividerX = compact ? 0 : rx - columnGap / 2;

            int btnSz = (int)(30 * s);
            moreBtn.SetBounds(W - pad - btnSz, y0, btnSz, btnSz);

            int syncWidth = syncStatus.PreferredWidth();
            syncStatus.SetBounds(moreBtn.Left - gap - syncWidth, y0, syncWidth, (int)(28 * s));

            int titleRight = (syncStatus.Visible ? syncStatus.Left - gap : moreBtn.Left - gap);
            int titleW = Math.Max(100, titleRight - rx);
            title.SetBounds(rx, y0, titleW, (int)(52 * s));

            wellAdd = new Rectangle(rx, y0 + (int)(56 * s), rw, (int)(38 * s));
            addBox.Font = fText;
            addBox.BackColor = TaskInk.Card;
            addBox.ForeColor = KitInk.Text;
            addBox.SetBounds(wellAdd.X + (int)(38 * s), 0, wellAdd.Width - (int)(52 * s), addBox.Height);
            addBox.Top = wellAdd.Y + (wellAdd.Height - addBox.Height) / 2;
            addBox.Placeholder = Lang.T("Tambah tugas…");

            int listTop = wellAdd.Bottom + gap;
            int listHeight = H - pad - listTop;
            taskView.SetBounds(rx, listTop, rw, listHeight);
            title.Visible = Tab == 0 && (!BoardOn || compact);
            board.BackColor = KitInk.Window;
            int boardTop = BoardOn && compact ? title.Bottom + gap : y0;
            board.SetBounds(rx, boardTop, rw, H - pad - boardTop);
            LayoutBoard();
            // Notes
            int lw = Math.Max((int)(240 * s), Math.Min((int)(280 * s), (int)(W * 0.31f)));
            wellSideHead = new Rectangle(pad, y0, lw, (int)(48 * s));
            newNoteBtn.Font = Ink.Semi(12.5f * s);
            newNoteBtn.Size = new Size(TextRenderer.MeasureText(newNoteBtn.Text, newNoteBtn.Font).Width + (int)(26 * s), (int)(30 * s));
            newNoteBtn.Location = new Point(pad + lw - newNoteBtn.Width, y0);

            wellSearch = new Rectangle(pad, wellSideHead.Bottom + (int)(4 * s), lw, (int)(38 * s));
            search.Font = fText; search.BackColor = KitInk.Group; search.ForeColor = KitInk.Text;
            search.Placeholder = Lang.T("Cari catatan…");
            search.SetBounds(wellSearch.X + (int)(34 * s), 0, wellSearch.Width - (int)(46 * s), search.PreferredHeight);
            search.Top = wellSearch.Y + (wellSearch.Height - search.Height) / 2;

            int filterY = wellSearch.Bottom + gap;
            noteArchiveFilter.Font = fSmall;
            int segW = Math.Min((int)(118 * s), lw - (int)(132 * s));
            noteArchiveFilter.SetBounds(pad, filterY, segW, (int)(30 * s));
            colorStrip.SetBounds(noteArchiveFilter.Right + (int)(6 * s), filterY, pad + lw - (noteArchiveFilter.Right + (int)(6 * s)), (int)(30 * s));

            int nTop = noteArchiveFilter.Bottom + gap;
            noteList.SetBounds(pad, nTop, lw, H - nTop - pad);

            int ex = pad + lw + columnGap;
            wellEditor = new Rectangle(ex, y0, W - pad - ex, H - y0 - pad);
            editorHeaderRect = new Rectangle(wellEditor.X, wellEditor.Y, wellEditor.Width, (int)(46 * s));

            int edBtnSz = (int)(30 * s), edBtnGap = (int)(4 * s);
            int tbY = wellEditor.Y + (int)(8 * s);
            int bx = wellEditor.Right - (int)(12 * s) - edBtnSz;
            delBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; archiveBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; pinBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; colorBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; checkBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);

            LayoutNoteTagChip();

            noteTitleBox.Font = Ink.Display(20f * s);
            noteTitleBox.BackColor = KitInk.Group; noteTitleBox.ForeColor = KitInk.Text;
            noteTitleBox.Placeholder = Lang.T("Judul catatan…");
            int titleTop = editorHeaderRect.Bottom + (int)(12 * s);
            noteTitleBox.SetBounds(wellEditor.X + (int)(18 * s), titleTop, wellEditor.Width - (int)(36 * s), noteTitleBox.PreferredHeight);

            editorMetaRect = new Rectangle(wellEditor.X + (int)(18 * s), noteTitleBox.Bottom + (int)(4 * s), wellEditor.Width - (int)(36 * s), (int)(20 * s));
            editorDividerY = editorMetaRect.Bottom + (int)(8 * s);

            int edTop = editorDividerY + (int)(10 * s);
            editor.Font = fText; editor.BackColor = KitInk.Group; editor.ForeColor = KitInk.Text;
            editor.Placeholder = Lang.T("Mulai menulis catatan…");
            editor.SetBounds(wellEditor.X + (int)(18 * s), edTop, wellEditor.Width - (int)(36 * s), Math.Max((int)(40 * s), wellEditor.Bottom - edTop - (int)(14 * s)));
            Native.ThemeScroll(editor);
            editor.SyncScroll();

            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(KitInk.Window);
            KitInk.Smooth(g);
            float s = Ui;
            if (fSub == null) return;
            using (var pen = new Pen(KitInk.Line))
            {
                int pad = (int)(16 * s);
                g.DrawLine(pen, pad, toolbarDividerY, ClientSize.Width - pad, toolbarDividerY);
                int split = Tab == 0 ? sidebarDividerX : (noteList.Right + wellEditor.Left) / 2;
                if (split > 0) g.DrawLine(pen, split, contentTop, split, ClientSize.Height - pad);
            }
            if (Tab == 0)
            {
                if (BoardOn) return;
                Well(g, wellAdd, Ink.RadiusControl * s);
                KitInk.Draw(g, "\uE710", Ink.Icons(14 * s), KitInk.Sub, new RectangleF(wellAdd.X + 12 * s, wellAdd.Y, 22 * s, wellAdd.Height), ContentAlignment.MiddleCenter);
            }
            else
            {
                int titleMaxW = Math.Max(40, wellSideHead.Width - newNoteBtn.Width - (int)(10 * s));
                KitInk.Draw(g, Lang.T("Catatan"), Ink.Display(24f * s), KitInk.Text, new RectangleF(wellSideHead.X, wellSideHead.Y, titleMaxW, 30 * s), ContentAlignment.MiddleLeft);
                KitInk.Draw(g, NoteCountText(), Ink.Text(12.5f * s), KitInk.Sub, new RectangleF(wellSideHead.X, wellSideHead.Y + 30 * s, wellSideHead.Width, 18 * s), ContentAlignment.MiddleLeft);

                Well(g, wellSearch, Ink.RadiusControl * s);
                KitInk.Draw(g, "\uE721", Ink.Icons(13.5f * s), KitInk.Sub, new RectangleF(wellSearch.X + 10 * s, wellSearch.Y, 20 * s, wellSearch.Height), ContentAlignment.MiddleCenter);

                PaintEditorWell(g, s);
            }
        }

        void PaintEditorWell(Graphics g, float s)
        {
            using (var cardPath = Ink.Round(wellEditor, Ink.RadiusControl * s))
            {
                using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, cardPath);
                if (current != null)
                {
                    Color noteCol = CurrentNoteColor();
                    if (noteCol != Color.Empty)
                    {
                        var clip = g.Save();
                        g.SetClip(cardPath, System.Drawing.Drawing2D.CombineMode.Intersect);
                        using (var hb = new SolidBrush(EditorHeaderColor())) g.FillRectangle(hb, editorHeaderRect);
                        using (var pen = new Pen(Ink.Mix(KitInk.Line, noteCol, 0.35f), 1f))
                            g.DrawLine(pen, editorHeaderRect.Left, editorHeaderRect.Bottom, editorHeaderRect.Right, editorHeaderRect.Bottom);
                        g.Restore(clip);
                        using (var bpen = new Pen(Ink.Mix(KitInk.Line, noteCol, 0.42f), 1.5f))
                            g.DrawPath(bpen, cardPath);
                    }
                    string rightStatus = saveStatusText.Length > 0 ? saveStatusText : (dirty ? Lang.T("Menyimpan…") : Lang.T("Tersimpan"));
                    if (owner.DriveNotesLoading) rightStatus = Lang.T("Memuat…");
                    int statusW = TextRenderer.MeasureText(rightStatus, fSmall, new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(12 * s);
                    KitInk.Draw(g, noteMetaLine, fSmall, KitInk.Sub, new RectangleF(editorMetaRect.X, editorMetaRect.Y, Math.Max(40, editorMetaRect.Width - statusW), editorMetaRect.Height), ContentAlignment.MiddleLeft);
                    KitInk.Draw(g, rightStatus, fSmall, saveFailed ? Ink.Base.Negative : KitInk.Faint, new RectangleF(editorMetaRect.Right - statusW, editorMetaRect.Y, statusW, editorMetaRect.Height), ContentAlignment.MiddleRight);
                    using (var pen = new Pen(KitInk.Line, 1f))
                        g.DrawLine(pen, wellEditor.X + 18 * s, editorDividerY, wellEditor.Right - 18 * s, editorDividerY);
                }
                else
                {
                    PaintNoteEmpty(g, s);
                }
            }
        }

        void PaintNoteEmpty(Graphics g, float s)
        {
            float box = 40 * s;
            float cx = wellEditor.X + wellEditor.Width / 2f;
            float cy = wellEditor.Y + (wellEditor.Height - 54 * s) / 2f;
            var tile = new RectangleF(cx - box / 2, cy - box / 2, box, box);
            TaskInk.Pill(g, tile, box * 0.3f, KitInk.Hover);
            KitInk.Draw(g, "\uE70B", Ink.Icons(20f * s), KitInk.Faint, tile, ContentAlignment.MiddleCenter);
            string eTitle, eSub;
            NoteEmptyStrings(out eTitle, out eSub);
            float ty = tile.Bottom + 8 * s;
            KitInk.Draw(g, eTitle, Ink.Semi(15f * s), KitInk.Text, new RectangleF(wellEditor.X + 24 * s, ty, wellEditor.Width - 48 * s, 24 * s), ContentAlignment.MiddleCenter);
            KitInk.Draw(g, eSub, Ink.Text(12.5f * s), KitInk.Sub, new RectangleF(wellEditor.X + 24 * s, ty + 24 * s, wellEditor.Width - 48 * s, 22 * s), ContentAlignment.MiddleCenter);
        }

        static void Well(Graphics g, Rectangle r, float radius)
        {
            using (var path = Ink.Round(r, radius))
            using (var b = new SolidBrush(KitInk.Group))
                g.FillPath(b, path);
        }
        // ---------- tasks ----------

        void RefillRail()
        {
            if (owner != null && (owner.todos == null || owner.taskLists == null)) owner.EnsureWorkData();
            if (owner != null && owner.todos != null) owner.todos.RemoveAll(t => t == null);
            var todos = owner != null && owner.todos != null ? owner.todos : new List<TodoItem>();
            var lists = owner != null && owner.taskLists != null ? owner.taskLists : new List<TaskListInfo>();
            TaskLists.Ensure(lists, todos);
            var railItems = new List<RailItem>();
            railItems.Add(new RailItem
            {
                Kind = RailItem.View,
                Key = "all",
                Text = Lang.T("Semua tugas"),
                Glyph = '\uE8FD',
                Count = TaskTree.OpenCount(todos, null, false)
            });
            railItems.Add(new RailItem
            {
                Kind = RailItem.View,
                Key = "starred",
                Text = Lang.T("Berbintang"),
                Glyph = '\uE735',
                Count = TaskTree.OpenCount(todos, null, true)
            });
            railItems.Add(new RailItem
            {
                Kind = RailItem.Heading,
                Text = Lang.T("Daftar tugas")
            });
            foreach (var l in lists)
            {
                railItems.Add(new RailItem
                {
                    Kind = RailItem.List,
                    Key = l.Name,
                    Text = l.Name,
                    Glyph = '\uE7C3',
                    Count = TaskTree.OpenCount(todos, l.Name, false)
                });
            }
            railItems.Add(new RailItem
            {
                Kind = RailItem.New,
                Key = "new",
                Text = Lang.T("Buat daftar baru"),
                Glyph = '\uE710'
            });
            rail.Items = railItems;
            rail.Selected = activeRailKey;
            rail.Invalidate();
        }

        void RefillTasks(string keepId)
        {
            if (keepId == null && taskView.Selected != null) keepId = taskView.Selected.Id;
            if (owner != null && (owner.todos == null || owner.taskLists == null)) owner.EnsureWorkData();
            if (owner != null && owner.todos != null) owner.todos.RemoveAll(t => t == null);
            var todos = owner != null && owner.todos != null ? owner.todos : new List<TodoItem>();
            var lists = owner != null && owner.taskLists != null ? owner.taskLists : new List<TaskListInfo>();
            TaskLists.Ensure(lists, todos);

            if (activeRailKey != "all" && activeRailKey != "starred")
            {
                if (TaskLists.Find(lists, activeRailKey) == null)
                    activeRailKey = TaskLists.Default;
            }

            bool starred = activeRailKey == "starred";
            string listName = (activeRailKey == "all" || activeRailKey == "starred") ? null : activeRailKey;

            listOrder.Clear();
            foreach (var l in lists) listOrder.Add(l.Name);

            var openRows = TaskTree.Open(todos, listName, starred, currentSort, listOrder, collapsed);
            var doneItems = TaskTree.Done(todos, listName, starred);

            string emptyTitle, emptySub;
            if (starred)
            {
                emptyTitle = Lang.T("Tidak ada tugas berbintang");
                emptySub = Lang.T("Beri bintang pada tugas untuk melihatnya di sini");
            }
            else if (doneItems.Count > 0)
            {
                emptyTitle = Lang.T("Semua tugas selesai");
                emptySub = Lang.T("Kerja bagus! Istirahat sejenak.");
            }
            else
            {
                emptyTitle = Lang.T("Belum ada tugas");
                emptySub = Lang.T("Tambahkan tugas untuk memulai");
            }

            taskView.SetData(openRows, doneItems, doneOpen, listName == null, emptyTitle, emptySub);
            if (keepId != null) taskView.Select(keepId);
            if (BoardOn) RefillBoard(todos, lists, listName, starred, keepId);

            if (activeRailKey == "all") title.Title = Lang.T("Semua tugas");
            else if (activeRailKey == "starred") title.Title = Lang.T("Berbintang");
            else title.Title = activeRailKey;

            int openCount = TaskTree.OpenCount(todos, listName, starred);
            title.Subtitle = openCount == 0 ? Lang.T("Tidak ada tugas aktif") : (openCount == 1 ? "1 " + Lang.T("tugas aktif") : openCount + " " + Lang.T("tugas aktif"));

            RefillRail();
            UpdateSyncStatus();
        }

        void UpdateSyncStatus()
        {
            bool gConn = GoogleWorkOn();
            if (!gConn || owner == null)
            {
                syncStatus.Visible = false;
                return;
            }
            syncStatus.Visible = Tab == 0 && !BoardOn;
            if (owner.WorkSyncBusy)
            {
                syncStatus.Set(Lang.T("Menyinkronkan…"), false, true);
            }
            else if (!string.IsNullOrEmpty(owner.LastSyncError))
            {
                syncStatus.Set(owner.LastSyncError, true, false);
            }
            else if (owner.LastSyncUtc > DateTime.MinValue)
            {
                syncStatus.Set(Lang.T("Tersinkron") + " · " + Fmt.Ago(owner.LastSyncUtc), false, false);
            }
            else
            {
                syncStatus.Set(Lang.T("Tersinkron · baru saja"), false, false);
            }
        }

        void OnWorkSyncStatusChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)OnWorkSyncStatusChanged);
                return;
            }
            UpdateSyncStatus();
        }

        static List<TodoItem> CopyTasks(List<TodoItem> items)
        {
            if (items == null) return new List<TodoItem>();
            var copy = new List<TodoItem>(items.Count);
            foreach (var t in items)
            {
                if (t == null) continue;
                copy.Add(new TodoItem
                {
                    Id = t.Id,
                    Title = t.Title,
                    Due = t.Due,
                    Star = t.Star,
                    Done = t.Done,
                    Created = t.Created,
                    DoneAt = t.DoneAt,
                    ListName = t.ListName,
                    Notes = t.Notes,
                    GoogleTaskId = t.GoogleTaskId,
                    GoogleListId = t.GoogleListId,
                    UpdatedUtc = t.UpdatedUtc,
                    Dirty = t.Dirty,
                    ParentId = t.ParentId,
                    Position = t.Position,
                    Moved = t.Moved
                });
            }
            return copy;
        }

        bool Changed(string keepId)
        {
            if (owner == null) return false;
            if (owner.todos == null) owner.EnsureWorkData();
            bool saved = owner.TodosChanged();
            if (saved) savedTasks = CopyTasks(owner.todos);
            else
            {
                if (savedTasks != null) owner.todos = CopyTasks(savedTasks);
                KitAsk.Notice(this, Lang.T("Tugas gagal disimpan. Perubahan dibatalkan."));
            }
            RefillTasks(keepId);
            return saved;
        }

        static bool ParseDue(string text, out string title, out DateTime? due)
        {
            title = text.Trim(); due = null;
            text = text.Replace("@hari ini", "@hariini");
            int at = text.LastIndexOf('@');
            if (at < 0 || (at > 0 && !char.IsWhiteSpace(text[at - 1]))) return true;
            DateTime? date = null; TimeSpan? time = null;
            foreach (var raw in text.Substring(at + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw.ToLowerInvariant();
                DateTime d; TimeSpan t;
                if (w == "besok" || w == "tomorrow") date = DateTime.Today.AddDays(1);
                else if (w == "hari" || w == "hariini" || w == "today") date = DateTime.Today;
                else if (DateTime.TryParseExact(w, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) date = d.Date;
                else if (TimeSpan.TryParseExact(w.Replace('.', ':'), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out t)) time = t;
                else return true;
            }
            if (!date.HasValue && !time.HasValue) return true;
            string head = text.Substring(0, at).Trim();
            if (head.Length == 0) return true;
            title = head;
            var day = date ?? DateTime.Today;
            var when = day + (time ?? new TimeSpan(9, 0, 0));
            if (!date.HasValue && when <= DateTime.Now) when = when.AddDays(1);
            due = when;
            return true;
        }

        void AddTask()
        {
            if (owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            string text = addBox.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length == 0) return;
            string titleStr; DateTime? due;
            ParseDue(text, out titleStr, out due);
            string targetList = (activeRailKey == "all" || activeRailKey == "starred") ? TaskLists.Default : activeRailKey;
            var item = new TodoItem
            {
                Title = titleStr,
                Due = due,
                ListName = targetList,
                Star = activeRailKey == "starred",
                Position = TaskTree.TopPosition(owner.todos, targetList, null),
                UpdatedUtc = DateTime.UtcNow,
                Dirty = true
            };
            owner.todos.Add(item);
            addBox.Clear();
            Changed(item.Id);
            addBox.Focus();
        }

        void EditTask(TodoItem item) { EditTask(item, null); }

        void EditTask(TodoItem item, string initialList)
        {
            if (owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            using (var dlg = new TaskEditor(item, initialList))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var t = item ?? new TodoItem();
                t.Title = dlg.TaskTitle;
                t.Due = dlg.Due;
                t.Star = dlg.Star;
                if (t.Done != dlg.Done) t.DoneAt = dlg.Done ? (DateTime?)DateTime.Now : null;
                t.Done = dlg.Done;
                if (!TaskLists.Same(t.ListName, dlg.ListName))
                {
                    t.ListName = dlg.ListName;
                    t.ParentId = "";
                    t.Moved = true;
                }
                t.Notes = dlg.Notes;
                t.UpdatedUtc = DateTime.UtcNow;
                t.Dirty = true;
                if (item == null)
                {
                    t.Position = TaskTree.TopPosition(owner.todos, t.ListName, null);
                    owner.todos.Add(t);
                }
                Changed(t.Id);
            }
        }

        void DeleteTask(TodoItem t)
        {
            if (t == null || owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            int count = TaskTree.Children(owner.todos, t).Count;
            string msg = count > 0 ? Lang.F("Hapus tugas ini beserta {0} subtugasnya?", count) : Lang.T("Hapus tugas ini?");
            if (!KitAsk.Confirm(this, msg)) return;
            TaskTree.Delete(owner.todos, t);
            Changed(null);
        }

        void ClearDone()
        {
            if (owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            if (!KitAsk.Confirm(this, Lang.T("Hapus semua tugas yang selesai?"), Lang.T("Hapus"), Lang.T("Batal"))) return;
            string list = (activeRailKey == "all" || activeRailKey == "starred") ? null : activeRailKey;
            TaskTree.ClearDone(owner.todos, list);
            Changed(null);
        }

        void ToggleTaskStar(TodoItem t)
        {
            t.Star = !t.Star;
            t.UpdatedUtc = DateTime.UtcNow;
            t.Dirty = true;
            Changed(t.Id);
        }

        TodoItem FindTask(string id)
        {
            return owner == null || owner.todos == null ? null : owner.todos.Find(t => t != null && t.Id == id);
        }

        string BoardListForNewTask()
        {
            var lane = boardLanes.Find(l => l.ContainsFocus);
            return lane != null ? lane.ListName : TaskLists.Find(owner.taskLists, activeRailKey) != null ? activeRailKey : TaskLists.Default;
        }

        void RefillBoard(List<TodoItem> tasks, List<TaskListInfo> lists, string listName, bool starred, string keepId)
        {
            var ordered = new List<TaskListInfo>();
            foreach (string id in boardOrder)
            {
                var match = lists.Find(l => l.Id == id);
                if (match != null && !ordered.Contains(match)) ordered.Add(match);
            }
            foreach (var list in lists)
                if (!ordered.Contains(list)) ordered.Add(list);
            string[] names = listName == null ? ordered.ConvertAll(l => l.Name).ToArray() : new[] { listName };
            foreach (var lane in boardLanes.ToArray())
                if (!Array.Exists(names, name => TaskLists.Same(name, lane.ListName)))
                {
                    boardLanes.Remove(lane);
                    lane.Dispose();
                }

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var lane = boardLanes.Find(l => TaskLists.Same(l.ListName, name));
                if (lane == null)
                {
                    lane = new TaskBoardLane(name);
                    lane.Edit += EditTask;
                    lane.AddTask += list => EditTask(null, list);
                    lane.ListMenu += ShowListItemMenu;
                    lane.Menu += ShowTaskContextMenu;
                    lane.ToggleDone += ToggleTaskDone;
                    lane.CardMoved += MoveBoardCard;
                    lane.ListMoved += MoveBoardList;
                    lane.Resolve += FindTask;
                    lane.DragAtScreenX += ScrollBoardAtEdge;
                    lane.LayoutChanged += delegate { RefillTasks(null); };
                    board.Controls.Add(lane);
                }
                else boardLanes.Remove(lane);
                boardLanes.Insert(i, lane);
                lane.TabIndex = i;
                lane.CanMoveList = names.Length > 1;
                var open = TaskTree.Open(tasks, name, starred, TaskTree.SortManual, null, null);
                lane.SetCards(open.ConvertAll(row => row.Item), TaskTree.Done(tasks, name, starred), keepId);
            }
            LayoutBoard();
        }

        void MoveBoardList(string source, string target, bool after)
        {
            var from = boardLanes.Find(l => TaskLists.Same(l.ListName, source));
            var to = boardLanes.Find(l => TaskLists.Same(l.ListName, target));
            if (from == null || to == null || from == to) return;
            int oldIndex = boardLanes.IndexOf(from);
            int newIndex = boardLanes.IndexOf(to);
            if (oldIndex < newIndex) newIndex--;
            if (after) newIndex++;
            if (oldIndex == newIndex) return;
            boardLanes.RemoveAt(oldIndex);
            boardLanes.Insert(newIndex, from);
            boardOrder.Clear();
            for (int i = 0; i < boardLanes.Count; i++)
            {
                var lane = boardLanes[i];
                lane.TabIndex = i;
                var list = TaskLists.Find(owner.taskLists, lane.ListName);
                if (list != null) boardOrder.Add(list.Id);
            }
            if (owner.settings != null)
            {
                owner.settings.Set("tasks.board_order", string.Join(",", boardOrder.ToArray()));
                owner.settings.Save();
            }
            LayoutBoard();
            board.ScrollControlIntoView(from);
            from.ShowLanding(null);
        }

        void ScrollBoardAtEdge(int screenX)
        {
            if (!board.HorizontalScroll.Visible) return;
            int x = board.PointToClient(new Point(screenX, 0)).X;
            int edge = (int)(30 * Ui);
            int step = x < edge ? -(int)(12 * Ui) : x > board.ClientSize.Width - edge ? (int)(12 * Ui) : 0;
            if (step != 0) board.AutoScrollPosition = new Point(Math.Max(0, -board.AutoScrollPosition.X + step), 0);
        }

        void LayoutBoard()
        {
            if (boardLanes.Count == 0) return;
            int width = (int)(254 * Ui), gap = (int)(8 * Ui);
            int totalWidth = boardLanes.Count * (width + gap) - gap;
            int offset = Math.Min(-board.AutoScrollPosition.X, Math.Max(0, totalWidth - board.ClientSize.Width));
            int height = board.ClientSize.Height - (totalWidth > board.ClientSize.Width ? SystemInformation.HorizontalScrollBarHeight : 0);
            board.SuspendLayout();
            board.AutoScrollMinSize = new Size(totalWidth, 0);
            board.AutoScrollPosition = new Point(0, 0);
            for (int i = 0; i < boardLanes.Count; i++)
            {
                var lane = boardLanes[i];
                lane.SetBounds(i * (width + gap), 0, width, Math.Max(0, Math.Min(height, lane.PreferredLaneHeight)));
            }
            board.ResumeLayout();
            board.AutoScrollPosition = new Point(offset, 0);
        }

        void ToggleTaskDone(TodoItem t)
        {
            t.Done = !t.Done;
            t.DoneAt = t.Done ? (DateTime?)DateTime.Now : null;
            t.UpdatedUtc = DateTime.UtcNow;
            t.Dirty = true;
            Changed(t.Id);
        }

        void MoveBoardCard(string id, string list, string anchorId, bool after)
        {
            var task = FindTask(id);
            if (task == null || !TaskTree.MoveBoardCard(owner.todos, task, list, FindTask(anchorId), after)) return;
            if (!Changed(id)) return;
            var lane = boardLanes.Find(l => TaskLists.Same(list, l.ListName));
            if (lane != null)
            {
                lane.FocusCard(id);
                lane.ShowLanding(id);
            }
        }

        void ShowTaskContextMenu(TodoItem t, Point screenPos)
        {
            if (t == null) return;
            var menu = new ContextMenuStrip();
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Items.Add(Lang.T("Edit…"), null, delegate { EditTask(t); });
            if (BoardOn)
            {
                menu.Items.Add(Lang.T(t.Done ? "Tandai belum selesai" : "Tandai selesai"), null,
                    delegate { ToggleTaskDone(t); });
                menu.Items.Add(Lang.T(t.Star ? "Hapus bintang" : "Beri bintang"), null, delegate { ToggleTaskStar(t); });
            }
            bool canIndent = TaskTree.CanIndent(owner.todos, t);
            bool canOutdent = !string.IsNullOrEmpty(t.ParentId);
            if (canIndent)
            {
                menu.Items.Add(Lang.T("Jadikan subtugas"), null, delegate
                {
                    if (TaskTree.Indent(owner.todos, t)) Changed(t.Id);
                });
            }
            if (canOutdent)
            {
                menu.Items.Add(Lang.T("Keluarkan dari subtugas"), null, delegate
                {
                    if (TaskTree.Outdent(owner.todos, t)) Changed(t.Id);
                });
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Pindah ke atas"), null, delegate
            {
                if (TaskTree.Reorder(owner.todos, t, true)) Changed(t.Id);
            });
            menu.Items.Add(Lang.T("Pindah ke bawah"), null, delegate
            {
                if (TaskTree.Reorder(owner.todos, t, false)) Changed(t.Id);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Hapus"), null, delegate { DeleteTask(t); });
            MenuLook.Apply(menu);
            menu.Show(screenPos);
        }

        void RenameList(string listName)
        {
            if (string.IsNullOrEmpty(listName) || TaskLists.IsDefault(listName)) return;
            using (var dlg = new ListNameDialog(Lang.T("Ganti nama daftar"), listName))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string newName = TaskLists.Clean(dlg.Value);
                    if (newName.Length > 0 && !TaskLists.Same(newName, listName))
                    {
                        if (TaskLists.Rename(owner.taskLists, owner.todos, listName, newName))
                        {
                            if (TaskLists.Same(activeRailKey, listName)) activeRailKey = newName;
                            owner.ListsChanged();
                            owner.TodosChanged();
                            RefillRail();
                            RefillTasks(null);
                        }
                    }
                }
            }
        }

        void DeleteList(string listName)
        {
            if (string.IsNullOrEmpty(listName) || TaskLists.IsDefault(listName)) return;
            if (KitAsk.Confirm(this, Lang.F("Hapus daftar \"{0}\" beserta seluruh tugas di dalamnya?", listName)))
            {
                TaskLists.Delete(owner.taskLists, owner.todos, listName);
                if (TaskLists.Same(activeRailKey, listName)) activeRailKey = TaskLists.Default;
                owner.ListsChanged();
                owner.TodosChanged();
                RefillRail();
                RefillTasks(null);
            }
        }

        void ShowListItemMenu(string listName, Point screenPos)
        {
            if (string.IsNullOrEmpty(listName)) return;
            var menu = new ContextMenuStrip();
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            bool isDefault = TaskLists.IsDefault(listName);
            var renameItem = menu.Items.Add(Lang.T("Ganti nama daftar…"), null, delegate { RenameList(listName); });
            renameItem.Enabled = !isDefault;

            var delItem = menu.Items.Add(Lang.T("Hapus daftar"), null, delegate { DeleteList(listName); });
            delItem.Enabled = !isDefault;

            MenuLook.Apply(menu);
            menu.Show(screenPos);
        }

        void PromptNewList()
        {
            using (var dlg = new ListNameDialog(Lang.T("Daftar baru"), ""))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string name = TaskLists.Clean(dlg.Value);
                    if (name.Length > 0)
                    {
                        var created = TaskLists.Create(owner.taskLists, name);
                        if (created != null)
                        {
                            activeRailKey = created.Name;
                            owner.ListsChanged();
                            RefillRail();
                            RefillTasks(null);
                        }
                    }
                }
            }
        }

        void ShowListPickerMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Items.Add(Lang.T("Semua tugas"), null, delegate { activeRailKey = "all"; RefillTasks(null); });
            menu.Items.Add(Lang.T("Berbintang"), null, delegate { activeRailKey = "starred"; RefillTasks(null); });
            menu.Items.Add(new ToolStripSeparator());
            if (owner.taskLists != null)
            {
                foreach (var l in owner.taskLists)
                {
                    string name = l.Name;
                    menu.Items.Add(name, null, delegate { activeRailKey = name; RefillTasks(null); });
                }
            }
            MenuLook.Apply(menu);
            menu.Items.Add(Lang.T("Buat daftar baru"), null, delegate { PromptNewList(); });
            menu.Show(title, new Point(0, title.Height));
        }

        void ShowMoreOptionsMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            if (!BoardOn)
            {
                var mManual = new ToolStripMenuItem(Lang.T("Urutkan: Urutan saya"), null, delegate
                {
                    currentSort = TaskTree.SortManual;
                    if (owner != null && owner.settings != null) { owner.settings.Set("tasks.sort", currentSort); owner.settings.Save(); }
                    RefillTasks(null);
                });
                mManual.Checked = currentSort == TaskTree.SortManual;
                menu.Items.Add(mManual);

                var mDate = new ToolStripMenuItem(Lang.T("Urutkan: Tanggal"), null, delegate
                {
                    currentSort = TaskTree.SortDate;
                    if (owner != null && owner.settings != null) { owner.settings.Set("tasks.sort", currentSort); owner.settings.Save(); }
                    RefillTasks(null);
                });
                mDate.Checked = currentSort == TaskTree.SortDate;
                menu.Items.Add(mDate);

                var mTitle = new ToolStripMenuItem(Lang.T("Urutkan: Judul"), null, delegate
                {
                    currentSort = TaskTree.SortTitle;
                    if (owner != null && owner.settings != null) { owner.settings.Set("tasks.sort", currentSort); owner.settings.Save(); }
                    RefillTasks(null);
                });
                mTitle.Checked = currentSort == TaskTree.SortTitle;
                menu.Items.Add(mTitle);

                menu.Items.Add(new ToolStripSeparator());
            }

            menu.Items.Add(Lang.T("Tugas baru…"), null, delegate { EditTask(null); });

            menu.Items.Add(new ToolStripSeparator());

            bool isSpecial = activeRailKey == "all" || activeRailKey == "starred" || TaskLists.IsDefault(activeRailKey);
            var mRename = menu.Items.Add(Lang.T("Ganti nama daftar…"), null, delegate { RenameList(activeRailKey); });
            mRename.Enabled = !isSpecial;

            var mDelete = menu.Items.Add(Lang.T("Hapus daftar"), null, delegate { DeleteList(activeRailKey); });
            mDelete.Enabled = !isSpecial;

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(Lang.T("Hapus tugas selesai"), null, delegate { ClearDone(); });

            MenuLook.Apply(menu);
            menu.Show(moreBtn, new Point(0, moreBtn.Height));
        }

        sealed class ListNameDialog : KitWindow
        {
            readonly TaskAddBox input = new TaskAddBox();
            readonly KitButton okBtn = new KitButton { Primary = true };
            readonly KitButton cancelBtn = new KitButton();
            readonly bool allowEmpty;
            Rectangle inputWell;
            public string Value { get { return input.Text.Trim(); } }

            public ListNameDialog(string title, string initial) : this(title, initial, false, "") { }

            public ListNameDialog(string title, string initial, bool allowEmpty, string placeholder)
            {
                this.allowEmpty = allowEmpty;
                Text = title;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                float s = Ui;
                ClientSize = new Size((int)(360 * s), (int)(136 * s));
                inputWell = new Rectangle((int)(20 * s), (int)(18 * s), (int)(320 * s), (int)(36 * s));
                input.BorderStyle = BorderStyle.None;
                input.Text = initial ?? "";
                input.Placeholder = placeholder ?? "";
                input.Font = UiFont(Ink.Body.Size);
                input.BackColor = KitInk.Group;
                input.ForeColor = KitInk.Text;
                input.SetBounds(inputWell.X + (int)(12 * s), 0, inputWell.Width - (int)(24 * s), input.PreferredHeight);
                input.Top = inputWell.Y + (inputWell.Height - input.Height) / 2;
                okBtn.Text = Lang.T("Simpan");
                cancelBtn.Text = Lang.T("Batal");
                okBtn.Font = cancelBtn.Font = UiFont(Ink.Callout.Size);
                cancelBtn.SetBounds((int)(150 * s), (int)(78 * s), (int)(85 * s), (int)(34 * s));
                okBtn.SetBounds((int)(245 * s), (int)(78 * s), (int)(95 * s), (int)(34 * s));
                okBtn.Click += delegate
                {
                    if (!this.allowEmpty && Value.Length == 0) return;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                cancelBtn.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                input.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter && (this.allowEmpty || Value.Length > 0)) { DialogResult = DialogResult.OK; Close(); }
                };
                Controls.AddRange(new Control[] { input, cancelBtn, okBtn });
                Shown += delegate { input.Focus(); input.SelectAll(); };
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                base.OnPaintBackground(e);
                KitInk.Smooth(e.Graphics);
                Well(e.Graphics, inputWell, Ink.RadiusControl * Ui);
            }
        }

        // ---------- notes ----------

        string NoteCountText()
        {
            int count = notes != null ? notes.Count : 0;
            if (count == 0)
            {
                if (current == null) return "";
                if (!string.IsNullOrWhiteSpace(search.Text) || !string.IsNullOrEmpty(colorFilter))
                    return Lang.T("Tidak ada catatan yang cocok");
                return Lang.T("Tidak ada catatan");
            }
            if (count == 1) return Lang.T("1 catatan");
            return Lang.F("{0} catatan", count);
        }

        void NoteEmptyStrings(out string eTitle, out string eSub)
        {
            if (noteArchiveFilter.Selected == 1)
            {
                eTitle = Lang.T("Arsip catatan kosong");
                eSub = Lang.T("Catatan yang diarsipkan akan muncul di sini");
            }
            else if (!string.IsNullOrWhiteSpace(search.Text) || !string.IsNullOrEmpty(colorFilter))
            {
                eTitle = Lang.T("Tidak ada catatan yang cocok");
                eSub = Lang.T("Coba kata kunci atau filter warna lain");
            }
            else
            {
                eTitle = Lang.T("Belum ada catatan");
                eSub = Lang.T("Buat catatan baru untuk menyimpan ide atau checklist");
            }
        }

        Color CurrentNoteColor()
        {
            NoteMeta m = GetMeta(current, false);
            return m != null ? NotePalette.ColorOf(m.Color) : Color.Empty;
        }

        Color EditorHeaderColor()
        {
            Color c = CurrentNoteColor();
            if (c == Color.Empty) return KitInk.Group;
            return Ink.Mix(KitInk.Group, c, Ink.Light ? 0.11f : 0.15f);
        }

        static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int count = 0;
            bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) inWord = false;
                else if (!inWord) { inWord = true; count++; }
            }
            return count;
        }

        static void CountChecklist(string text, out int done, out int total)
        {
            done = 0; total = 0;
            if (string.IsNullOrEmpty(text)) return;
            foreach (var raw in text.Split('\n'))
            {
                string tr = raw.Trim();
                if (tr.StartsWith("- [ ] ", StringComparison.Ordinal)) total++;
                else if (tr.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) { total++; done++; }
            }
        }

        void UpdateNoteStats()
        {
            noteStatsTimer.Stop();
            if (current == null) { noteMetaLine = ""; return; }
            string body = editor.Text;
            noteMetaLine = noteModified.ToString("d MMM", CultureInfo.CurrentCulture) + "  \u00B7  " + Lang.F("{0} kata", CountWords(body));
            int done, total;
            CountChecklist(body, out done, out total);
            if (total > 0) noteMetaLine += "  \u00B7  " + Lang.F("{0}/{1} selesai", done, total);
            if (IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        void OnNoteInputChanged()
        {
            if (loading || current == null) return;
            bool statusChanged = !dirty || saveFailed || saveStatusText.Length > 0;
            dirty = true;
            saveFailed = false;
            saveStatusText = "";
            saveTimer.Stop();
            saveTimer.Start();
            noteStatsTimer.Start();
            if (statusChanged && IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        void RefreshNotes(string keepPath)
        {
            if (!IsHandleCreated) return;
            loading = true;
            try
            {
                notes = NoteFiles.List(search.Text, pins, noteMeta, noteArchiveFilter.Selected == 1, colorFilter);
                noteList.SetData(notes, keepPath);
            }
            finally { loading = false; }
            ApplyNoteEditorVisibility();
            // Filtering the sidebar never clears the current editor.
            Invalidate();
        }

        void OnArchiveFilterChanged()
        {
            if (!SaveCurrent(true))
            {
                noteArchiveFilter.Selected = noteArchiveFilter.Selected == 0 ? 1 : 0;
                return;
            }
            RefreshNotes(current);
            bool hasCurrent = false;
            if (current != null)
                for (int i = 0; i < notes.Count; i++)
                    if (string.Equals(notes[i].Path, current, StringComparison.OrdinalIgnoreCase)) { hasCurrent = true; break; }
            if (!hasCurrent)
                ShowNote(notes.Count > 0 ? notes[0].Path : null);
        }

        void OpenNote(string path)
        {
            if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase)) return;
            if (!SaveCurrent(true)) { RefreshNotes(current); return; }
            ShowNote(path);
        }

        void ShowNote(string path)
        {
            string text = "";
            if (path != null && !NoteFiles.TryRead(path, out text))
            {
                KitAsk.Notice(this, Lang.T("Catatan tidak dapat dibaca."));
                RefreshNotes(current);
                return;
            }
            current = path;
            baseline = text;
            loading = true;
            bool isQuick = path != null && string.Equals(path, NoteFiles.QuickPath, StringComparison.OrdinalIgnoreCase);
            if (path == null)
            {
                noteTitleBox.ReadOnly = false;
                noteTitleBox.BackColor = KitInk.Group;
                noteTitleBox.Text = "";
                editor.Text = "";
                noteTitleBox.Enabled = false;
                editor.Enabled = false;
            }
            else if (isQuick)
            {
                noteTitleBox.Text = Lang.T("Catatan kilat");
                noteTitleBox.ReadOnly = true;
                noteTitleBox.BackColor = KitInk.Group;
                editor.Text = NoteFiles.NormalizeNewlines(text);
                noteTitleBox.Enabled = true;
                editor.Enabled = true;
            }
            else
            {
                string nTitle, nBody;
                NoteFiles.SplitNote(text, out nTitle, out nBody);
                noteTitleBox.ReadOnly = false;
                noteTitleBox.BackColor = KitInk.Group;
                noteTitleBox.Text = nTitle;
                editor.Text = NoteFiles.NormalizeNewlines(nBody);
                noteTitleBox.Enabled = true;
                editor.Enabled = true;
            }
            saveStatusText = "";
            saveFailed = false;
            noteTitleBox.SelectionStart = 0;
            noteTitleBox.SelectionLength = 0;
            editor.SelectionStart = 0;
            editor.SelectionLength = 0;
            loading = false;
            dirty = false;
            noteModified = DateTime.Now;
            if (path != null)
            {
                try { noteModified = System.IO.File.GetLastWriteTime(path); }
                catch (Exception) { }
            }
            UpdateNoteStats();

            NoteMeta m = GetMeta(path, false);
            noteTagChip.Tags = m != null ? (m.Labels ?? "") : "";
            string curCol = m != null ? (m.Color ?? "") : "";
            colorBtn.DotColor = NotePalette.ColorOf(curCol);
            bool pinned = path != null && NoteFiles.IsPinned(pins, path);
            pinBtn.Active = pinned;
            noteTips.SetToolTip(pinBtn, pinned ? Lang.T("Lepas sematan") : Lang.T("Sematkan"));
            bool archived = m != null && m.Archived;
            archiveBtn.Active = archived;
            noteTips.SetToolTip(archiveBtn, archived ? Lang.T("Pulihkan") : Lang.T("Arsipkan"));

            ApplyNoteEditorVisibility();
            if (IsHandleCreated) LayoutNoteTagChip();
            editor.SyncScroll();
            Invalidate(true);
        }

        string SerializeCurrentNote()
        {
            if (current != null && string.Equals(current, NoteFiles.QuickPath, StringComparison.OrdinalIgnoreCase))
                return editor.Text;
            return NoteFiles.JoinNote(noteTitleBox.Text, editor.Text);
        }

        void SetSaveState(string text)
        {
            saveStatusText = text ?? "";
            if (IsHandleCreated && !editorMetaRect.IsEmpty) Invalidate(editorMetaRect);
        }

        bool SaveCurrent() { return SaveCurrent(true); }
        bool SaveCurrent(bool notify)
        {
            if (current == null || !dirty) return true;
            saveTimer.Stop();
            string disk;
            string payload = SerializeCurrentNote();
            if (payload == baseline)
            {
                dirty = false; saveFailed = false;
                SetSaveState("");
                return true;
            }
            // Compare content, not timestamps: quick-note append and external edits
            // must never be silently overwritten by an old editor buffer.
            if (!NoteFiles.TryRead(current, out disk) || disk != baseline)
            {
                saveFailed = true; SetSaveState(Lang.T("Konflik"));
                if (!notify) return false;
                saveTimer.Stop();
                if (!KitAsk.Confirm(this, Lang.T("Catatan berubah di luar aplikasi. Timpa draf ini?"), Lang.T("Timpa"))) return false;
                if (!NoteFiles.Write(current, payload)) return SaveError(notify);
            }
            else if (!NoteFiles.Write(current, payload)) return SaveError(notify);
            saveTimer.Stop();
            baseline = payload; dirty = false; saveFailed = false;
            noteModified = DateTime.Now;
            UpdateNoteStats();
            SetSaveState("");
            owner.ScheduleDriveNotesUpload();
            RefreshNotes(current);
            return true;
        }

        bool SaveError(bool notify)
        {
            saveFailed = true; SetSaveState(Lang.T("Gagal menyimpan"));
            if (notify) KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
            return false;
        }

        void NewNote()
        {
            if (owner.DriveNotesLoading) return;
            if (!SaveCurrent(true)) return;
            string path;
            try { path = NoteFiles.NewPath(); }
            catch (Exception ex) { KitAsk.Notice(this, ex.Message); return; }
            if (!NoteFiles.Write(path, "")) { SaveError(true); return; }
            owner.ScheduleDriveNotesUpload();
            search.Text = "";
            colorFilter = "";
            colorStrip.Selected = "";
            colorStrip.Invalidate();
            if (noteArchiveFilter.Selected != 0) noteArchiveFilter.Selected = 0;
            ShowNote(path);
            RefreshNotes(path);
            noteTitleBox.Focus();
        }

        void TogglePin() { TogglePin(current); }
        void TogglePin(string path)
        {
            if (owner.DriveNotesLoading) return;
            if (string.IsNullOrEmpty(path)) return;
            if (!SaveCurrent(true)) return;
            NoteMeta meta = GetMeta(path, true);
            DateTime previous = meta.UpdatedUtc;
            meta.UpdatedUtc = DateTime.UtcNow;
            if (!NoteFiles.SaveMeta(noteMeta))
            {
                meta.UpdatedUtc = previous;
                KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                return;
            }
            NoteFiles.SetPin(pins, path, !NoteFiles.IsPinned(pins, path));
            RefreshNotes(current);
            owner.ScheduleDriveNotesUpload();
            if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase)) ShowNote(current);
        }

        void DeleteNote() { DeleteNote(null); }

        void DeleteNote(string path)
        {
            if (owner.DriveNotesLoading) return;
            // A pinned draft is saved first; deleting another row must not drop it
            if (!SaveCurrent(true)) return;
            string target = path ?? current;
            if (target == null) return;
            if (!KitAsk.Confirm(this, Lang.T("Hapus catatan ini?")) || owner.DriveNotesLoading) return;
            saveTimer.Stop();
            string deleted = target;
            string delKey = NoteFiles.KeyOf(deleted);
            NoteMeta delMeta = GetMeta(deleted, false);
            if (delMeta == null || string.IsNullOrEmpty(delMeta.DriveId))
            {
                NoteMeta freshMeta;
                if (NoteFiles.LoadMeta().TryGetValue(delKey, out freshMeta) && freshMeta != null && !string.IsNullOrEmpty(freshMeta.DriveId))
                    delMeta = freshMeta;
            }
            var prevDeletes = NoteFiles.LoadDeletedDrive();
            if (delMeta != null && !string.IsNullOrEmpty(delMeta.DriveId)) NoteFiles.AddDeletedDrive(delMeta.DriveId);
            NoteFiles.AddDeletedKey(delKey);
            if (!NoteFiles.Remove(deleted))
            {
                NoteFiles.SaveDeletedDrive(prevDeletes);
                KitAsk.Notice(this, Lang.T("Catatan gagal dihapus."));
                return;
            }
            if (NoteFiles.IsPinned(pins, deleted)) NoteFiles.SetPin(pins, deleted, false);
            if (noteMeta.Remove(delKey)) NoteFiles.SaveMeta(noteMeta);
            owner.ScheduleDriveNotesUpload();
            bool wasOpen = string.Equals(deleted, current, StringComparison.OrdinalIgnoreCase);
            if (wasOpen) { dirty = false; current = null; }
            RefreshNotes(deleted);
            if (wasOpen) { if (notes.Count > 0) ShowNote(notes[0].Path); else ShowNote(null); }
            else if (current != null) ShowNote(current);
        }

        void ShowNoteContextMenu(NoteInfo n, Point screenPos)
        {
            if (n == null) return;
            var menu = new ContextMenuStrip();
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            bool pinned = NoteFiles.IsPinned(pins, n.Path);
            NoteMeta m = GetMeta(n.Path, false);
            bool archived = m != null && m.Archived;
            menu.Items.Add(pinned ? Lang.T("Lepas sematan") : Lang.T("Sematkan"), null, delegate { TogglePin(n.Path); });
            menu.Items.Add(archived ? Lang.T("Pulihkan") : Lang.T("Arsipkan"), null, delegate { ToggleArchive(n.Path); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Hapus"), null, delegate { DeleteNote(n.Path); });
            MenuLook.Apply(menu);
            menu.Show(screenPos);
        }

        void OpenTagDialog()
        {
            if (owner.DriveNotesLoading) return;
            if (current == null) return;
            NoteMeta m = GetMeta(current, false);
            string initial = m != null ? (m.Labels ?? "") : "";
            using (var dlg = new ListNameDialog(Lang.T("Tag catatan"), initial, true, Lang.T("Pisahkan dengan koma, mis. kerja, ide")))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || owner.DriveNotesLoading) return;
                NoteMeta target = GetMeta(current, true);
                string prevLabels = target.Labels;
                DateTime prevUtc = target.UpdatedUtc;
                target.Labels = NotePalette.CleanTags(dlg.Value);
                target.UpdatedUtc = DateTime.UtcNow;
                if (!NoteFiles.SaveMeta(noteMeta))
                {
                    target.Labels = prevLabels;
                    target.UpdatedUtc = prevUtc;
                    KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                    return;
                }
                noteTagChip.Tags = target.Labels;
                noteTagChip.Invalidate();
                LayoutNoteTagChip();
                RefreshNotes(current);
                owner.ScheduleDriveNotesUpload();
            }
        }

        void ShowNoteColorMenu()
        {
            if (owner.DriveNotesLoading) return;
            if (current == null) return;
            NoteMeta cm = GetMeta(current, false);
            string curCol = cm != null ? (cm.Color ?? "") : "";
            var menu = new ContextMenuStrip();
            var imgs = new List<Image>();
            menu.Closed += delegate
            {
                BeginInvoke(new Action(delegate
                {
                    foreach (var img in imgs) img.Dispose();
                    menu.Dispose();
                }));
            };
            for (int i = 0; i < NotePalette.Keys.Length; i++)
            {
                string key = NotePalette.Keys[i];
                var item = new ToolStripMenuItem(NotePalette.NameOf(key, false));
                item.Checked = string.Equals(curCol, key, StringComparison.OrdinalIgnoreCase);
                var bmp = new Bitmap(14, 14);
                using (var g = Graphics.FromImage(bmp))
                {
                    KitInk.Smooth(g);
                    Color sw = NotePalette.ColorOf(key);
                    if (sw == Color.Empty)
                    {
                        using (var b = new SolidBrush(KitInk.Group)) g.FillEllipse(b, 1, 1, 11, 11);
                        using (var p = new Pen(KitInk.Sub, 1.3f)) g.DrawEllipse(p, 1, 1, 11, 11);
                    }
                    else
                    {
                        using (var b = new SolidBrush(sw)) g.FillEllipse(b, 1, 1, 11, 11);
                    }
                }
                imgs.Add(bmp);
                item.Image = bmp;
                item.Click += delegate
                {
                    if (owner.DriveNotesLoading || current == null) return;
                    NoteMeta m = GetMeta(current, true);
                    string prevColor = m.Color;
                    DateTime prevUtc = m.UpdatedUtc;
                    m.Color = key;
                    m.UpdatedUtc = DateTime.UtcNow;
                    if (!NoteFiles.SaveMeta(noteMeta))
                    {
                        m.Color = prevColor;
                        m.UpdatedUtc = prevUtc;
                        KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                        return;
                    }
                    colorBtn.DotColor = NotePalette.ColorOf(key);
                    RefreshNotes(current);
                    Invalidate(true);
                    owner.ScheduleDriveNotesUpload();
                };
                menu.Items.Add(item);
            }
            MenuLook.Apply(menu);
            menu.Show(colorBtn, new Point(0, colorBtn.Height));
        }

        internal void ReloadFromSync()
        {
            ReloadTasksFromSync();
            ReloadNotesFromDisk();
        }

        internal void ReloadTasksFromSync()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (owner != null && (owner.todos == null || owner.taskLists == null)) owner.EnsureWorkData();
            if (owner != null && owner.todos != null) owner.todos.RemoveAll(t => t == null);
            savedTasks = CopyTasks(owner != null ? owner.todos : null);
            RefillTasks(null);
        }

        internal void RefreshNoteDriveIds(Dictionary<string, NoteMeta> fresh)
        {
            foreach (var entry in fresh)
            {
                NoteMeta local;
                if (noteMeta.TryGetValue(entry.Key, out local)) local.DriveId = entry.Value.DriveId;
                else noteMeta[entry.Key] = entry.Value;
            }
        }

        internal void ReloadNotesFromDisk()
        {
            if (IsDisposed || !IsHandleCreated) return;
            pins = NoteFiles.LoadPins();
            noteMeta = NoteFiles.LoadMeta();
            RefreshNotes(current);
            if (!dirty) ShowNote(current ?? (notes.Count > 0 ? notes[0].Path : null));
        }

        NoteMeta GetMeta(string path, bool create)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = NoteFiles.KeyOf(path);
            NoteMeta m;
            if (!noteMeta.TryGetValue(key, out m) && create)
            {
                m = new NoteMeta { Key = key };
                noteMeta[key] = m;
            }
            return m;
        }

        void ToggleArchive() { ToggleArchive(current); }
        void ToggleArchive(string path)
        {
            if (owner.DriveNotesLoading) return;
            if (string.IsNullOrEmpty(path)) return;
            if (!SaveCurrent(true)) return;
            NoteMeta m = GetMeta(path, true);
            bool prevArchived = m.Archived;
            DateTime prevUtc = m.UpdatedUtc;
            m.Archived = !m.Archived;
            m.UpdatedUtc = DateTime.UtcNow;
            if (!NoteFiles.SaveMeta(noteMeta))
            {
                m.Archived = prevArchived;
                m.UpdatedUtc = prevUtc;
                KitAsk.Notice(this, Lang.T("Gagal menyimpan."));
                return;
            }
            RefreshNotes(current);
            if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase))
                ShowNote(notes.Count > 0 ? notes[0].Path : null);
            owner.ScheduleDriveNotesUpload();
        }

        static readonly Regex MarkdownList = new Regex(
            @"^([ 	]*)([-+*] \[[ xX]\]|[-+*]|([0-9]+)[.)])([ 	]+)(.*)$");

        void ContinueMarkdownList(object sender, KeyEventArgs e)
        {
            if (e.KeyData != Keys.Enter || editor.ReadOnly || editor.SelectionLength != 0) return;
            string text = editor.Text;
            int pos = editor.SelectionStart;
            if (pos == 0 || (pos < text.Length && text[pos] != '\r' && text[pos] != '\n')) return;
            int start = text.LastIndexOf('\n', pos - 1) + 1;
            Match item = MarkdownList.Match(text.Substring(start, pos - start));
            if (!item.Success) return;

            string marker = item.Groups[2].Value;
            if (item.Groups[3].Success)
            {
                int number;
                if (!int.TryParse(item.Groups[3].Value, out number) || number == int.MaxValue) return;
                marker = (number + 1).ToString(CultureInfo.InvariantCulture) + marker[marker.Length - 1];
            }
            else if (marker.Length > 1) marker = marker[0] + " [ ]";

            e.SuppressKeyPress = true;
            if (string.IsNullOrWhiteSpace(item.Groups[5].Value))
            {
                editor.Select(start, pos - start);
                editor.SelectedText = "";
            }
            else editor.SelectedText = Environment.NewLine + item.Groups[1].Value + marker + item.Groups[4].Value;
        }

        void ToggleChecklistLine()
        {
            if (current == null || !editor.Enabled || editor.ReadOnly) return;
            int pos = editor.SelectionStart;
            string text = editor.Text;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, pos - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int lineEnd = text.IndexOf('\n', pos);
            if (lineEnd < 0) lineEnd = text.Length;
            string line = text.Substring(lineStart, lineEnd - lineStart);
            string next;
            if (line.StartsWith("- [ ] ", StringComparison.Ordinal)) next = "- [x] " + line.Substring(6);
            else if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) next = line.Substring(6);
            else next = "- [ ] " + line;
            editor.Text = text.Substring(0, lineStart) + next + text.Substring(lineEnd);
            editor.SelectionStart = Math.Min(editor.Text.Length, lineStart + next.Length);
            editor.Focus();
        }

        void EditorMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !editor.Enabled || editor.ReadOnly) return;
            int idx = editor.GetCharIndexFromPosition(e.Location);
            string text = editor.Text;
            int lineStart = text.LastIndexOf('\n', Math.Max(0, idx - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            if (idx - lineStart > 6) return;
            if (lineStart + 6 <= text.Length)
            {
                string prefix = text.Substring(lineStart, 6);
                if (prefix == "- [ ] ")
                {
                    editor.Text = text.Substring(0, lineStart) + "- [x] " + text.Substring(lineStart + 6);
                    editor.SelectionStart = lineStart + 6;
                }
                else if (string.Equals(prefix, "- [x] ", StringComparison.OrdinalIgnoreCase))
                {
                    editor.Text = text.Substring(0, lineStart) + "- [ ] " + text.Substring(lineStart + 6);
                    editor.SelectionStart = lineStart + 6;
                }
            }
        }
    }
}
