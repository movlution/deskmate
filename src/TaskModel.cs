using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deskmate
{
    // One task list (Google Tasks "tasklist"). Empty lists live here so they survive a restart
    sealed class TaskListInfo
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name = "", GoogleId = "";
        // Renamed locally, not yet pushed
        public bool Dirty;

        public TaskListInfo Clone() { return new TaskListInfo { Id = Id, Name = Name, GoogleId = GoogleId, Dirty = Dirty }; }
    }

    // tugas-daftar.txt: id, name, google id, dirty. The default list is always first
    static class TaskLists
    {
        public const string Default = "Tugas Saya";
        const string DefaultId = "default";
        static string FilePath { get { return Path.Combine(Settings.Dir, "tugas-daftar.txt"); } }
        static string DeletedPath { get { return Path.Combine(Settings.Dir, "tugas-daftar-deleted.txt"); } }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[i + 1];
                    if (c == 'n') { sb.Append('\n'); i++; continue; }
                    if (c == 'r') { sb.Append('\r'); i++; continue; }
                    if (c == 't') { sb.Append('\t'); i++; continue; }
                    if (c == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static bool Same(string a, string b)
        {
            return string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDefault(string name) { return Same(name, Default); }

        // Name of the list a task belongs to
        public static string NameOf(TodoItem t) { return t == null || string.IsNullOrEmpty(t.ListName) ? Default : t.ListName; }

        public static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length);
            bool gap = false;
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { gap = sb.Length > 0; continue; }
                if (gap) { sb.Append(' '); gap = false; }
                sb.Append(c);
            }
            string r = sb.ToString();
            return r.Length > 120 ? r.Substring(0, 120).TrimEnd() : r;
        }

        static void EnsureDefault(List<TaskListInfo> list)
        {
            int at = list.FindIndex(x => x.Id == DefaultId || Same(x.Name, Default));
            if (at < 0) { list.Insert(0, new TaskListInfo { Id = DefaultId, Name = Default }); return; }
            var d = list[at];
            d.Id = DefaultId; d.Name = Default; d.Dirty = false;
            if (at != 0) { list.RemoveAt(at); list.Insert(0, d); }
        }

        public static List<TaskListInfo> Load()
        {
            var list = new List<TaskListInfo>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        var f = line.Split('\t');
                        if (f.Length < 2) continue;
                        string name = Clean(Unesc(f[1]));
                        if (name.Length == 0 || list.Exists(x => Same(x.Name, name))) continue;
                        var e = new TaskListInfo { Name = name };
                        if (f[0].Length > 0) e.Id = f[0];
                        if (f.Length >= 3) e.GoogleId = Unesc(f[2]);
                        if (f.Length >= 4) e.Dirty = f[3] == "1";
                        list.Add(e);
                    }
            }
            catch (Exception ex) { Log.Write("daftar tugas: " + ex.Message); }
            EnsureDefault(list);
            return list;
        }

        public static bool Save(List<TaskListInfo> list)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var e in list)
                    sb.Append(e.Id).Append('\t').Append(Esc(e.Name)).Append('\t').Append(Esc(e.GoogleId)).Append('\t').Append(e.Dirty ? '1' : '0').Append('\n');
                Directory.CreateDirectory(Settings.Dir);
                AtomicFile.Write(FilePath, new UTF8Encoding(false).GetBytes(sb.ToString()));
                return true;
            }
            catch (Exception ex) { Log.Write("simpan daftar tugas: " + ex.Message); return false; }
        }

        public static List<TaskListInfo> CloneAll(List<TaskListInfo> list)
        {
            return list.ConvertAll(x => x.Clone());
        }

        public static TaskListInfo Find(List<TaskListInfo> list, string name)
        {
            foreach (var e in list) if (Same(e.Name, name)) return e;
            return null;
        }

        // Every list a task names must exist in the registry; true when something was added
        public static bool Ensure(List<TaskListInfo> reg, List<TodoItem> todos)
        {
            if (reg == null || todos == null) return false;
            bool added = false;
            foreach (var t in todos)
            {
                if (t == null) continue;
                string n = Clean(NameOf(t));
                if (n.Length == 0) continue;
                if (Find(reg, n) != null) continue;
                reg.Add(new TaskListInfo { Name = n });
                added = true;
            }
            return added;
        }

        // Display order: registry order (default first); stray names from tasks follow
        public static List<string> Names(List<TaskListInfo> reg, List<TodoItem> todos)
        {
            var names = new List<string>();
            if (reg != null)
                foreach (var e in reg) if (e != null && !names.Exists(x => Same(x, e.Name))) names.Add(e.Name);
            if (!names.Exists(x => Same(x, Default))) names.Insert(0, Default);
            if (todos != null)
                foreach (var t in todos)
                {
                    if (t == null) continue;
                    string n = Clean(NameOf(t));
                    if (n.Length > 0 && !names.Exists(x => Same(x, n))) names.Add(n);
                }
            return names;
        }

        // Existing entry with that name, or a new one; null for an empty name
        public static TaskListInfo Create(List<TaskListInfo> reg, string name)
        {
            name = Clean(name);
            if (name.Length == 0) return null;
            var e = Find(reg, name);
            if (e != null) return e;
            e = new TaskListInfo { Name = name };
            reg.Add(e);
            return e;
        }

        // The default list and duplicate names are refused
        public static bool Rename(List<TaskListInfo> reg, List<TodoItem> todos, string oldName, string newName)
        {
            if (reg == null) return false;
            newName = Clean(newName);
            var e = Find(reg, oldName);
            if (e == null || newName.Length == 0 || IsDefault(e.Name) || IsDefault(newName)) return false;
            if (!Same(oldName, newName) && Find(reg, newName) != null) return false;
            if (e.Name == newName) return true;
            e.Name = newName;
            if (!string.IsNullOrEmpty(e.GoogleId)) e.Dirty = true;
            if (todos != null)
            {
                var now = DateTime.UtcNow;
                foreach (var t in todos)
                    if (t != null && Same(NameOf(t), oldName)) { t.ListName = newName; t.UpdatedUtc = now; }
            }
            return true;
        }

        // Deletes the list and its tasks; Google deletions are queued. Returns tasks removed, -1 if refused
        public static int Delete(List<TaskListInfo> reg, List<TodoItem> todos, string name)
        {
            if (IsDefault(name)) return -1;
            var e = reg != null ? Find(reg, name) : null;
            int removed = 0;
            if (todos != null)
            {
                for (int i = todos.Count - 1; i >= 0; i--)
                {
                    if (todos[i] == null || !Same(NameOf(todos[i]), name)) continue;
                    if (!string.IsNullOrEmpty(todos[i].GoogleTaskId)) TodoFile.AddDeletedGoogle(todos[i].GoogleListId, todos[i].GoogleTaskId);
                    todos.RemoveAt(i);
                    removed++;
                }
            }
            if (e != null)
            {
                if (!string.IsNullOrEmpty(e.GoogleId)) AddDeleted(e.GoogleId);
                reg.Remove(e);
            }
            return removed;
        }

        public static List<string> LoadDeleted()
        {
            var list = new List<string>();
            try
            {
                if (File.Exists(DeletedPath))
                    foreach (var l in File.ReadAllLines(DeletedPath, Encoding.UTF8))
                        if (l.Trim().Length > 0 && !list.Contains(l.Trim())) list.Add(l.Trim());
            }
            catch (Exception ex) { Log.Write("hapus daftar tugas: " + ex.Message); }
            return list;
        }

        public static void AddDeleted(string googleId)
        {
            if (string.IsNullOrEmpty(googleId)) return;
            var list = LoadDeleted();
            if (list.Contains(googleId)) return;
            list.Add(googleId);
            SaveDeleted(list);
        }

        public static void SaveDeleted(List<string> ids)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                AtomicFile.Write(DeletedPath, new UTF8Encoding(false).GetBytes(string.Join("\n", ids.ToArray())));
            }
            catch (Exception ex) { Log.Write("simpan hapus daftar tugas: " + ex.Message); }
        }
    }

    // One visible row of the open-task list
    sealed class TaskRow
    {
        public TodoItem Item;
        public int Depth;
        public bool HasChildren;
        public int ChildCount;
    }

    // Views, ordering and subtask moves, free of any UI so they can be checked on their own
    static class TaskTree
    {
        public const string SortManual = "manual", SortDate = "date", SortTitle = "title";

        public static string NormalizeSort(string s)
        {
            return s == SortDate || s == SortTitle ? s : SortManual;
        }

        // "Urutan saya": position, then newest first
        public static int CompareManual(TodoItem a, TodoItem b)
        {
            int c = a.Position.CompareTo(b.Position);
            if (c != 0) return c;
            c = b.Created.CompareTo(a.Created);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }

        static int CompareDate(TodoItem a, TodoItem b)
        {
            if (a.Due.HasValue != b.Due.HasValue) return a.Due.HasValue ? -1 : 1;
            if (a.Due.HasValue && a.Due.Value != b.Due.Value) return a.Due.Value.CompareTo(b.Due.Value);
            return CompareManual(a, b);
        }

        static int CompareTitle(TodoItem a, TodoItem b)
        {
            int c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : CompareManual(a, b);
        }

        public static Comparison<TodoItem> Comparer(string sort)
        {
            sort = NormalizeSort(sort);
            if (sort == SortDate) return CompareDate;
            if (sort == SortTitle) return CompareTitle;
            return CompareManual;
        }

        static bool InView(TodoItem t, string list, bool starred)
        {
            if (starred && !t.Star) return false;
            return list == null || TaskLists.Same(TaskLists.NameOf(t), list);
        }

        static Predicate<TodoItem> Where(string list, bool starred)
        {
            return t => InView(t, list, starred);
        }

        // Smart views. Today keeps overdue work in sight, as Google Tasks and To Do do
        public static bool DueToday(TodoItem t)
        {
            return t != null && t.Due.HasValue && t.Due.Value.Date <= DateTime.Today;
        }

        public static bool Planned(TodoItem t) { return t != null && t.Due.HasValue; }

        // Case-insensitive match on title and details; empty query matches everything
        public static bool Matches(TodoItem t, string query)
        {
            if (t == null) return false;
            if (string.IsNullOrWhiteSpace(query)) return true;
            var c = CultureInfo.CurrentCulture.CompareInfo;
            foreach (var word in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (c.IndexOf(t.Title ?? "", word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0 &&
                    c.IndexOf(t.Notes ?? "", word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) return false;
            return true;
        }

        // Open tasks as rows: top level in sort order, subtasks (one indent) under their parent.
        // list null = every list, starred = only starred. collapsed hides a parent's subtasks
        public static List<TaskRow> Open(List<TodoItem> all, string list, bool starred, string sort, List<string> listOrder, HashSet<string> collapsed)
        {
            return OpenWhere(all, list == null, Where(list, starred), sort, listOrder, collapsed);
        }

        // Same as Open with any filter. allLists orders the top level by list first
        public static List<TaskRow> OpenWhere(List<TodoItem> all, bool allLists, Predicate<TodoItem> filter, string sort, List<string> listOrder, HashSet<string> collapsed)
        {
            if (all == null) return new List<TaskRow>();
            var cands = all.FindAll(t => t != null && !t.Done && (filter == null || filter(t)));
            var ids = new HashSet<string>();
            foreach (var t in cands) ids.Add(t.Id);
            var kids = new Dictionary<string, List<TodoItem>>();
            var top = new List<TodoItem>();
            foreach (var t in cands)
            {
                if (!string.IsNullOrEmpty(t.ParentId) && t.ParentId != t.Id && ids.Contains(t.ParentId))
                {
                    List<TodoItem> k;
                    if (!kids.TryGetValue(t.ParentId, out k)) kids[t.ParentId] = k = new List<TodoItem>();
                    k.Add(t);
                }
                else top.Add(t);
            }
            var cmp = Comparer(sort);
            Comparison<TodoItem> topCmp = cmp;
            if (allLists && listOrder != null)
            {
                topCmp = delegate(TodoItem a, TodoItem b)
                {
                    int c = ListIndex(listOrder, a).CompareTo(ListIndex(listOrder, b));
                    return c != 0 ? c : cmp(a, b);
                };
            }
            top.Sort(topCmp);
            var rows = new List<TaskRow>();
            var seen = new HashSet<string>();
            foreach (var t in top) AddRows(rows, t, 0, kids, cmp, collapsed, seen);
            // A parent cycle in damaged data would hide its members; show them flat
            foreach (var t in cands)
                if (!seen.Contains(t.Id)) rows.Add(new TaskRow { Item = t });
            return rows;
        }

        static int ListIndex(List<string> order, TodoItem t)
        {
            int i = order.FindIndex(x => TaskLists.Same(x, TaskLists.NameOf(t)));
            return i < 0 ? int.MaxValue : i;
        }

        static void AddRows(List<TaskRow> rows, TodoItem t, int depth, Dictionary<string, List<TodoItem>> kids, Comparison<TodoItem> cmp, HashSet<string> collapsed, HashSet<string> seen)
        {
            if (!seen.Add(t.Id)) return;
            List<TodoItem> k;
            kids.TryGetValue(t.Id, out k);
            rows.Add(new TaskRow { Item = t, Depth = Math.Min(depth, 1), HasChildren = k != null, ChildCount = k != null ? k.Count : 0 });
            if (k == null || (collapsed != null && collapsed.Contains(t.Id))) return;
            k.Sort(cmp);
            foreach (var c in k) AddRows(rows, c, depth + 1, kids, cmp, collapsed, seen);
        }

        // Completed tasks of the view, most recent first
        public static List<TodoItem> Done(List<TodoItem> all, string list, bool starred)
        {
            return DoneWhere(all, Where(list, starred));
        }

        public static List<TodoItem> DoneWhere(List<TodoItem> all, Predicate<TodoItem> filter)
        {
            if (all == null) return new List<TodoItem>();
            var d = all.FindAll(t => t != null && t.Done && (filter == null || filter(t)));
            d.Sort(delegate(TodoItem a, TodoItem b)
            {
                int c = (b.DoneAt ?? b.Created).CompareTo(a.DoneAt ?? a.Created);
                return c != 0 ? c : CompareManual(a, b);
            });
            return d;
        }

        public static int OpenCount(List<TodoItem> all, string list, bool starred)
        {
            return CountWhere(all, Where(list, starred));
        }

        public static int CountWhere(List<TodoItem> all, Predicate<TodoItem> filter)
        {
            if (all == null) return 0;
            int n = 0;
            foreach (var t in all) if (t != null && !t.Done && (filter == null || filter(t))) n++;
            return n;
        }

        // Same list and same parent, in "my order"
        public static List<TodoItem> Siblings(List<TodoItem> all, TodoItem t)
        {
            if (t == null) return new List<TodoItem>();
            var s = all == null ? new List<TodoItem>() : all.FindAll(x => x != null && x.Done == t.Done && x.Id != t.Id && TaskLists.Same(TaskLists.NameOf(x), TaskLists.NameOf(t)) && SameParent(x, t));
            s.Add(t);
            s.Sort(CompareManual);
            return s;
        }

        static bool SameParent(TodoItem a, TodoItem b) { return (a.ParentId ?? "") == (b.ParentId ?? ""); }

        public static List<TodoItem> Children(List<TodoItem> all, TodoItem t)
        {
            if (all == null || t == null) return new List<TodoItem>();
            return all.FindAll(x => x != null && x.Id != t.Id && x.ParentId == t.Id);
        }

        static void Touch(TodoItem t, bool moved)
        {
            t.UpdatedUtc = DateTime.UtcNow;
            t.Dirty = true;
            if (moved) t.Moved = true;
        }

        // First free position above every sibling: a new task lands on top, as in Google Tasks
        public static int TopPosition(List<TodoItem> all, string list, string parentId)
        {
            if (all == null) return 0;
            int min = 0; bool any = false;
            foreach (var x in all)
            {
                if (x == null || x.Done || !TaskLists.Same(TaskLists.NameOf(x), list) || (x.ParentId ?? "") != (parentId ?? "")) continue;
                if (!any || x.Position < min) min = x.Position;
                any = true;
            }
            return any ? min - 1 : 0;
        }

        // Moves t one place up or down among its siblings. False when it is already at the end
        public static bool Reorder(List<TodoItem> all, TodoItem t, bool up)
        {
            if (all == null || t == null) return false;
            var sib = Siblings(all, t);
            int i = sib.IndexOf(t), j = up ? i - 1 : i + 1;
            if (j < 0 || j >= sib.Count) return false;
            sib.RemoveAt(i);
            sib.Insert(j, t);
            for (int n = 0; n < sib.Count; n++) sib[n].Position = n;
            Touch(t, true);
            return true;
        }

        public static bool BoardSiblings(TodoItem a, TodoItem b)
        {
            return a != null && b != null && a.Id != b.Id &&
                TaskLists.Same(TaskLists.NameOf(a), TaskLists.NameOf(b)) && SameParent(a, b);
        }

        public static bool MoveBoardCard(List<TodoItem> all, TodoItem task, string list, TodoItem anchor, bool after)
        {
            if (all == null || task == null || !all.Contains(task) || string.IsNullOrEmpty(list)) return false;
            bool across = !TaskLists.Same(TaskLists.NameOf(task), list);
            if (across)
            {
                // Keep a moved parent's subtasks together. A subtask moved alone becomes a top-level task.
                int position = TopPosition(all, list, null);
                var pending = new List<TodoItem> { task };
                var seen = new HashSet<TodoItem> { task };
                task.ParentId = "";
                for (int i = 0; i < pending.Count; i++)
                {
                    var item = pending[i];
                    foreach (var child in Children(all, item))
                        if (TaskLists.Same(TaskLists.NameOf(child), TaskLists.NameOf(item)) && seen.Add(child)) pending.Add(child);
                    item.ListName = list;
                    Touch(item, true);
                }
                task.Position = position;
            }
            if (!BoardSiblings(task, anchor) || anchor.Done || !all.Contains(anchor)) anchor = null;
            if (!across && (anchor == null || task.Done)) return false;
            if (anchor != null)
            {
                var siblings = all.FindAll(t => BoardSiblings(task, t));
                siblings.Add(task);
                siblings.Sort(CompareManual);
                int from = siblings.IndexOf(task);
                siblings.RemoveAt(from);
                int to = siblings.IndexOf(anchor) + (after ? 1 : 0);
                if (!across && from == to) return false;
                siblings.Insert(to, task);
                for (int i = 0; i < siblings.Count; i++) siblings[i].Position = i;
            }
            Touch(task, across || anchor != null);
            return true;
        }

        // Google Tasks nests one level only: a task with subtasks cannot become one
        public static bool CanIndent(List<TodoItem> all, TodoItem t)
        {
            if (all == null || t == null || t.Done || !string.IsNullOrEmpty(t.ParentId) || Children(all, t).Count > 0) return false;
            var sib = Siblings(all, t);
            return sib.IndexOf(t) > 0;
        }

        public static bool Indent(List<TodoItem> all, TodoItem t)
        {
            if (!CanIndent(all, t)) return false;
            var sib = Siblings(all, t);
            var parent = sib[sib.IndexOf(t) - 1];
            int max = -1;
            foreach (var c in Children(all, parent)) if (!c.Done) max = Math.Max(max, c.Position);
            t.ParentId = parent.Id;
            t.Position = max + 1;
            Touch(t, true);
            return true;
        }

        // Subtask back to the top level, right below its former parent
        public static bool Outdent(List<TodoItem> all, TodoItem t)
        {
            if (all == null || t == null || string.IsNullOrEmpty(t.ParentId)) return false;
            var parent = all.Find(x => x != null && x.Id == t.ParentId);
            t.ParentId = "";
            var sib = Siblings(all, t);
            sib.Remove(t);
            int at = parent != null ? sib.IndexOf(parent) : -1;
            sib.Insert(at < 0 ? sib.Count : at + 1, t);
            for (int n = 0; n < sib.Count; n++) sib[n].Position = n;
            Touch(t, true);
            return true;
        }

        // The task and every subtask below it, parent first
        public static List<TodoItem> Subtree(List<TodoItem> all, TodoItem t)
        {
            var gone = new List<TodoItem>();
            if (all == null || t == null) return gone;
            gone.Add(t);
            var seen = new HashSet<string>();
            seen.Add(t.Id);
            for (int i = 0; i < gone.Count; i++)
                foreach (var c in Children(all, gone[i]))
                    if (seen.Add(c.Id)) gone.Add(c);
            return gone;
        }

        // A copy right below the original, in the same list and under the same parent
        public static TodoItem Duplicate(List<TodoItem> all, TodoItem t)
        {
            if (all == null || t == null || !all.Contains(t)) return null;
            var copy = new TodoItem
            {
                Title = t.Title,
                Notes = t.Notes,
                Due = t.Due,
                Star = t.Star,
                ListName = t.ListName,
                ParentId = t.ParentId ?? "",
                UpdatedUtc = DateTime.UtcNow,
                Dirty = true
            };
            var sib = Siblings(all, t);
            sib.Insert(sib.IndexOf(t) + 1, copy);
            for (int n = 0; n < sib.Count; n++) sib[n].Position = n;
            all.Add(copy);
            Touch(copy, true);
            return copy;
        }

        // Removes the task and its subtasks; each Google id is queued for deletion. Returns how many went
        public static int Delete(List<TodoItem> all, TodoItem t)
        {
            if (all == null || t == null) return 0;
            var gone = Subtree(all, t);
            foreach (var g in gone)
            {
                if (!string.IsNullOrEmpty(g.GoogleTaskId)) TodoFile.AddDeletedGoogle(g.GoogleListId, g.GoogleTaskId);
                all.Remove(g);
            }
            return gone.Count;
        }

        // Deletes completed tasks of the view (list null = all) with their Google ids queued
        public static int ClearDone(List<TodoItem> all, string list)
        {
            if (all == null) return 0;
            int n = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null || !t.Done || (list != null && !TaskLists.Same(TaskLists.NameOf(t), list))) continue;
                if (!string.IsNullOrEmpty(t.GoogleTaskId)) TodoFile.AddDeletedGoogle(t.GoogleListId, t.GoogleTaskId);
                all.RemoveAt(i);
                n++;
            }
            // Subtasks of a deleted parent would dangle
            var live = new HashSet<string>();
            foreach (var t in all) if (t != null) live.Add(t.Id);
            foreach (var t in all) if (t != null && !string.IsNullOrEmpty(t.ParentId) && !live.Contains(t.ParentId)) { t.ParentId = ""; t.Moved = true; t.Dirty = true; t.UpdatedUtc = DateTime.UtcNow; }
            return n;
        }
    }
}
