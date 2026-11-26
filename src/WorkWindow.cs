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
        readonly TaskIconButton searchBtn = new TaskIconButton { Glyph = '\uE721', GlyphPx = 15, AccessibleName = Lang.T("Cari tugas") };
        readonly TaskAddBox taskSearch = new TaskAddBox();
        bool taskSearchOn;
        string addHint = "";
        int addHintW;
        // Completed rows linger with their check filled, then leave together
        readonly Timer completeTimer = new Timer { Interval = 420 };
        readonly List<string> pendingDone = new List<string>();
        readonly WorkToast toast = new WorkToast();
        int shownTab = -1;
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
            rail.Picked += delegate(string key) { PickView(key); };
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
            // The composer's accent edge follows focus
            addBox.GotFocus += delegate { Invalidate(Rectangle.Inflate(wellAdd, 2, 2)); };
            addBox.LostFocus += delegate { Invalidate(Rectangle.Inflate(wellAdd, 2, 2)); };

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
            searchBtn.Back = delegate { return KitInk.Window; };
            searchBtn.Click += delegate { if (taskSearchOn) CloseTaskSearch(); else OpenTaskSearch(); };
            noteTips.SetToolTip(searchBtn, Lang.T("Cari tugas") + " (Ctrl+F)");
            noteTips.SetToolTip(moreBtn, Lang.T("Opsi daftar"));

            taskSearch.BorderStyle = BorderStyle.None;
            taskSearch.AccessibleName = Lang.T("Cari tugas");
            taskSearch.Wheel = taskView;
            taskSearch.Visible = false;
            taskSearch.TextChanged += delegate { RefillTasks(null); };
            taskSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseTaskSearch(); }
                else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; taskView.FocusFirst(); }
            };
            taskSearch.GotFocus += delegate { Invalidate(Rectangle.Inflate(wellAdd, 2, 2)); };
            taskSearch.LostFocus += delegate { Invalidate(Rectangle.Inflate(wellAdd, 2, 2)); };
            addBox.TextChanged += delegate { UpdateAddHint(); };

            completeTimer.Tick += delegate { FlushCompletions(); };
            toast.Back = delegate { return Tab == 1 ? KitInk.Group : BoardOn ? KitInk.Window : TaskInk.Card; };
            toast.Placing += PlaceToast;
            noteTips.BackColor = KitInk.Window; noteTips.ForeColor = KitInk.Text;
            noteTips.Draw += delegate(object o, DrawToolTipEventArgs e)
            {
                e.DrawBackground();
                KitInk.Draw(e.Graphics, noteTips.GetToolTip(e.AssociatedControl), e.Font, KitInk.Text, e.Bounds, ContentAlignment.MiddleLeft);
            };
            taskView.AccessibleName = Lang.T("Daftar tugas");
            taskView.ToggleDone += BeginToggleDone;
            taskView.ToggleStar += delegate(TodoItem t)
            {
                taskView.PopStar(t.Id);
                ToggleTaskStar(t);
            };
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
            search.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && search.TextLength > 0) { e.SuppressKeyPress = true; search.Clear(); }
                else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; noteList.Focus(); }
            };
            search.GotFocus += delegate { Invalidate(Rectangle.Inflate(wellSearch, 2, 2)); };
            search.LostFocus += delegate { Invalidate(Rectangle.Inflate(wellSearch, 2, 2)); };

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

            Controls.AddRange(new Control[] { tabs, taskMode, rail, title, addBox, taskSearch, taskView, board, syncStatus, searchBtn, moreBtn });
            Controls.AddRange(new Control[] { newNoteBtn, search, noteArchiveFilter, colorStrip, noteList, noteTagChip, blockBtn, checkBtn, colorBtn, pinBtn, archiveBtn, delBtn, noteTitleBox, editor });
            Controls.Add(toast);
            toast.BringToFront();
            FormClosing += (s, e) =>
            {
                FlushCompletions();
                toast.Dismiss();
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
            VisibleChanged += delegate { if (!Visible) FlushCompletions(); owner.SyncWorkDock(); };
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
            // The first look at the window: the open side's list cascades in
            if (Tab == 0) { if (!BoardOn) taskView.PlayEnter(); }
            else noteList.PlayEnter();
            owner.SyncGoogleWorkIfStale(15);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (blockMenu != null) blockMenu.Dispose();
                saveTimer.Dispose(); noteStatsTimer.Dispose(); searchTimer.Dispose(); noteTips.Dispose(); completeTimer.Dispose();
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
            if (key == (Keys.Control | Keys.F) && Tab == 0 && !BoardOn) { OpenTaskSearch(); return true; }
            // Ctrl+Z undoes the last toast action unless a text field owns the key for its own undo
            if (key == (Keys.Control | Keys.Z) && toast.HasAction && !(ActiveControl is TextBoxBase)) { toast.RunAction(); return true; }
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

            if (BoardOn && taskSearchOn) { taskSearchOn = false; taskSearch.Text = ""; }
            rail.Visible = t;
            title.Visible = t && !BoardOn;
            addBox.Visible = t && !BoardOn && !taskSearchOn;
            taskSearch.Visible = t && !BoardOn && taskSearchOn;
            taskMode.Visible = t;
            taskView.Visible = t && !BoardOn;
            board.Visible = t && BoardOn;
            syncStatus.Visible = t && gConn && !BoardOn;
            moreBtn.Visible = t && !BoardOn;
            searchBtn.Visible = t && !BoardOn;
            searchBtn.Active = taskSearchOn;

            newNoteBtn.Visible = search.Visible = noteArchiveFilter.Visible = colorStrip.Visible = noteList.Visible = !t;
            ApplyNoteEditorVisibility();
            Relayout();
            if (shownTab != Tab)
            {
                // Switching tabs: the new side's list cascades in, and an old undo no longer applies here
                if (shownTab >= 0)
                {
                    toast.Dismiss();
                    if (t) { if (!BoardOn) taskView.PlayEnter(); }
                    else noteList.PlayEnter();
                }
                shownTab = Tab;
            }
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
            int chipH = (int)(28 * s);
            noteTagChip.SetBounds(wellEditor.X + (int)(Ink.Sp12 * s), editorHeaderRect.Y + (editorHeaderRect.Height - chipH) / 2, noteTagChip.PreferredWidth(maxChipW), chipH);
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
            int pad = (int)(Ink.Sp16 * s), W = ClientSize.Width, H = ClientSize.Height;
            // Toolbar controls on the 32 px step; buttons and inputs below take the 36 px target
            int bar = (int)(Ink.Sp32 * s), target = (int)(36 * s);
            tabs.Font = fSub;
            float tw = 0;
            foreach (var it in tabs.Items) tw = Math.Max(tw, TextRenderer.MeasureText(it, fSub).Width);
            tabs.SetBounds(pad, pad, (int)((tw + Ink.Sp32 * s) * tabs.Items.Length), bar);
            toolbarDividerY = tabs.Bottom + (int)(Ink.Sp12 * s);
            int y0 = contentTop = toolbarDividerY + (int)(Ink.Sp20 * s);
            int modeWidth = 2 * (Math.Max(TextRenderer.MeasureText(taskMode.Items[0], fSub).Width,
                TextRenderer.MeasureText(taskMode.Items[1], fSub).Width) + (int)(Ink.Sp24 * s));
            taskMode.SetBounds(W - pad - modeWidth, tabs.Top, modeWidth, tabs.Height);
            int gap = (int)(Ink.Sp12 * s), columnGap = (int)(Ink.Sp32 * s);

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

            int btnSz = bar;
            moreBtn.SetBounds(W - pad - btnSz, y0, btnSz, btnSz);
            searchBtn.SetBounds(moreBtn.Left - (int)(Ink.Sp4 * s) - btnSz, y0, btnSz, btnSz);

            int syncWidth = syncStatus.PreferredWidth();
            syncStatus.SetBounds(searchBtn.Left - gap - syncWidth, y0 + (btnSz - (int)(28 * s)) / 2, syncWidth, (int)(28 * s));

            int titleRight = (syncStatus.Visible ? syncStatus.Left - gap : searchBtn.Left - gap);
            int titleW = Math.Max(100, titleRight - rx);
            // Title line, then the subtitle on its own line with air between them
            title.SetBounds(rx, y0, titleW, (int)(56 * s));

            // Composer: a calm input field one tone step off the canvas; the accent shows on focus
            wellAdd = new Rectangle(rx, title.Bottom + (int)(Ink.Sp12 * s), rw, target);
            addBox.Font = fText;
            addBox.BackColor = TaskInk.Card;
            addBox.ForeColor = KitInk.Text;
            addBox.SetBounds(wellAdd.X + (int)(40 * s), 0, Math.Max(40, wellAdd.Width - (int)(52 * s) - addHintW), addBox.Height);
            addBox.Top = wellAdd.Y + (wellAdd.Height - addBox.Height) / 2;
            addBox.Placeholder = AddPlaceholder();
            taskSearch.Font = fText;
            taskSearch.BackColor = TaskInk.Card;
            taskSearch.ForeColor = KitInk.Text;
            taskSearch.SetBounds(wellAdd.X + (int)(40 * s), 0, wellAdd.Width - (int)(52 * s), taskSearch.Height);
            taskSearch.Top = wellAdd.Y + (wellAdd.Height - taskSearch.Height) / 2;
            taskSearch.Placeholder = Lang.T("Cari judul atau detail tugas…");

            int listTop = wellAdd.Bottom + (int)(Ink.Sp16 * s);
            int listHeight = H - pad - listTop;
            taskView.SetBounds(rx, listTop, rw, listHeight);
            title.Visible = Tab == 0 && (!BoardOn || compact);
            board.BackColor = KitInk.Window;
            // The horizontal lane scrollbar follows the theme instead of staying light
            Native.ThemeScroll(board);
            int boardTop = BoardOn && compact ? title.Bottom + gap : y0;
            board.SetBounds(rx, boardTop, rw, H - pad - boardTop);
            LayoutBoard();
            // Notes
            int lw = Math.Max((int)(240 * s), Math.Min((int)(280 * s), (int)(W * 0.31f)));
            wellSideHead = new Rectangle(pad, y0, lw, (int)(56 * s));
            newNoteBtn.Font = Ink.Semi(12.5f * s);
            newNoteBtn.Size = new Size(TextRenderer.MeasureText(newNoteBtn.Text, newNoteBtn.Font).Width + (int)(Ink.Sp32 * s), target);
            newNoteBtn.Location = new Point(pad + lw - newNoteBtn.Width, y0 - (int)(3 * s));

            wellSearch = new Rectangle(pad, wellSideHead.Bottom + (int)(Ink.Sp12 * s), lw, target);
            search.Font = fText; search.BackColor = KitInk.Group; search.ForeColor = KitInk.Text;
            search.Placeholder = Lang.T("Cari catatan…");
            search.SetBounds(wellSearch.X + (int)(36 * s), 0, wellSearch.Width - (int)(48 * s), search.PreferredHeight);
            search.Top = wellSearch.Y + (wellSearch.Height - search.Height) / 2;

            int filterY = wellSearch.Bottom + gap;
            noteArchiveFilter.Font = fSmall;
            int segW = Math.Min((int)(118 * s), lw - (int)(132 * s));
            noteArchiveFilter.SetBounds(pad, filterY, segW, bar);
            colorStrip.SetBounds(noteArchiveFilter.Right + (int)(Ink.Sp8 * s), filterY, pad + lw - (noteArchiveFilter.Right + (int)(Ink.Sp8 * s)), bar);

            int nTop = noteArchiveFilter.Bottom + gap;
            noteList.SetBounds(pad, nTop, lw, H - nTop - pad);

            int ex = pad + lw + columnGap;
            wellEditor = new Rectangle(ex, y0, W - pad - ex, H - y0 - pad);
            editorHeaderRect = new Rectangle(wellEditor.X, wellEditor.Y, wellEditor.Width, (int)(48 * s));

            int edBtnSz = bar, edBtnGap = (int)(Ink.Sp4 * s);
            int tbY = wellEditor.Y + (editorHeaderRect.Height - edBtnSz) / 2;
            int bx = wellEditor.Right - (int)(Ink.Sp12 * s) - edBtnSz;
            delBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; archiveBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; pinBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; colorBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; checkBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);
            bx -= edBtnSz + edBtnGap; blockBtn.SetBounds(bx, tbY, edBtnSz, edBtnSz);

            LayoutNoteTagChip();

            int inset = (int)(Ink.Sp20 * s);
            noteTitleBox.Font = Ink.Display(20f * s);
            noteTitleBox.BackColor = KitInk.Group; noteTitleBox.ForeColor = KitInk.Text;
            noteTitleBox.Placeholder = Lang.T("Judul catatan…");
            int titleTop = editorHeaderRect.Bottom + (int)(Ink.Sp16 * s);
            noteTitleBox.SetBounds(wellEditor.X + inset, titleTop, wellEditor.Width - 2 * inset, noteTitleBox.PreferredHeight);

            editorMetaRect = new Rectangle(wellEditor.X + inset, noteTitleBox.Bottom + (int)(Ink.Sp4 * s), wellEditor.Width - 2 * inset, (int)(20 * s));
            editorDividerY = editorMetaRect.Bottom + (int)(Ink.Sp12 * s);

            int edTop = editorDividerY + (int)(Ink.Sp12 * s);
            editor.Font = fText; editor.BackColor = KitInk.Group; editor.ForeColor = KitInk.Text;
            editor.SetBounds(wellEditor.X + inset, edTop, wellEditor.Width - 2 * inset, Math.Max((int)(40 * s), wellEditor.Bottom - edTop - (int)(Ink.Sp16 * s)));
            Native.ThemeScroll(editor);
            editor.RefreshFormatting();
            PlaceToast();

            Invalidate(true);
        }

        void PlaceToast()
        {
            if (!IsHandleCreated) return;
            float s = Ui;
            Rectangle host = Tab == 1 ? wellEditor : BoardOn ? board.Bounds : taskView.Bounds;
            if (host.Width <= 0 || host.Height <= 0) host = ClientRectangle;
            var size = toast.Measure(Math.Min(host.Width - (int)(Ink.Sp32 * s), (int)(520 * s)));
            toast.SetBounds(host.X + (host.Width - size.Width) / 2, host.Bottom - size.Height - (int)(14 * s), size.Width, size.Height);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(KitInk.Window);
            KitInk.Smooth(g);
            float s = Ui;
            if (fSub == null) return;
            var line = KitInk.PenOf(KitInk.Line, 1f);
            int pad = (int)(Ink.Sp16 * s);
            g.DrawLine(line, pad, toolbarDividerY, ClientSize.Width - pad, toolbarDividerY);
            int split = Tab == 0 ? sidebarDividerX : (noteList.Right + wellEditor.Left) / 2;
            if (split > 0) g.DrawLine(line, split, contentTop, split, ClientSize.Height - pad);
            if (Tab == 0)
            {
                if (BoardOn) return;
                bool focusedField = taskSearchOn ? taskSearch.Focused : addBox.Focused;
                Field(g, wellAdd, focusedField, s);
                KitInk.Draw(g, taskSearchOn ? "\uE721" : "\uE710", Ink.Icons((taskSearchOn ? 13.5f : 14f) * s), focusedField ? KitInk.Accent : KitInk.Sub, new RectangleF(wellAdd.X + Ink.Sp12 * s, wellAdd.Y, 22 * s, wellAdd.Height), ContentAlignment.MiddleCenter);
                if (!taskSearchOn && addHint.Length > 0)
                {
                    // Live preview of the date the composer understood, as a calm accent chip
                    var fChip = Ink.Small(11.5f * s);
                    float cw = addHintW - Ink.Sp8 * s;
                    var chip = new RectangleF(wellAdd.Right - Ink.Sp8 * s - cw, wellAdd.Y + (wellAdd.Height - 22 * s) / 2f, cw, 22 * s);
                    TaskInk.Pill(g, chip, chip.Height / 2f, TaskInk.Wash(KitInk.Group, 0.16f));
                    KitInk.Draw(g, "\uE787", Ink.Icons(10.5f * s), KitInk.Accent, new RectangleF(chip.X + 7 * s, chip.Y, 14 * s, chip.Height), ContentAlignment.MiddleCenter);
                    KitInk.Draw(g, addHint, fChip, KitInk.Accent, new RectangleF(chip.X + 22 * s, chip.Y, chip.Width - 28 * s, chip.Height), ContentAlignment.MiddleLeft);
                }
            }
            else
            {
                int titleMaxW = Math.Max(40, wellSideHead.Width - newNoteBtn.Width - (int)(Ink.Sp8 * s));
                KitInk.Draw(g, Lang.T("Catatan"), Ink.Display(22f * s), KitInk.Text, new RectangleF(wellSideHead.X, wellSideHead.Y, titleMaxW, 30 * s), ContentAlignment.MiddleLeft);
                KitInk.Draw(g, NoteCountText(), Ink.Text(12.5f * s), KitInk.Sub, new RectangleF(wellSideHead.X, wellSideHead.Y + (30 + Ink.Sp8 - 2) * s, wellSideHead.Width, 20 * s), ContentAlignment.MiddleLeft);

                Field(g, wellSearch, search.Focused, s);
                KitInk.Draw(g, "\uE721", Ink.Icons(13.5f * s), search.Focused ? KitInk.Accent : KitInk.Sub, new RectangleF(wellSearch.X + Ink.Sp12 * s, wellSearch.Y, 20 * s, wellSearch.Height), ContentAlignment.MiddleCenter);

                PaintEditorWell(g, s);
            }
        }

        void PaintEditorWell(Graphics g, float s)
        {
            float radius = Ink.RadiusCard * s;
            KitInk.FillRound(g, wellEditor, radius, KitInk.Group);
            if (current != null)
            {
                Color noteCol = CurrentNoteColor();
                if (noteCol != Color.Empty)
                {
                    // Label colour: a tinted header band and a soft edge, nothing louder
                    var clip = g.Save();
                    g.SetClip(KitInk.Shape(wellEditor, radius), System.Drawing.Drawing2D.CombineMode.Intersect);
                    g.FillRectangle(KitInk.BrushOf(EditorHeaderColor()), editorHeaderRect);
                    g.DrawLine(KitInk.PenOf(Ink.Mix(KitInk.Line, noteCol, 0.35f), 1f), editorHeaderRect.Left, editorHeaderRect.Bottom, editorHeaderRect.Right, editorHeaderRect.Bottom);
                    g.Restore(clip);
                    KitInk.StrokeRound(g, wellEditor, radius, Ink.Mix(KitInk.Line, noteCol, 0.42f), 1.5f);
                }
                string rightStatus = saveStatusText.Length > 0 ? saveStatusText : (dirty ? Lang.T("Menyimpan…") : Lang.T("Tersimpan"));
                if (owner.DriveNotesLoading) rightStatus = Lang.T("Memuat…");
                int statusW = TextRenderer.MeasureText(rightStatus, fSmall, new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(Ink.Sp12 * s);
                KitInk.Draw(g, noteMetaLine, fSmall, KitInk.Sub, new RectangleF(editorMetaRect.X, editorMetaRect.Y, Math.Max(40, editorMetaRect.Width - statusW), editorMetaRect.Height), ContentAlignment.MiddleLeft);
                KitInk.Draw(g, rightStatus, fSmall, saveFailed ? Ink.Base.Negative : KitInk.Faint, new RectangleF(editorMetaRect.Right - statusW, editorMetaRect.Y, statusW, editorMetaRect.Height), ContentAlignment.MiddleRight);
                g.DrawLine(KitInk.PenOf(KitInk.Line, 1f), editorMetaRect.Left, editorDividerY, editorMetaRect.Right, editorDividerY);
            }
            else
            {
                PaintNoteEmpty(g, s);
            }
        }

        void PaintNoteEmpty(Graphics g, float s)
        {
            float box = 44 * s;
            float cx = wellEditor.X + wellEditor.Width / 2f;
            float cy = wellEditor.Y + (wellEditor.Height - 54 * s) / 2f;
            var tile = TaskInk.EmptyTile(g, cx, cy, box, KitInk.Group, "\uE70B", s);
            string eTitle, eSub;
            NoteEmptyStrings(out eTitle, out eSub);
            float ty = tile.Bottom + Ink.Sp12 * s;
            KitInk.Draw(g, eTitle, Ink.Semi(15f * s), KitInk.Text, new RectangleF(wellEditor.X + Ink.Sp24 * s, ty, wellEditor.Width - 2 * Ink.Sp24 * s, 24 * s), ContentAlignment.MiddleCenter);
            KitInk.Draw(g, eSub, Ink.Text(12.5f * s), KitInk.Sub, new RectangleF(wellEditor.X + Ink.Sp24 * s, ty + 24 * s, wellEditor.Width - 2 * Ink.Sp24 * s, 22 * s), ContentAlignment.MiddleCenter);
        }

        // Input field: Group one step off the canvas; focus draws the accent edge instead of a ring
        static void Field(Graphics g, Rectangle r, bool focused, float s)
        {
            float radius = Ink.RadiusControl * s;
            KitInk.FillRound(g, r, radius, KitInk.Group);
            if (focused) KitInk.StrokeRound(g, RectangleF.Inflate(r, -0.75f * s, -0.75f * s), radius - 0.75f * s, KitInk.Accent, 1.5f * s);
        }
    }
}
