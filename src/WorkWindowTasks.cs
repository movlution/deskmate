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

    }
}
