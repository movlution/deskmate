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
        readonly KitPopup done = new KitPopup { AccessibleName = Lang.T("Status") };
        readonly Label statusLabel = new Label { Text = Lang.T("Status"), AutoSize = true };
        readonly Label dueLabel = new Label { Text = Lang.T("Tenggat"), AutoSize = true };
        readonly Label priorityLabel = new Label { Text = Lang.T("Prioritas"), AutoSize = true };
        readonly Panel titleFrame = new Panel { BorderStyle = BorderStyle.FixedSingle };
        readonly Panel noteFrame = new Panel { BorderStyle = BorderStyle.FixedSingle };
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
        // Task fields, a shared notes toolbar, and a fixed action footer
        readonly Panel ribbon = new Panel(), canvas = new Panel(), page = new Panel(), footerBar = new Panel();
        readonly Label ribbonLine = new Label { AutoSize = false }, footerLine = new Label { AutoSize = false };
        readonly Label sep1 = new Label { AutoSize = false }, sep2 = new Label { AutoSize = false };
        readonly KitButton addImage = new KitButton { Text = Lang.T("Tambah gambar") };
        readonly KitButton addTable = new KitButton { Text = Lang.T("Tambah tabel") };
        readonly KitButton tableMenu = new KitButton { Text = Lang.T("Atur tabel ▾"), Enabled = false };
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
        Font uiFont, fSmall, fTitle;

        public string TaskTitle { get { return title.Text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(); } }
        public DateTime? Due { get { return hasDue.Checked ? (DateTime?)(chosen.Date + timeSlots[time.Selected]) : null; } }
        public bool Star { get { return star.Checked; } }
        public bool Done { get { return done.Selected == 1; } }
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
            star.Checked = item != null && item.Star;
            done.Items = new[] { Lang.English ? "Not completed" : "Belum selesai", Lang.T("Selesai") };
            done.Selected = item != null && item.Done ? 1 : 0;
            listPick.AccessibleName = Lang.T("Daftar");
            hasDue.AccessibleName = Lang.T("Tenggat");
            hasDue.Text = "";
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
            addImage.Click += delegate { notesBox.PickImage(); };
            addTable.Click += delegate { notesBox.PickTable(); };
            tableMenu.Click += delegate { notesBox.OpenTableMenu(tableMenu); };
            notesBox.TableFocusChanged = delegate(bool inTable) { tableMenu.Enabled = inTable; };
            notesBox.Notice = delegate(string text) { warning.Text = text; };
            tips.SetToolTip(tableMenu, Lang.T("Klik sel untuk mengatur tabel"));
            title.BorderStyle = BorderStyle.None;
            titleLabel.Visible = false;
            titleFrame.Controls.Add(title);
            noteFrame.Controls.AddRange(new Control[] { ribbon, notesBox });
            ribbon.Controls.AddRange(new Control[] { bold, italic, sep1, blocks, sep2, addImage, addTable, tableMenu, ribbonLine });
            page.Controls.AddRange(new Control[] { titleLabel, titleFrame, listLabel, listPick, statusLabel, done, dueLabel, hasDue, date, time,
                priorityLabel, star, notesLabel, noteFrame });
            canvas.Controls.Add(page);
            footerBar.Controls.AddRange(new Control[] { footerLine, warning, cancel, save });
            Controls.AddRange(new Control[] { canvas, footerBar });
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
                MinimumSize = new Size(Math.Min(area.Width, (int)(680 * Ui) + borderWidth),
                    Math.Min(area.Height, (int)(540 * Ui) + borderHeight));
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
            if (fTitle == null) fTitle = new Font(Ink.MenuFamily, 24 * Ui, FontStyle.Bold, GraphicsUnit.Pixel);
            Font = uiFont;
            BackColor = KitInk.Window;
            ForeColor = KitInk.Text;
            titleLabel.Font = listLabel.Font = notesLabel.Font = statusLabel.Font = dueLabel.Font = priorityLabel.Font = uiFont;
            listPick.Font = date.Font = time.Font = star.Font = done.Font = hasDue.Font = fSmall;
            bold.Font = italic.Font = blocks.Font = fSmall;
            // Title reads as the page heading, not a form field
            addImage.Font = addTable.Font = tableMenu.Font = fSmall;
            title.Font = fTitle;
            // Grey canvas, raised sheet: the sheet is the only bright surface
            ribbon.BackColor = footerBar.BackColor = KitInk.Window;
            canvas.BackColor = page.BackColor = KitInk.Window;
            titleFrame.BackColor = noteFrame.BackColor = KitInk.Group;
            title.BackColor = KitInk.Group;
            title.ForeColor = KitInk.Text;
            listLabel.BackColor = notesLabel.BackColor = KitInk.Group;
            listLabel.ForeColor = notesLabel.ForeColor = KitInk.Sub;
            warning.BackColor = KitInk.Window;
            ribbonLine.BackColor = footerLine.BackColor = sep1.BackColor = sep2.BackColor = KitInk.Line;
            warning.ForeColor = Ink.Base.Negative;
            LayoutFields();
            // Body sits on the page itself, like a document
            notesBox.SetTheme(KitInk.Group, KitInk.Text, KitInk.Sub, KitInk.Hover);
            notesBox.LoadMarkdown(originalNotes);
        }

        void LayoutFields()
        {
            int w = ClientSize.Width, h = ClientSize.Height, margin = Px(18), gap = Px(8);
            int width = Math.Max(1, w - margin * 2), right = margin + width;
            int line = Math.Max(1, Px(1)), foot = Px(54), footTop = Math.Max(0, h - foot);
            canvas.SetBounds(0, 0, w, footTop);
            page.SetBounds(0, 0, w, footTop);
            footerBar.SetBounds(0, footTop, w, foot);
            titleFrame.SetBounds(margin, Px(18), width, Px(46));
            title.SetBounds(Px(13), (titleFrame.ClientSize.Height - title.PreferredHeight) / 2,
                Math.Max(1, titleFrame.ClientSize.Width - Px(26)), title.PreferredHeight);

            int gutter = Px(42), col = Math.Max(1, (width - gutter) / 2), x2 = right - col;
            int row = Px(34), y = Px(80);
            listLabel.Location = new Point(margin, y);
            statusLabel.Location = new Point(x2, y);
            listPick.SetBounds(margin, y + Px(21), col, row);
            done.SetBounds(x2, y + Px(21), col, row);
            y += Px(70);
            dueLabel.Location = new Point(margin, y);
            priorityLabel.Location = new Point(x2, y);
            y += Px(21);
            int check = Px(26), timeW = Math.Min(Px(100), col / 3);
            hasDue.SetBounds(margin, y, check, row);
            date.SetBounds(margin + check + gap, y, Math.Max(1, col - check - timeW - 2 * gap), row);
            time.SetBounds(margin + col - timeW, y, timeW, row);
            star.SetBounds(x2, y, col, row);

            y += row + Px(18);
            notesLabel.Location = new Point(margin, y);
            y += Px(23);
            noteFrame.SetBounds(margin, y, width, Math.Max(1, footTop - y - margin));
            int rw = noteFrame.ClientSize.Width, btn = Px(30), inset = Px(8), x = inset, top = inset;
            foreach (Control tool in new Control[] { bold, italic, sep1, blocks, addImage, addTable, sep2, tableMenu })
            {
                bool separator = tool == sep1 || tool == sep2;
                int tw = separator ? line : tool == bold || tool == italic ? btn : Fit(tool);
                if (x > inset && x + tw > rw - inset) { x = inset; top += btn + Px(4); }
                tool.SetBounds(x, top + (separator ? Px(5) : 0), tw, separator ? btn - Px(10) : btn);
                x += tw + Px(5);
            }
            int ribbonH = top + btn + inset;
            ribbon.SetBounds(0, 0, rw, ribbonH);
            ribbonLine.SetBounds(0, ribbonH - line, rw, line);
            notesBox.SetBounds(0, ribbonH, rw, Math.Max(1, noteFrame.ClientSize.Height - ribbonH));

            footerLine.SetBounds(0, 0, w, line);
            int fy = (foot - row) / 2;
            save.SetBounds(right - Px(96), fy, Px(96), row);
            cancel.SetBounds(save.Left - gap - Px(82), fy, Px(82), row);
            warning.SetBounds(margin, fy, Math.Max(1, cancel.Left - margin - gap), row);
            warning.TextAlign = ContentAlignment.MiddleLeft;
        }

        int Fit(Control c) { return TextRenderer.MeasureText(c.Text, fSmall ?? Font).Width + Px(18); }

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
                if (fTitle != null) fTitle.Dispose();
            }
        }
    }
}
