using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Tasks and notes, one window. Tasks live in Island.todos; notes are Markdown files.
    sealed partial class WorkWindow : KitWindow
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
        readonly TaskIconButton blockBtn = new TaskIconButton { Glyph = '\uE710', GlyphPx = 15 };
        NoteBlockMenu blockMenu;
        readonly TaskIconButton checkBtn = new TaskIconButton { Glyph = '\uE762', GlyphPx = 15 };
        readonly TaskIconButton colorBtn = new TaskIconButton { Glyph = '\uE790', GlyphPx = 15 };
        readonly TaskIconButton pinBtn = new TaskIconButton { Glyph = '\uE718', GlyphPx = 15 };
        readonly TaskIconButton archiveBtn = new TaskIconButton { Glyph = '\uE7B8', GlyphPx = 15 };
        readonly TaskIconButton delBtn = new TaskIconButton { Glyph = '\uE74D', GlyphPx = 15 };
        readonly TaskAddBox noteTitleBox = new TaskAddBox();
        readonly NoteEditor editor = new NoteEditor();
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
            blockBtn.Back = delegate { return EditorHeaderColor(); };
            blockBtn.AccessibleName = Lang.T("Sisipkan blok");
            blockBtn.Click += delegate { ShowBlockMenu(false); };
            noteTips.SetToolTip(blockBtn, Lang.T("Sisipkan blok") + " (/)");
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
            editor.WordWrap = true;
            editor.TextChanged += delegate { OnNoteInputChanged(); };
            editor.KeyDown += ContinueMarkdownList;
            editor.OpenBlockMenu = delegate { ShowBlockMenu(true); };
            editor.MouseDown += EditorMouseDown;
            noteStatsTimer.Tick += delegate { editor.RefreshFormatting(); UpdateNoteStats(); };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveCurrent(false); };

            Controls.AddRange(new Control[] { tabs, taskMode, rail, title, addBox, taskView, board, syncStatus, moreBtn });
            Controls.AddRange(new Control[] { newNoteBtn, search, noteArchiveFilter, colorStrip, noteList, noteTagChip, blockBtn, checkBtn, colorBtn, pinBtn, archiveBtn, delBtn, noteTitleBox, editor });
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
            if (disposing)
            {
                if (blockMenu != null) blockMenu.Dispose();
                saveTimer.Dispose(); noteStatsTimer.Dispose(); searchTimer.Dispose(); noteTips.Dispose();
            }
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
            noteTagChip.Visible = blockBtn.Visible = checkBtn.Visible = colorBtn.Visible = pinBtn.Visible = archiveBtn.Visible = delBtn.Visible = noteTitleBox.Visible = editor.Visible = hasNote;
            UpdateNoteAvailability();
        }

        internal void UpdateNoteAvailability()
        {
            bool ready = !owner.DriveNotesLoading;
            newNoteBtn.Enabled = ready;
            noteTagChip.Enabled = blockBtn.Enabled = checkBtn.Enabled = colorBtn.Enabled = pinBtn.Enabled = archiveBtn.Enabled = delBtn.Enabled = ready && current != null;
            noteTitleBox.ReadOnly = !ready || current == NoteFiles.QuickPath;
            editor.ReadOnly = !ready;
            if (!ready && blockMenu != null) blockMenu.Close();
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
            int maxChipW = Math.Max((int)(80 * s), blockBtn.Left - wellEditor.X - (int)(24 * s));
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
            bx -= edBtnSz + edBtnGap; blockBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);

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
            editor.SetBounds(wellEditor.X + (int)(18 * s), edTop, wellEditor.Width - (int)(36 * s), Math.Max((int)(40 * s), wellEditor.Bottom - edTop - (int)(14 * s)));
            Native.ThemeScroll(editor);
            editor.RefreshFormatting();

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
    }
}
