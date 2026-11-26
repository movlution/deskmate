using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Deskmate
{
    sealed partial class WorkWindow : KitWindow
    {
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
                Key = "today",
                Text = Lang.T("Hari ini"),
                Glyph = '\uE706',
                Count = TaskTree.CountWhere(todos, TaskTree.DueToday)
            });
            railItems.Add(new RailItem
            {
                Kind = RailItem.View,
                Key = "planned",
                Text = Lang.T("Terjadwal"),
                Glyph = '\uE787',
                Count = TaskTree.CountWhere(todos, TaskTree.Planned)
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

            if (!IsViewKey(activeRailKey))
            {
                if (TaskLists.Find(lists, activeRailKey) == null)
                    activeRailKey = TaskLists.Default;
            }

            bool starred = activeRailKey == "starred";
            string listName = IsViewKey(activeRailKey) ? null : activeRailKey;
            var filter = ViewFilter();
            bool searching = taskSearchOn && taskSearch.Text.Trim().Length > 0;

            listOrder.Clear();
            foreach (var l in lists) listOrder.Add(l.Name);

            // Planned reads as a timeline, whatever the list sort is
            string sort = activeRailKey == "planned" || activeRailKey == "today" ? TaskTree.SortDate : currentSort;
            var openRows = TaskTree.OpenWhere(todos, listName == null, filter, sort, listOrder, collapsed);
            var doneItems = TaskTree.DoneWhere(todos, filter);

            string emptyTitle, emptySub;
            if (searching)
            {
                emptyTitle = Lang.T("Tidak ada tugas yang cocok");
                emptySub = Lang.T("Coba kata kunci lain");
            }
            else if (activeRailKey == "today" && doneItems.Count == 0)
            {
                emptyTitle = Lang.T("Tidak ada tugas untuk hari ini");
                emptySub = Lang.T("Tugas bertenggat hari ini atau terlambat muncul di sini");
            }
            else if (activeRailKey == "planned" && doneItems.Count == 0)
            {
                emptyTitle = Lang.T("Belum ada tugas terjadwal");
                emptySub = Lang.T("Beri tenggat, mis. \"Kirim laporan @besok 10:00\"");
            }
            else if (starred)
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
            if (BoardOn) RefillBoard(todos, lists, listName, filter, keepId);

            if (activeRailKey == "all") title.Title = Lang.T("Semua tugas");
            else if (activeRailKey == "starred") title.Title = Lang.T("Berbintang");
            else if (activeRailKey == "today") title.Title = Lang.T("Hari ini");
            else if (activeRailKey == "planned") title.Title = Lang.T("Terjadwal");
            else title.Title = activeRailKey;

            int openCount = TaskTree.CountWhere(todos, filter);
            if (searching)
                title.Subtitle = openCount == 0 ? Lang.T("Tidak ada hasil") : Lang.F("{0} hasil", openCount);
            else
            {
                title.Subtitle = openCount == 0 ? Lang.T("Tidak ada tugas aktif") : (openCount == 1 ? "1 " + Lang.T("tugas aktif") : openCount + " " + Lang.T("tugas aktif"));
                if (activeRailKey == "today") title.Subtitle = DateTime.Today.ToString("dddd, d MMMM", CultureInfo.CurrentCulture) + "  \u00B7  " + title.Subtitle;
            }
            title.Invalidate();

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

        bool Changed(string keepId, bool showFailure = true)
        {
            if (owner == null) return false;
            if (owner.todos == null) owner.EnsureWorkData();
            bool saved = owner.TodosChanged();
            if (saved) savedTasks = CopyTasks(owner.todos);
            else
            {
                if (savedTasks != null) owner.todos = CopyTasks(savedTasks);
                if (showFailure) KitAsk.Notice(this, Lang.T("Tugas gagal disimpan. Perubahan dibatalkan."));
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
            string prev = "";
            foreach (var raw in text.Substring(at + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = raw.ToLowerInvariant();
                DateTime d; TimeSpan t; int wd;
                if (w == "besok" || w == "tomorrow") date = DateTime.Today.AddDays(1);
                else if (w == "lusa") date = DateTime.Today.AddDays(2);
                else if (w == "hari" || w == "hariini" || w == "today") date = DateTime.Today;
                // "minggu depan" / "next week": Monday of next week
                else if ((w == "depan" && prev == "minggu") || (w == "week" && prev == "next")) date = NextWeekday(DayOfWeek.Monday);
                else if (w == "next") { }
                else if ((wd = Weekday(w)) >= 0) date = NextWeekday((DayOfWeek)wd);
                else if (DateTime.TryParseExact(w, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) date = d.Date;
                else if (DateTime.TryParseExact(w, new[] { "d/M", "d/M/yyyy", "d-M-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                {
                    date = d.Date;
                    // A day and month without a year that already passed means next year
                    if (w.Split('/', '-').Length == 2 && date.Value < DateTime.Today) date = date.Value.AddYears(1);
                }
                else if (TimeSpan.TryParseExact(w.Replace('.', ':'), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out t)) time = t;
                else if (w.StartsWith("jam", StringComparison.Ordinal) || w == "pukul" || w == "at") { }
                else return true;
                prev = w;
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

        static readonly string[] DayWords = { "minggu", "senin", "selasa", "rabu", "kamis", "jumat", "sabtu" };
        static readonly string[] DayWordsEn = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

        static int Weekday(string w)
        {
            if (w == "jum'at") w = "jumat";
            for (int i = 0; i < 7; i++)
                if (w == DayWords[i] || w == DayWordsEn[i] || (w.Length == 3 && DayWordsEn[i].StartsWith(w, StringComparison.Ordinal))) return i;
            return -1;
        }

        // The next such day after today; today itself is "hari ini"
        static DateTime NextWeekday(DayOfWeek day)
        {
            int ahead = ((int)day - (int)DateTime.Today.DayOfWeek + 7) % 7;
            return DateTime.Today.AddDays(ahead == 0 ? 7 : ahead);
        }

        static bool IsViewKey(string key) { return key == "all" || key == "starred" || key == "today" || key == "planned"; }

        Predicate<TodoItem> ViewFilter()
        {
            string key = activeRailKey;
            string query = taskSearchOn ? taskSearch.Text.Trim() : "";
            Predicate<TodoItem> view;
            if (key == "starred") view = t => t.Star;
            else if (key == "today") view = TaskTree.DueToday;
            else if (key == "planned") view = TaskTree.Planned;
            else if (key == "all") view = null;
            else view = t => TaskLists.Same(TaskLists.NameOf(t), key);
            if (query.Length == 0) return view ?? (t => true);
            return t => (view == null || view(t)) && TaskTree.Matches(t, query);
        }

        string AddPlaceholder()
        {
            if (activeRailKey == "today") return Lang.T("Tambah tugas untuk hari ini…");
            return Lang.T("Tambah tugas… (mis. Rapat @besok 10:00)");
        }

        void UpdateAddHint()
        {
            string titleStr; DateTime? due;
            string text = addBox.Text.Replace('\r', ' ').Replace('\n', ' ');
            ParseDue(text, out titleStr, out due);
            string hint = due.HasValue ? TaskInk.DueText(due.Value) : "";
            if (hint == addHint) return;
            addHint = hint;
            float s = Ui;
            addHintW = hint.Length == 0 ? 0 : TextRenderer.MeasureText(hint, Ink.Small(11.5f * s), new Size(int.MaxValue, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + (int)(44 * s);
            addBox.Width = Math.Max(40, wellAdd.Width - (int)(52 * s) - addHintW);
            Invalidate(Rectangle.Inflate(wellAdd, 2, 2));
        }

        void OpenTaskSearch()
        {
            if (Tab != 0 || BoardOn) return;
            if (!taskSearchOn)
            {
                taskSearchOn = true;
                ApplyTab();
            }
            taskSearch.Focus();
            taskSearch.SelectAll();
        }

        void CloseTaskSearch()
        {
            if (!taskSearchOn) return;
            taskSearchOn = false;
            taskSearch.Text = "";
            ApplyTab();
            RefillTasks(null);
            taskView.PlayEnter();
            addBox.Focus();
        }

        // ---------- completion with a beat, and undo ----------

        void BeginToggleDone(TodoItem t)
        {
            if (t == null) return;
            if (pendingDone.Contains(t.Id))
            {
                // A second click inside the beat takes it back
                pendingDone.Remove(t.Id);
                taskView.MarkCompleting(t.Id, false);
                taskView.StartCheck(t, true);
                if (pendingDone.Count == 0) completeTimer.Stop();
                return;
            }
            if (t.Done || Anim.Reduced || !taskView.Visible)
            {
                taskView.StartCheck(t, t.Done);
                ToggleTaskDone(t);
                return;
            }
            pendingDone.Add(t.Id);
            taskView.MarkCompleting(t.Id, true);
            completeTimer.Stop();
            completeTimer.Interval = (int)(420 * Anim.Pace);
            completeTimer.Start();
        }

        void FlushCompletions()
        {
            completeTimer.Stop();
            if (pendingDone.Count == 0) return;
            var ids = new List<string>(pendingDone);
            pendingDone.Clear();
            var done = new List<TodoItem>();
            foreach (string id in ids)
            {
                var t = FindTask(id);
                if (t == null || t.Done) continue;
                t.Done = true;
                t.DoneAt = DateTime.Now;
                t.UpdatedUtc = DateTime.UtcNow;
                t.Dirty = true;
                done.Add(t);
            }
            foreach (string id in ids) taskView.MarkCompleting(id, false);
            if (done.Count == 0) { RefillTasks(null); return; }
            if (Changed(null)) ToastDone(done);
        }

        void ToastDone(List<TodoItem> done)
        {
            var ids = done.ConvertAll(x => x.Id);
            string msg = done.Count == 1 ? Lang.T("Tugas selesai") : Lang.F("{0} tugas selesai", done.Count);
            ShowToast(msg, delegate
            {
                string keep = null;
                foreach (string id in ids)
                {
                    var t = FindTask(id);
                    if (t == null || !t.Done) continue;
                    t.Done = false;
                    t.DoneAt = null;
                    t.UpdatedUtc = DateTime.UtcNow;
                    t.Dirty = true;
                    keep = t.Id;
                }
                if (keep != null) Changed(keep);
            });
        }

        void ShowToast(string message, Action undo)
        {
            toast.Show(message, Lang.T("Urungkan"), undo);
        }

        // Puts deleted tasks back. A Google delete still queued is cancelled; one already sent
        // means the task is created again instead of pointing at a removed remote task
        void RestoreTasks(List<TodoItem> items, Dictionary<string, string> parents)
        {
            if (owner == null || items == null || items.Count == 0) return;
            if (owner.todos == null) owner.EnsureWorkData();
            var queue = TodoFile.LoadDeletedGoogle();
            bool queueChanged = false;
            foreach (var t in items)
            {
                if (owner.todos.Exists(x => x != null && x.Id == t.Id)) continue;
                if (!string.IsNullOrEmpty(t.GoogleTaskId))
                {
                    int at = queue.FindIndex(q => q[1] == t.GoogleTaskId);
                    if (at >= 0) { queue.RemoveAt(at); queueChanged = true; }
                    else { t.GoogleTaskId = ""; t.GoogleListId = ""; }
                }
                t.UpdatedUtc = DateTime.UtcNow;
                t.Dirty = true;
                t.Moved = true;
                owner.todos.Add(t);
            }
            if (parents != null)
                foreach (var pair in parents)
                {
                    var child = FindTask(pair.Key);
                    if (child != null && string.IsNullOrEmpty(child.ParentId) && FindTask(pair.Value) != null)
                    {
                        child.ParentId = pair.Value;
                        child.Moved = true; child.Dirty = true; child.UpdatedUtc = DateTime.UtcNow;
                    }
                }
            if (queueChanged) TodoFile.SaveDeletedGoogle(queue);
            Changed(items[0].Id);
        }

        void AddTask()
        {
            if (owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            string text = addBox.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length == 0) return;
            string titleStr; DateTime? due;
            ParseDue(text, out titleStr, out due);
            string targetList = IsViewKey(activeRailKey) ? TaskLists.Default : activeRailKey;
            // Added from Today or Planned: due today, so it stays in the view it was typed into
            if (!due.HasValue && (activeRailKey == "today" || activeRailKey == "planned")) due = DateTime.Today;
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
            taskView.Select(item.Id);
            addBox.Focus();
        }

        void EditTask(TodoItem item) { EditTask(item, null); }

        TaskEditor taskEditor;

        void EditTask(TodoItem item, string initialList)
        {
            if (owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            if (taskEditor != null && !taskEditor.IsDisposed) { taskEditor.Activate(); return; }
            var dlg = new TaskEditor(item, initialList);
            taskEditor = dlg;
            string id = item == null ? null : item.Id;
            string originalNotes = item == null ? "" : item.Notes ?? "";
            dlg.FormClosed += delegate { taskEditor = null; };
            dlg.Commit = delegate(TaskEditor draft)
            {
                if (IsDisposed || owner == null) return Lang.T("Buka ulang panel tugas.");
                var t = id == null ? new TodoItem() : FindTask(id);
                if (t == null) return Lang.T("Tugas sudah dihapus.");
                string currentNotes = t.Notes ?? "";
                if (id != null && draft.NotesChanged && currentNotes != originalNotes && currentNotes != draft.Notes)
                {
                    if (!KitAsk.Confirm(dlg, Lang.T("Catatan berubah saat sinkronisasi. Simpan versi Anda?"), "Simpan", "Lanjut mengedit"))
                        return Lang.T("Perubahan belum disimpan.");
                    // The dialog pumps UI messages, which can apply another sync result.
                    t = FindTask(id);
                    if (t == null) return Lang.T("Tugas sudah dihapus.");
                    if ((t.Notes ?? "") != currentNotes && (t.Notes ?? "") != draft.Notes)
                        return Lang.T("Perubahan belum disimpan.");
                }

                if (id == null || draft.TitleChanged) t.Title = draft.TaskTitle;
                if (id == null || draft.DueChanged) t.Due = draft.Due;
                if (id == null || draft.StarChanged) t.Star = draft.Star;
                if (id == null || draft.DoneChanged)
                {
                    if (t.Done != draft.Done) t.DoneAt = draft.Done ? (DateTime?)DateTime.Now : null;
                    t.Done = draft.Done;
                }
                if ((id == null || draft.ListChanged) && !TaskLists.Same(t.ListName, draft.ListName))
                {
                    t.ListName = draft.ListName;
                    t.ParentId = "";
                    t.Moved = true;
                }
                if (id == null || draft.NotesChanged) t.Notes = draft.Notes;
                if (id != null && !draft.TitleChanged && !draft.DueChanged && !draft.StarChanged
                    && !draft.DoneChanged && !draft.ListChanged && !draft.NotesChanged) return null;

                t.UpdatedUtc = DateTime.UtcNow;
                t.Dirty = true;
                if (id == null)
                {
                    t.Position = TaskTree.TopPosition(owner.todos, t.ListName, null);
                    owner.todos.Add(t);
                }
                return Changed(t.Id, false) ? null : Lang.T("Gagal menyimpan. Coba lagi.");
            };
            dlg.Show(this);
        }

        // No confirmation: the toast offers undo, as Google Tasks and To Do do
        void DeleteTask(TodoItem t)
        {
            if (t == null || owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            pendingDone.Remove(t.Id);
            var gone = TaskTree.Subtree(owner.todos, t);
            TaskTree.Delete(owner.todos, t);
            if (!Changed(null)) return;
            string msg = gone.Count > 1 ? Lang.F("Tugas dan {0} subtugas dihapus", gone.Count - 1) : Lang.T("Tugas dihapus");
            ShowToast(msg, delegate { RestoreTasks(gone, null); });
        }

        void ClearDone()
        {
            if (owner == null) return;
            if (owner.todos == null) owner.EnsureWorkData();
            // Only the completed tasks this view shows: Starred clears starred ones, not every list
            var done = TaskTree.DoneWhere(owner.todos, ViewFilter());
            if (done.Count == 0) return;
            var ids = new HashSet<string>();
            foreach (var t in done) ids.Add(t.Id);
            var parents = new Dictionary<string, string>();
            foreach (var t in owner.todos)
                if (t != null && !ids.Contains(t.Id) && !string.IsNullOrEmpty(t.ParentId) && ids.Contains(t.ParentId)) parents[t.Id] = t.ParentId;
            foreach (var t in done)
            {
                if (!string.IsNullOrEmpty(t.GoogleTaskId)) TodoFile.AddDeletedGoogle(t.GoogleListId, t.GoogleTaskId);
                owner.todos.Remove(t);
            }
            // Open subtasks of a removed parent move up a level instead of dangling
            foreach (var pair in parents)
            {
                var child = FindTask(pair.Key);
                if (child == null) continue;
                child.ParentId = ""; child.Moved = true; child.Dirty = true; child.UpdatedUtc = DateTime.UtcNow;
            }
            if (!Changed(null)) return;
            ShowToast(done.Count == 1 ? Lang.T("1 tugas selesai dihapus") : Lang.F("{0} tugas selesai dihapus", done.Count),
                delegate { RestoreTasks(done, parents); });
        }

        void ToggleTaskStar(TodoItem t)
        {
            t.Star = !t.Star;
            t.UpdatedUtc = DateTime.UtcNow;
            t.Dirty = true;
            Changed(t.Id);
        }

        void SetTaskDue(TodoItem t, DateTime? due)
        {
            if (t == null) return;
            // Keep an hour the task already had when only the day changes
            if (due.HasValue && t.Due.HasValue && t.Due.Value.TimeOfDay != TimeSpan.Zero) due = due.Value.Date + t.Due.Value.TimeOfDay;
            t.Due = due;
            t.UpdatedUtc = DateTime.UtcNow;
            t.Dirty = true;
            Changed(t.Id);
        }

        void MoveTaskToList(TodoItem t, string list)
        {
            if (t == null || owner == null || TaskLists.Same(TaskLists.NameOf(t), list)) return;
            string from = TaskLists.NameOf(t);
            if (!TaskTree.MoveBoardCard(owner.todos, t, list, null, false)) return;
            if (!Changed(t.Id)) return;
            string shown = TaskLists.IsDefault(list) ? Lang.T(TaskLists.Default) : list;
            string id = t.Id;
            ShowToast(Lang.F("Dipindahkan ke {0}", shown), delegate
            {
                var back = FindTask(id);
                if (back != null && TaskTree.MoveBoardCard(owner.todos, back, from, null, false)) Changed(id);
            });
        }

        void DuplicateTask(TodoItem t)
        {
            if (t == null || owner == null) return;
            var copy = TaskTree.Duplicate(owner.todos, t);
            if (copy != null && Changed(copy.Id)) taskView.Select(copy.Id);
        }

        static void CopyTaskText(TodoItem t)
        {
            if (t == null) return;
            string text = t.Title ?? "";
            if (!string.IsNullOrWhiteSpace(t.Notes)) text += Environment.NewLine + t.Notes.Trim();
            try { Clipboard.SetText(text); }
            catch (Exception ex) { Log.Write("salin tugas: " + ex.Message); }
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

        void RefillBoard(List<TodoItem> tasks, List<TaskListInfo> lists, string listName, Predicate<TodoItem> filter, string keepId)
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
                string laneName = name;
                Predicate<TodoItem> inLane = t => TaskLists.Same(TaskLists.NameOf(t), laneName) && (filter == null || filter(t));
                var open = TaskTree.OpenWhere(tasks, false, inLane, TaskTree.SortManual, null, null);
                lane.SetCards(open.ConvertAll(row => row.Item), TaskTree.DoneWhere(tasks, inLane), keepId);
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
            pendingDone.Remove(t.Id);
            t.Done = !t.Done;
            t.DoneAt = t.Done ? (DateTime?)DateTime.Now : null;
            t.UpdatedUtc = DateTime.UtcNow;
            t.Dirty = true;
            if (Changed(t.Id) && t.Done) ToastDone(new List<TodoItem> { t });
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
            menu.Items.Add(Lang.T(t.Done ? "Tandai belum selesai" : "Tandai selesai"), null, delegate
            {
                if (BoardOn) ToggleTaskDone(t);
                else BeginToggleDone(t);
            });
            menu.Items.Add(Lang.T(t.Star ? "Hapus bintang" : "Beri bintang"), null, delegate
            {
                taskView.PopStar(t.Id);
                ToggleTaskStar(t);
            });

            // Due date in one step, the four choices people pick most
            var dueMenu = new ToolStripMenuItem(Lang.T("Tenggat"));
            dueMenu.DropDownItems.Add(Lang.T("Hari ini"), null, delegate { SetTaskDue(t, DateTime.Today); });
            dueMenu.DropDownItems.Add(Lang.T("Besok"), null, delegate { SetTaskDue(t, DateTime.Today.AddDays(1)); });
            dueMenu.DropDownItems.Add(Lang.T("Minggu depan"), null, delegate { SetTaskDue(t, NextWeekday(DayOfWeek.Monday)); });
            if (t.Due.HasValue)
            {
                dueMenu.DropDownItems.Add(new ToolStripSeparator());
                dueMenu.DropDownItems.Add(Lang.T("Hapus tenggat"), null, delegate { SetTaskDue(t, null); });
            }
            dueMenu.DropDownItems.Add(new ToolStripSeparator());
            dueMenu.DropDownItems.Add(Lang.T("Pilih tanggal…"), null, delegate { EditTask(t); });
            menu.Items.Add(dueMenu);

            if (owner.taskLists != null && owner.taskLists.Count > 1)
            {
                var moveMenu = new ToolStripMenuItem(Lang.T("Pindah ke daftar"));
                foreach (var l in owner.taskLists)
                {
                    string name = l.Name;
                    var item = new ToolStripMenuItem(TaskLists.IsDefault(name) ? Lang.T(TaskLists.Default) : name, null, delegate { MoveTaskToList(t, name); });
                    item.Checked = TaskLists.Same(TaskLists.NameOf(t), name);
                    item.Enabled = !item.Checked;
                    moveMenu.DropDownItems.Add(item);
                }
                menu.Items.Add(moveMenu);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Duplikat"), null, delegate { DuplicateTask(t); });
            menu.Items.Add(Lang.T("Salin teks"), null, delegate { CopyTaskText(t); });
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

        void PickView(string key)
        {
            if (activeRailKey == key) return;
            FlushCompletions();
            activeRailKey = key;
            rail.Selected = key;
            RefillTasks(null);
            UpdateAddHintPlaceholder();
            if (!BoardOn) taskView.PlayEnter();
        }

        void UpdateAddHintPlaceholder()
        {
            addBox.Placeholder = AddPlaceholder();
            addBox.Invalidate();
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
            menu.Items.Add(Lang.T("Semua tugas"), null, delegate { PickView("all"); });
            menu.Items.Add(Lang.T("Hari ini"), null, delegate { PickView("today"); });
            menu.Items.Add(Lang.T("Terjadwal"), null, delegate { PickView("planned"); });
            menu.Items.Add(Lang.T("Berbintang"), null, delegate { PickView("starred"); });
            menu.Items.Add(new ToolStripSeparator());
            if (owner.taskLists != null)
            {
                foreach (var l in owner.taskLists)
                {
                    string name = l.Name;
                    menu.Items.Add(name, null, delegate { PickView(name); });
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

            menu.Items.Add(Lang.T("Tugas baru…") + "\tCtrl+N", null, delegate { EditTask(null); });
            menu.Items.Add(Lang.T("Cari tugas") + "\tCtrl+F", null, delegate { OpenTaskSearch(); });

            menu.Items.Add(new ToolStripSeparator());

            bool isSpecial = IsViewKey(activeRailKey) || TaskLists.IsDefault(activeRailKey);
            var mRename = menu.Items.Add(Lang.T("Ganti nama daftar…"), null, delegate { RenameList(activeRailKey); });
            mRename.Enabled = !isSpecial;

            var mDelete = menu.Items.Add(Lang.T("Hapus daftar"), null, delegate { DeleteList(activeRailKey); });
            mDelete.Enabled = !isSpecial;

            menu.Items.Add(new ToolStripSeparator());

            var clear = menu.Items.Add(Lang.T("Hapus tugas selesai"), null, delegate { ClearDone(); });
            clear.Enabled = owner != null && TaskTree.DoneWhere(owner.todos, ViewFilter()).Count > 0;

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
                // The input's accent edge follows focus, like the composer in WorkWindow
                input.GotFocus += delegate { Invalidate(Rectangle.Inflate(inputWell, 2, 2)); };
                input.LostFocus += delegate { Invalidate(Rectangle.Inflate(inputWell, 2, 2)); };
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                base.OnPaintBackground(e);
                var g = e.Graphics;
                KitInk.Smooth(g);
                float s = Ui;
                float radius = Ink.RadiusControl * s;
                KitInk.FillRound(g, inputWell, radius, KitInk.Group);
                if (input.Focused) KitInk.StrokeRound(g, RectangleF.Inflate(inputWell, -0.75f * s, -0.75f * s), radius - 0.75f * s, KitInk.Accent, 1.5f * s);
            }
        }

    }
}
