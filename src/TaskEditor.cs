using System;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // Edits a draft only; the caller commits after OK. Cancel never mutates a task.
    sealed class TaskEditor : KitWindow
    {
        readonly KitText title = new KitText();
        readonly KitCheck hasDue = new KitCheck { Text = "Tenggat" };
        readonly KitPopup date = new KitPopup();
        readonly KitPopup time = new KitPopup();
        readonly KitCheck star = new KitCheck { Text = "Prioritas" };
        readonly KitCheck done = new KitCheck { Text = "Selesai" };
        readonly Label warning = new Label { AutoSize = false };
        bool titleInvalid;
        readonly KitButton save = new KitButton { Text = "Simpan", Primary = true };
        readonly KitButton cancel = new KitButton { Text = "Batal" };
        readonly Label titleLabel = new Label { Text = "Judul tugas", AutoSize = true };
        readonly Label listLabel = new Label { Text = Lang.T("Daftar"), AutoSize = true };
        readonly KitPopup listPick = new KitPopup();
        readonly Label notesLabel = new Label { Text = Lang.T("Catatan"), AutoSize = true };
        readonly KitText notesBox = new KitText { Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.Vertical };

        // Longest list the date picker shows; rebuilt around the chosen month
        const int DaysShown = 60;
        DateTime chosen;
        Font uiFont, fSmall;

        public string TaskTitle { get { return title.Text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(); } }
        public DateTime? Due { get { return hasDue.Checked ? (DateTime?)(chosen.Date + TimeSpan.FromMinutes(time.Selected * 15)) : null; } }
        public bool Star { get { return star.Checked; } }
        public bool Done { get { return done.Checked; } }
        public string ListName { get { return listPick.Selected >= 0 && listPick.Items != null && listPick.Selected < listPick.Items.Length ? listPick.Items[listPick.Selected] : TaskLists.Default; } }
        public string Notes { get { return notesBox.Text ?? ""; } }

        public TaskEditor(TodoItem item) : this(item, null) { }

        public TaskEditor(TodoItem item, string initialList)
        {
            Text = item == null ? "Tugas baru" : "Edit tugas";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            title.AccessibleName = "Judul tugas";
            title.MaxLength = 2000;
            date.AccessibleName = "Tanggal tenggat";
            time.AccessibleName = "Jam tenggat";
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
            notesBox.Text = item != null && item.Notes != null ? item.Notes : "";

            date.Items = DateItems();
            date.Selected = Math.Max(0, Math.Min(DaysShown - 1, 7));
            time.Items = TimeItems();
            int slot = item != null && item.Due.HasValue ? ((item.Due.Value.Hour * 60 + item.Due.Value.Minute) / 15) : (9 * 4);
            time.Selected = Math.Max(0, Math.Min(95, slot));
            // A new list keeps the chosen day; re-picking only recentres it with the same items
            date.Changed += delegate(int i) { chosen = DayFrom(i); UpdateDue(); };
            time.Changed += delegate(int i) { UpdateDue(); };

            hasDue.CheckedChanged += delegate { UpdateDue(); };
            date.Enabled = time.Enabled = hasDue.Checked;
            UpdateDue();
            title.TextChanged += delegate { if (titleInvalid && TaskTitle.Length > 0) { titleInvalid = false; warning.Text = hasDue.Checked && Due.HasValue && Due.Value <= DateTime.Now ? "Tenggat sudah lewat" : ""; } };
            save.Click += delegate
            {
                if (TaskTitle.Length == 0) { titleInvalid = true; warning.Text = "Judul wajib diisi"; title.Focus(); return; }
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.AddRange(new Control[] { titleLabel, title, listLabel, listPick, hasDue, date, time, star, done, notesLabel, notesBox, warning, save, cancel });
            Shown += delegate { title.Focus(); title.SelectAll(); };
        }

        static string DayLabel(DateTime d) { return d.ToString("ddd, dd MMM", UiCulture); }
        static System.Globalization.CultureInfo UiCulture { get { return System.Globalization.CultureInfo.CurrentCulture; } }

        string[] DateItems()
        {
            var items = new string[DaysShown];
            for (int i = 0; i < DaysShown; i++) items[i] = DayLabel(chosen.AddDays(i - 7));
            return items;
        }

        DateTime DayFrom(int i)
        {
            if (i < 0 || i >= DaysShown) return chosen;
            return chosen.AddDays(i - 7);
        }

        string[] TimeItems()
        {
            var items = new string[96];
            for (int i = 0; i < 96; i++) items[i] = (i / 4).ToString("00") + ":" + (i % 4 * 15).ToString("00");
            return items;
        }

        void UpdateDue()
        {
            date.Enabled = time.Enabled = hasDue.Checked;
            if (hasDue.Checked && Due.Value <= DateTime.Now) warning.Text = "Tenggat sudah lewat";
            else if (warning.Text == "Judul wajib diisi" && titleInvalid) return;
            else if (warning.Text == "Tenggat sudah lewat") warning.Text = "";
            date.AccessibleDescription = "Tanggal tenggat";
            time.AccessibleDescription = "Jam tenggat lokal";
            warning.ForeColor = Ink.Base.Negative;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Relayout(); }

        public override void Relayout()
        {
            if (!IsHandleCreated) return;
            if (uiFont == null) uiFont = UiFont(14);
            Font = uiFont; BackColor = KitInk.Window; ForeColor = KitInk.Text;
            float s = Ui;
            titleLabel.Font = listLabel.Font = notesLabel.Font = UiFont(12.5f);
            titleLabel.SetBounds((int)(20*s), (int)(18*s), (int)(430*s), (int)(22*s));
            title.SetBounds((int)(20*s), (int)(44*s), (int)(440*s), (int)(30*s));
            title.BackColor = KitInk.Group; title.ForeColor = KitInk.Text;
            if (fSmall == null) fSmall = UiFont(12.5f);
            listPick.Font = date.Font = time.Font = star.Font = done.Font = hasDue.Font = fSmall;
            listLabel.SetBounds((int)(20*s), (int)(84*s), (int)(120*s), (int)(22*s));
            listPick.SetBounds((int)(20*s), (int)(108*s), (int)(220*s), (int)(30*s));
            hasDue.SetBounds((int)(20*s), (int)(150*s), (int)(140*s), (int)(28*s));
            date.SetBounds((int)(20*s), (int)(182*s), (int)(258*s), (int)(30*s));
            time.SetBounds((int)(288*s), (int)(182*s), (int)(172*s), (int)(30*s));
            star.SetBounds((int)(20*s), (int)(222*s), (int)(140*s), (int)(28*s));
            done.SetBounds((int)(170*s), (int)(222*s), (int)(140*s), (int)(28*s));
            notesLabel.SetBounds((int)(20*s), (int)(258*s), (int)(200*s), (int)(22*s));
            notesBox.Font = fSmall; notesBox.BackColor = KitInk.Group; notesBox.ForeColor = KitInk.Text;
            notesBox.SetBounds((int)(20*s), (int)(282*s), (int)(440*s), (int)(76*s));
            warning.SetBounds((int)(20*s), (int)(364*s), (int)(440*s), (int)(26*s)); warning.ForeColor = Ink.Base.Negative;
            cancel.SetBounds((int)(250*s), (int)(396*s), (int)(100*s), (int)(34*s));
            save.SetBounds((int)(360*s), (int)(396*s), (int)(100*s), (int)(34*s));
            ClientSize = new Size((int)(480*s), (int)(446*s));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == (Keys.Control | Keys.Enter)) { save.PerformClick(); return true; }
            return base.ProcessCmdKey(ref msg, keys);
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { if (uiFont != null) uiFont.Dispose(); if (fSmall != null) fSmall.Dispose(); } }
    }
}
