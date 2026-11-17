using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Edits a draft only; the caller commits after OK. Cancel never mutates a task.
    sealed class TaskEditor : KitWindow
    {
        readonly KitText title = new KitText();
        readonly KitCheck hasDue = new KitCheck { Text = Lang.T("Tenggat") };
        readonly KitPopup date = new KitPopup();
        readonly KitPopup time = new KitPopup();
        readonly KitCheck star = new KitCheck { Text = Lang.T("Prioritas") };
        readonly KitCheck done = new KitCheck { Text = Lang.T("Selesai") };
        readonly Label warning = new Label { AutoSize = false };
        bool titleInvalid;
        readonly KitButton save = new KitButton { Text = Lang.T("Simpan"), Primary = true };
        readonly KitButton cancel = new KitButton { Text = Lang.T("Batal") };
        readonly Label titleLabel = new Label { Text = Lang.T("Judul tugas"), AutoSize = true };
        readonly Label listLabel = new Label { Text = Lang.T("Daftar"), AutoSize = true };
        readonly KitPopup listPick = new KitPopup();
        readonly Label notesLabel = new Label { Text = Lang.T("Catatan"), AutoSize = true };
        readonly TaskNoteEditor notesBox = new TaskNoteEditor();
        readonly KitButton blocks = new KitButton { Text = Lang.T("Blok") + " +" };
        readonly KitButton bold = new KitButton { Text = "B", AccessibleName = Lang.T("Format tebal") };
        readonly KitButton italic = new KitButton { Text = "I", AccessibleName = Lang.T("Format miring") };
        readonly string originalNotes, originalTitle, originalList;
        readonly DateTime? originalDue;
        readonly bool originalStar, originalDone;
        internal Func<TaskEditor, string> Commit;
        bool discardConfirmed;
        NoteBlockMenu blockMenu;
        bool layoutReady;

        // Longest list the date picker shows; rebuilt around the chosen month
        const int DaysShown = 60;
        DateTime chosen, firstDay;
        TimeSpan[] timeSlots;
        Font uiFont, fSmall;

        public string TaskTitle { get { return title.Text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(); } }
        public DateTime? Due { get { return hasDue.Checked ? (DateTime?)(chosen.Date + timeSlots[time.Selected]) : null; } }
        public bool Star { get { return star.Checked; } }
        public bool Done { get { return done.Checked; } }
        public string ListName { get { return listPick.Selected >= 0 && listPick.Items != null && listPick.Selected < listPick.Items.Length ? listPick.Items[listPick.Selected] : TaskLists.Default; } }
        public string Notes { get; private set; }
        internal bool NotesChanged { get { return Notes != originalNotes; } }
        internal bool TitleChanged { get { return TaskTitle != originalTitle; } }
        internal bool DueChanged { get { return Due != originalDue; } }
        internal bool StarChanged { get { return Star != originalStar; } }
        internal bool DoneChanged { get { return Done != originalDone; } }
        internal bool ListChanged { get { return !TaskLists.Same(ListName, originalList); } }

        public TaskEditor(TodoItem item) : this(item, null) { }

        public TaskEditor(TodoItem item, string initialList)
        {
            Text = Lang.T(item == null ? "Tugas baru" : "Edit tugas");
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            title.AccessibleName = Lang.T("Judul tugas");
            title.MaxLength = 2000;
            date.AccessibleName = Lang.T("Tanggal tenggat");
            time.AccessibleName = Lang.T("Jam tenggat");
            title.Text = item == null ? "" : item.Title;
            var fallback = DateTime.Today.AddDays(1);
            chosen = item != null && item.Due.HasValue ? item.Due.Value.Date : fallback;
            hasDue.Checked = item != null && item.Due.HasValue;
            star.Checked = item != null && item.Star; done.Checked = item != null && item.Done;
            var loadedLists = TaskLists.Load();
            var listNames = new System.Collections.Generic.List<string>();
            foreach (var l in loadedLists)
            {
                if (!string.IsNullOrEmpty(l.Name) && !listNames.Exists(x => TaskLists.Same(x, l.Name)))
                    listNames.Add(l.Name);
            }
            if (!listNames.Exists(x => TaskLists.Same(x, TaskLists.Default)))
                listNames.Insert(0, TaskLists.Default);
            string curList = item != null && !string.IsNullOrEmpty(item.ListName) ? item.ListName : (initialList ?? TaskLists.Default);
            int listIdx = listNames.FindIndex(p => TaskLists.Same(p, curList));
            if (listIdx < 0) { listNames.Add(curList); listIdx = listNames.Count - 1; }
            listPick.Items = listNames.ToArray();
            listPick.Selected = listIdx;
            Notes = originalNotes = item == null ? "" : (item.Notes ?? "");

            RefreshDates();
            var originalTime = item != null && item.Due.HasValue ? item.Due.Value.TimeOfDay : TimeSpan.FromHours(9);
            var slots = new System.Collections.Generic.List<TimeSpan>();
            for (int i = 0; i < 96; i++) slots.Add(TimeSpan.FromMinutes(i * 15));
            if (!slots.Contains(originalTime)) { slots.Add(originalTime); slots.Sort(); }
            timeSlots = slots.ToArray();
            time.Items = slots.ConvertAll(t => DateTime.Today.Add(t).ToString("HH:mm")).ToArray();
            time.Selected = slots.IndexOf(originalTime);
            originalTitle = TaskTitle;
            originalList = ListName;
            originalDue = Due;
            originalStar = Star;
            originalDone = Done;

            date.Changed += delegate(int i) { chosen = firstDay.AddDays(i); RefreshDates(); UpdateDue(); };
            time.Changed += delegate { UpdateDue(); };
            hasDue.CheckedChanged += delegate { UpdateDue(); };
            UpdateDue();
            title.TextChanged += delegate { if (titleInvalid && TaskTitle.Length > 0) { titleInvalid = false; UpdateDue(); } };
            save.Click += delegate { SaveDraft(); };
            cancel.Click += delegate { if (ConfirmDiscard()) { DialogResult = DialogResult.Cancel; Close(); } };
            notesBox.OpenBlockMenu = delegate { ShowBlocks(true); };
            notesBox.SaveTask = delegate { save.PerformClick(); };
            notesBox.CancelTask = delegate { if (blockMenu == null) cancel.PerformClick(); };
            blocks.Click += delegate { ShowBlocks(false); };
            var tips = new ToolTip();
            tips.SetToolTip(bold, Lang.T("Format tebal") + " (Ctrl+B)");
            tips.SetToolTip(italic, Lang.T("Format miring") + " (Ctrl+I)");
            tips.SetToolTip(blocks, Lang.T("Sisipkan blok") + " (/)");
            tips.SetToolTip(save, Lang.T("Simpan") + " (Ctrl+Enter)");
            Disposed += delegate { tips.Dispose(); };
            bold.Click += delegate { notesBox.ToggleBold(); };
            italic.Click += delegate { notesBox.ToggleItalic(); };
            Controls.AddRange(new Control[] { titleLabel, title, listLabel, listPick, hasDue, date, time, star, done,
                notesLabel, bold, italic, blocks, notesBox, warning, cancel, save });
            Shown += delegate { title.Focus(); title.SelectAll(); };
        }

        void SaveDraft()
        {
            if (TaskTitle.Length == 0)
            {
                titleInvalid = true;
                UpdateDue();
                title.Focus();
                return;
            }
            Notes = notesBox.SaveMarkdown(originalNotes);
            string error = Commit == null ? null : Commit(this);
            if (!string.IsNullOrEmpty(error))
            {
                warning.Text = error;
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        static string DayLabel(DateTime d) { return d.ToString("ddd, dd MMM", UiCulture); }
        static System.Globalization.CultureInfo UiCulture { get { return System.Globalization.CultureInfo.CurrentCulture; } }

        void RefreshDates()
        {
            // Rebuild the labels and their date mapping together.
            firstDay = chosen.AddDays(-7);
            var items = new string[DaysShown];
            for (int i = 0; i < DaysShown; i++) items[i] = DayLabel(firstDay.AddDays(i));
            date.Items = items;
            date.Selected = 7;
        }

        void UpdateDue()
        {
            date.Enabled = time.Enabled = hasDue.Checked;
            warning.Text = titleInvalid ? Lang.T("Judul wajib diisi")
                : hasDue.Checked && Due.Value <= DateTime.Now ? Lang.T("Tenggat sudah lewat") : "";
            warning.ForeColor = Ink.Base.Negative;
        }

        bool ConfirmDiscard()
        {
            if (discardConfirmed) return true;
            bool changed = TitleChanged || DueChanged || StarChanged || DoneChanged || ListChanged
                || notesBox.SaveMarkdown(originalNotes) != originalNotes;
            if (!changed) return true;
            discardConfirmed = KitAsk.Confirm(this, Lang.T("Buang perubahan tugas?"), Lang.T("Buang"), Lang.T("Lanjut mengedit"));
            return discardConfirmed;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.CloseReason == CloseReason.UserClosing && DialogResult != DialogResult.OK && !e.Cancel)
                e.Cancel = !ConfirmDiscard();
        }

        void ShowBlocks(bool fromSlash)
        {
            if (blockMenu != null || !notesBox.Visible) return;
            var menu = new NoteBlockMenu(Ui, delegate(string prefix)
            {
                if (!IsDisposed) notesBox.ApplyBlock(prefix);
            }, true);
            blockMenu = menu;
            menu.Closed += delegate(object sender, ToolStripDropDownClosedEventArgs e)
            {
                blockMenu = null;
                if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard) notesBox.FocusEditor();
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(menu.Dispose));
                else menu.Dispose();
            };
            if (fromSlash) menu.Show(notesBox, notesBox.CaretLocation());
            else menu.Show(blocks, new Point(0, blocks.Height + 3));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!layoutReady)
            {
                Rectangle area = Screen.FromHandle(Handle).WorkingArea;
                int borderWidth = Width - ClientSize.Width, borderHeight = Height - ClientSize.Height;
                MinimumSize = new Size(Math.Min(area.Width, (int)(520 * Ui) + borderWidth),
                    Math.Min(area.Height, (int)(410 * Ui) + borderHeight));
                ClientSize = new Size(Math.Min(area.Width - borderWidth, (int)(800 * Ui)),
                    Math.Min(area.Height - borderHeight, (int)(695 * Ui)));
                layoutReady = true;
            }
            Relayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!layoutReady) return;
            LayoutFields();
        }

        public override void Relayout()
        {
            if (!layoutReady) return;
            if (uiFont == null) uiFont = UiFont(14);
            if (fSmall == null) fSmall = UiFont(13);
            Font = uiFont;
            BackColor = KitInk.Window;
            ForeColor = KitInk.Text;
            titleLabel.Font = listLabel.Font = notesLabel.Font = fSmall;
            listPick.Font = date.Font = time.Font = star.Font = done.Font = hasDue.Font = fSmall;
            title.BackColor = KitInk.Group;
            title.ForeColor = KitInk.Text;
            warning.ForeColor = Ink.Base.Negative;
            LayoutFields();
            notesBox.SetTheme(KitInk.Group, KitInk.Text, KitInk.Sub, KitInk.Hover);
            notesBox.LoadMarkdown(originalNotes);
        }

        void LayoutFields()
        {
            int margin = Px(20), gap = Px(8);
            int width = Math.Max(1, Math.Min(Px(1120), ClientSize.Width - 2 * margin));
            margin = Math.Max(margin, (ClientSize.Width - width) / 2);
            int right = margin + width;
            int col = (width - 2 * gap) / 3;
            int y = Px(15);
            titleLabel.Location = new Point(margin, y);
            title.SetBounds(margin, y + Px(23), width, Px(31));
            y = Px(77);
            listLabel.Location = new Point(margin, y + Px(4));
            hasDue.SetBounds(margin + col + gap, y, col, Px(25));
            y += Px(30);
            listPick.SetBounds(margin, y, col, Px(30));
            date.SetBounds(margin + col + gap, y, col, Px(30));
            time.SetBounds(margin + 2 * (col + gap), y, col, Px(30));
            star.SetBounds(margin, y + Px(36), col, Px(27));
            done.SetBounds(margin + col + gap, y + Px(36), col, Px(27));
            y = Px(185);
            notesLabel.Location = new Point(margin, y + Px(6));
            blocks.SetBounds(right - Px(86), y, Px(86), Px(29));
            italic.SetBounds(blocks.Left - gap - Px(34), y, Px(34), Px(29));
            bold.SetBounds(italic.Left - gap - Px(34), y, Px(34), Px(29));
            int footer = ClientSize.Height - Px(51);
            int editorTop = y + Px(37);
            notesBox.SetBounds(margin, editorTop, width, Math.Max(1, footer - editorTop - Px(14)));
            save.SetBounds(right - Px(100), footer, Px(100), Px(34));
            cancel.SetBounds(save.Left - gap - Px(100), footer, Px(100), Px(34));
            warning.SetBounds(margin, footer, Math.Max(1, cancel.Left - margin - gap), Px(38));
            warning.TextAlign = ContentAlignment.MiddleLeft;
        }

        int Px(int value) { return (int)Math.Round(value * Ui); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == (Keys.Control | Keys.Enter)) { save.PerformClick(); return true; }
            if (keys == Keys.Escape && blockMenu == null) { cancel.PerformClick(); return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (blockMenu != null) blockMenu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                if (uiFont != null) uiFont.Dispose();
                if (fSmall != null) fSmall.Dispose();
            }
        }
    }
}
