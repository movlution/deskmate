using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Deskmate
{
    // Full-app AI action handlers: tasks, notes, calendar, reminders, timer, media, system, shelf/clipboard, integrations, tools, settings
    partial class Island
    {
        static bool? ArgBool(Dictionary<string, object> args, string key)
        {
            if (args == null) return null;
            object v;
            if (!args.TryGetValue(key, out v) || v == null) return null;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "on" || s == "yes" || s == "ya") return true;
            if (s == "false" || s == "0" || s == "off" || s == "no" || s == "tidak") return false;
            return null;
        }

        static int? ArgInt(Dictionary<string, object> args, string key)
        {
            if (args == null) return null;
            object v;
            if (!args.TryGetValue(key, out v) || v == null) return null;
            int n;
            if (int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            double d;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return (int)Math.Round(d);
            return null;
        }

        static bool TryParseAskDateTime(string raw, out DateTime dt, out bool hasTime)
        {
            dt = default(DateTime);
            hasTime = false;
            raw = (raw ?? "").Trim();
            if (raw.Length == 0) return false;
            string[] withTime = { "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
            if (DateTime.TryParseExact(raw, withTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                hasTime = true;
                return true;
            }
            if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                hasTime = false;
                return true;
            }
            return false;
        }

        static ChatAct ActOk(char glyph, string text) { return new ChatAct { Glyph = glyph, Text = text }; }
        static ChatAct ActFail(string text) { return new ChatAct { Glyph = IcoFail, Text = text }; }

        // Worker-thread tools (blocking HTTP such as Google Calendar API)
        bool IsWorkerTool(string name, Dictionary<string, object> args)
        {
            if (name != "manage_calendar") return false;
            string act = Arg(args, "action").Trim().ToLowerInvariant();
            return act == "create" || act == "update" || act == "delete";
        }

        ChatAct DoWorkerTool(string name, Dictionary<string, object> args)
        {
            if (name == "manage_calendar") return DoManageCalendarWorker(args);
            return null;
        }

        // ==================== TASKS ====================

        TodoItem FindLocalTask(string id, string title)
        {
            EnsureWorkData();
            id = (id ?? "").Trim();
            title = (title ?? "").Trim();
            if (id.Length > 0)
            {
                var exactId = todos.Find(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                if (exactId != null) return exactId;
            }
            if (title.Length > 0)
            {
                var exact = todos.Find(t => string.Equals(t.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                var partial = todos.Find(t => t.Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
                if (partial != null) return partial;
            }
            return null;
        }

        KuliaTask FindExternalTask(string id, string title, string source)
        {
            if (tasks == null) return null;
            id = (id ?? "").Trim();
            title = (title ?? "").Trim();
            source = (source ?? "").Trim().ToLowerInvariant();
            if (id.Length > 0)
            {
                var byId = tasks.Find(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    (source.Length == 0 || string.Equals(t.Source, source, StringComparison.OrdinalIgnoreCase)));
                if (byId != null) return byId;
            }
            if (title.Length > 0)
            {
                var exact = tasks.Find(t => string.Equals(t.Title.Trim(), title, StringComparison.OrdinalIgnoreCase) &&
                    (source.Length == 0 || string.Equals(t.Source, source, StringComparison.OrdinalIgnoreCase)));
                if (exact != null) return exact;
                var partial = tasks.Find(t => t.Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (source.Length == 0 || string.Equals(t.Source, source, StringComparison.OrdinalIgnoreCase)));
                if (partial != null) return partial;
            }
            return null;
        }

        ChatAct AddAskTask(string title, string due, string listName = "", string notes = "", bool star = false, string parentId = "")
        {
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 3000)
                return ActFail(Lang.T("Judul tugas tidak valid"));
            DateTime at = default(DateTime);
            bool hasTime;
            due = (due ?? "").Trim();
            if (due.Length > 0 && !TryParseAskDateTime(due, out at, out hasTime))
                return ActFail(Lang.T("Tanggal tugas tidak jelas"));

            EnsureWorkData();
            string list = TaskLists.Clean(listName);
            if (list.Length == 0) list = TaskLists.Default;
            if (TaskLists.Find(taskLists, list) == null)
            {
                TaskLists.Create(taskLists, list);
                TaskLists.Save(taskLists);
            }

            string cleanParent = "";
            if (!string.IsNullOrEmpty(parentId))
            {
                var parent = FindLocalTask(parentId, parentId);
                if (parent != null && string.IsNullOrEmpty(parent.ParentId))
                {
                    cleanParent = parent.Id;
                    list = TaskLists.NameOf(parent);
                }
            }

            var task = new TodoItem
            {
                Title = title,
                ListName = list,
                Due = due.Length > 0 ? (DateTime?)at : null,
                Notes = (notes ?? "").Trim(),
                Star = star,
                ParentId = cleanParent,
                UpdatedUtc = DateTime.UtcNow,
                Dirty = true,
            };
            todos.Add(task);
            if (!TodosChanged())
            {
                todos.Remove(task);
                return ActFail(Lang.T("Tugas gagal disimpan"));
            }
            ReloadWorkFromSync();
            return ActOk(Ink.IcoCalendar, Lang.F("Tugas baru: {0}", title));
        }

        ChatAct DoManageTask(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            switch (act)
            {
                case "add":
                case "create":
                    return AddAskTask(s("title").Length > 0 ? s("title") : s("text"), s("due"), s("list"), s("notes"), ArgBool(args, "star") == true, s("parent_id"));

                case "update":
                case "edit":
                {
                    var task = FindLocalTask(s("id"), s("title"));
                    if (task == null) return ActFail(Lang.T("Tugas tidak ditemukan"));
                    string newTitle = s("new_title");
                    if (newTitle.Length > 0) task.Title = newTitle;
                    string dueStr = s("due");
                    if (ArgBool(args, "clear_due") == true) task.Due = null;
                    else if (dueStr.Length > 0)
                    {
                        DateTime at;
                        bool hasTime;
                        if (!TryParseAskDateTime(dueStr, out at, out hasTime))
                            return ActFail(Lang.T("Tanggal tugas tidak jelas"));
                        task.Due = at;
                    }
                    string list = TaskLists.Clean(s("list"));
                    if (list.Length > 0)
                    {
                        if (TaskLists.Find(taskLists, list) == null)
                        {
                            TaskLists.Create(taskLists, list);
                            TaskLists.Save(taskLists);
                        }
                        if (!TaskLists.Same(task.ListName, list))
                        {
                            task.ListName = list;
                            task.Moved = true;
                        }
                    }
                    if (args != null && args.ContainsKey("notes")) task.Notes = s("notes");
                    var star = ArgBool(args, "star");
                    if (star.HasValue) task.Star = star.Value;
                    var done = ArgBool(args, "done");
                    if (done.HasValue)
                    {
                        task.Done = done.Value;
                        task.DoneAt = done.Value ? (DateTime?)DateTime.Now : null;
                    }
                    task.UpdatedUtc = DateTime.UtcNow;
                    task.Dirty = true;
                    if (!TodosChanged()) return ActFail(Lang.T("Tugas gagal disimpan"));
                    ReloadWorkFromSync();
                    return ActOk(Ink.IcoCalendar, Lang.F("Tugas diperbarui: {0}", task.Title));
                }

                case "complete":
                case "done":
                case "reopen":
                {
                    bool markDone = act != "reopen";
                    string src = s("source").ToLowerInvariant();
                    if (src.Length > 0 && src != "local")
                    {
                        if (!markDone) return ActFail(Lang.T("Tugas sumber luar belum bisa dibuka kembali"));
                        var ext = FindExternalTask(s("id"), s("title"), src);
                        if (ext == null) return ActFail(Lang.T("Tugas tidak ditemukan"));
                        if (ext.ReadOnly) return ActFail(Lang.T("Tugas ini hanya bisa dibaca"));
                        MarkDone(ext);
                        return ActOk(IcoDone, Lang.F("Tugas selesai: {0}", ext.Title));
                    }
                    var task = FindLocalTask(s("id"), s("title"));
                    if (task != null)
                    {
                        task.Done = markDone;
                        task.DoneAt = markDone ? (DateTime?)DateTime.Now : null;
                        task.UpdatedUtc = DateTime.UtcNow;
                        task.Dirty = true;
                        if (!TodosChanged()) return ActFail(Lang.T("Tugas gagal disimpan"));
                        ReloadWorkFromSync();
                        return ActOk(IcoDone, markDone ? Lang.F("Tugas selesai: {0}", task.Title) : Lang.F("Tugas dibuka lagi: {0}", task.Title));
                    }
                    if (markDone)
                    {
                        var ext = FindExternalTask(s("id"), s("title"), "");
                        if (ext != null)
                        {
                            if (ext.ReadOnly) return ActFail(Lang.T("Tugas ini hanya bisa dibaca"));
                            MarkDone(ext);
                            return ActOk(IcoDone, Lang.F("Tugas selesai: {0}", ext.Title));
                        }
                    }
                    return ActFail(Lang.T("Tugas tidak ditemukan"));
                }

                case "delete":
                case "remove":
                {
                    var task = FindLocalTask(s("id"), s("title"));
                    if (task == null) return ActFail(Lang.T("Tugas tidak ditemukan"));
                    string title = task.Title;
                    var toRemove = new List<TodoItem> { task };
                    foreach (var sub in todos)
                        if (sub != task && string.Equals(sub.ParentId, task.Id, StringComparison.OrdinalIgnoreCase))
                            toRemove.Add(sub);
                    foreach (var item in toRemove)
                    {
                        if (!string.IsNullOrEmpty(item.GoogleTaskId))
                            TodoFile.AddDeletedGoogle(item.GoogleListId, item.GoogleTaskId);
                        todos.Remove(item);
                    }
                    if (!TodosChanged()) return ActFail(Lang.T("Tugas gagal dihapus"));
                    ReloadWorkFromSync();
                    return ActOk(IcoDone, Lang.F("Tugas dihapus: {0}", title));
                }

                case "set_focus_task":
                {
                    string title = s("title");
                    SetFocusTask(title);
                    return ActOk(Ink.IcoTimer, title.Length > 0 ? Lang.F("Fokus tugas: {0}", title) : Lang.T("Tugas fokus dibersihkan"));
                }

                case "create_list":
                {
                    EnsureWorkData();
                    string name = TaskLists.Clean(s("list").Length > 0 ? s("list") : s("title"));
                    if (name.Length == 0) return ActFail(Lang.T("Nama daftar tugas tidak valid"));
                    TaskLists.Create(taskLists, name);
                    if (!ListsChanged()) return ActFail(Lang.T("Daftar tugas gagal disimpan"));
                    ReloadWorkFromSync();
                    return ActOk(Ink.IcoCalendar, Lang.F("Daftar tugas dibuat: {0}", name));
                }

                case "rename_list":
                {
                    EnsureWorkData();
                    string oldName = s("list");
                    string newName = s("new_title").Length > 0 ? s("new_title") : s("new_list");
                    if (!TaskLists.Rename(taskLists, todos, oldName, newName))
                        return ActFail(Lang.T("Daftar tugas tidak bisa diubah namanya"));
                    TodosChanged();
                    ListsChanged();
                    ReloadWorkFromSync();
                    return ActOk(Ink.IcoCalendar, Lang.F("Daftar tugas diganti nama: {0}", TaskLists.Clean(newName)));
                }

                case "delete_list":
                {
                    EnsureWorkData();
                    string name = s("list").Length > 0 ? s("list") : s("title");
                    int removed = TaskLists.Delete(taskLists, todos, name);
                    if (removed < 0) return ActFail(Lang.T("Daftar utama tidak bisa dihapus"));
                    TodosChanged();
                    ListsChanged();
                    ReloadWorkFromSync();
                    return ActOk(IcoDone, Lang.F("Daftar tugas dihapus: {0}", name));
                }

                case "add_external":
                {
                    string text = s("title").Length > 0 ? s("title") : s("text");
                    if (text.Length == 0 || sources == null) return ActFail(Lang.T("Sumber tugas luar belum terhubung"));
                    string target = s("source").ToLowerInvariant();
                    if (target.Length == 0) target = sources.Find("todoist") != null ? "todoist" : sources.Find("notion") != null ? "notion" : "";
                    if (target.Length == 0 || sources.Find(target) == null)
                        return ActFail(Lang.T("Todoist atau Notion belum terhubung"));
                    SaveNote(text, target);
                    return ActOk(Ink.IcoCalendar, Lang.F("Tugas baru: {0}", text));
                }
            }
            return ActFail(Lang.T("Aksi tugas tidak dikenal"));
        }

        // ==================== NOTES ====================

        NoteInfo FindNoteByKeyOrTitle(string key, string title, bool includeArchived)
        {
            key = (key ?? "").Trim();
            title = (title ?? "").Trim();
            var pins = NoteFiles.LoadPins();
            var meta = NoteFiles.LoadMeta();
            var list = new List<NoteInfo>(NoteFiles.List("", pins, meta, false, ""));
            if (includeArchived) list.AddRange(NoteFiles.List("", pins, meta, true, ""));

            if (key.Length > 0)
            {
                var exactKey = list.Find(n => string.Equals(NoteFiles.KeyOf(n.Path), key, StringComparison.OrdinalIgnoreCase));
                if (exactKey != null) return exactKey;
            }
            if (title.Length > 0)
            {
                var exactTitle = list.Find(n => string.Equals(n.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
                if (exactTitle != null) return exactTitle;
                var partial = list.Find(n => n.Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
                if (partial != null) return partial;
            }
            return null;
        }

        void RefreshNotesUi()
        {
            ScheduleDriveNotesUpload();
            if (workWin != null && !workWin.IsDisposed) workWin.ReloadNotesFromDisk();
        }

        ChatAct DoManageNote(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            if (DriveNotesLoading) return ActFail(Lang.T("Catatan sedang dimuat dari Google Drive"));
            if (workWin != null && !workWin.IsDisposed) workWin.FlushNoteDraft();

            switch (act)
            {
                case "append_quick":
                case "quick":
                {
                    string text = s("content").Length > 0 ? s("content") : s("text");
                    if (text.Length == 0) return ActFail(Lang.T("Isi catatan kosong"));
                    SaveNote(text, "rak");
                    return ActOk(IcoNote, Lang.F("Dicatat: {0}", Fmt.OneLine(text, 80)));
                }

                case "create":
                case "add":
                {
                    string title = s("title");
                    string content = Arg(args, "content");
                    if (string.IsNullOrEmpty(content)) content = Arg(args, "text");
                    if (title.Length == 0 && content.Trim().Length == 0)
                        return ActFail(Lang.T("Isi catatan kosong"));
                    string path = NoteFiles.NewPath();
                    string payload = NoteFiles.NormalizeNewlines(NoteFiles.JoinNote(title, content));
                    if (!NoteFiles.Write(path, payload)) return ActFail(Lang.T("Catatan gagal disimpan"));

                    string key = NoteFiles.KeyOf(path);
                    string color = s("color").ToLowerInvariant();
                    string labels = NotePalette.CleanTags(s("labels"));
                    bool pin = ArgBool(args, "pinned") == true;
                    var meta = NoteFiles.LoadMeta();
                    meta[key] = new NoteMeta
                    {
                        Key = key,
                        Color = Array.IndexOf(NotePalette.Keys, color) >= 0 ? color : "",
                        Labels = labels,
                        Archived = false,
                        UpdatedUtc = DateTime.UtcNow,
                    };
                    NoteFiles.SaveMeta(meta);
                    if (pin)
                    {
                        var pins = NoteFiles.LoadPins();
                        NoteFiles.SetPin(pins, path, true);
                    }
                    RefreshNotesUi();
                    string label = title.Length > 0 ? title : Fmt.OneLine(content, 60);
                    return ActOk(IcoNote, Lang.F("Catatan dibuat: {0}", label));
                }

                case "update":
                case "edit":
                {
                    var note = FindNoteByKeyOrTitle(s("key"), s("title"), true);
                    if (note == null) return ActFail(Lang.T("Catatan tidak ditemukan"));
                    string existing = NoteFiles.Read(note.Path);
                    string oldTitle, oldBody;
                    NoteFiles.SplitNote(existing, out oldTitle, out oldBody);
                    string newTitle = s("new_title").Length > 0 ? s("new_title") : oldTitle;
                    string newBody = args != null && args.ContainsKey("content") ? Arg(args, "content") : oldBody;
                    string payload = note.Quick
                        ? NoteFiles.NormalizeNewlines(newBody)
                        : NoteFiles.NormalizeNewlines(NoteFiles.JoinNote(newTitle, newBody));
                    if (!NoteFiles.Write(note.Path, payload)) return ActFail(Lang.T("Catatan gagal disimpan"));
                    var meta = NoteFiles.LoadMeta();
                    string key = NoteFiles.KeyOf(note.Path);
                    NoteMeta m;
                    if (!meta.TryGetValue(key, out m) || m == null) { m = new NoteMeta { Key = key }; meta[key] = m; }
                    m.UpdatedUtc = DateTime.UtcNow;
                    NoteFiles.SaveMeta(meta);
                    RefreshNotesUi();
                    return ActOk(IcoNote, Lang.F("Catatan diperbarui: {0}", newTitle.Length > 0 ? newTitle : note.Title));
                }

                case "append":
                {
                    var note = FindNoteByKeyOrTitle(s("key"), s("title"), true);
                    if (note == null) return ActFail(Lang.T("Catatan tidak ditemukan"));
                    string addText = Arg(args, "content");
                    if (string.IsNullOrEmpty(addText)) addText = Arg(args, "text");
                    if (addText.Trim().Length == 0) return ActFail(Lang.T("Isi tambahan kosong"));
                    string existing = NoteFiles.Read(note.Path);
                    string sep = existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal) ? "\r\n" : "";
                    string payload = NoteFiles.NormalizeNewlines(existing + sep + addText);
                    if (!NoteFiles.Write(note.Path, payload)) return ActFail(Lang.T("Catatan gagal disimpan"));
                    var meta = NoteFiles.LoadMeta();
                    string key = NoteFiles.KeyOf(note.Path);
                    NoteMeta m;
                    if (!meta.TryGetValue(key, out m) || m == null) { m = new NoteMeta { Key = key }; meta[key] = m; }
                    m.UpdatedUtc = DateTime.UtcNow;
                    NoteFiles.SaveMeta(meta);
                    RefreshNotesUi();
                    return ActOk(IcoNote, Lang.F("Ditambahkan ke catatan: {0}", note.Title));
                }

                case "set_meta":
                case "pin":
                case "unpin":
                case "archive":
                case "unarchive":
                {
                    var note = FindNoteByKeyOrTitle(s("key"), s("title"), true);
                    if (note == null) return ActFail(Lang.T("Catatan tidak ditemukan"));
                    string key = NoteFiles.KeyOf(note.Path);
                    var meta = NoteFiles.LoadMeta();
                    NoteMeta m;
                    if (!meta.TryGetValue(key, out m) || m == null) { m = new NoteMeta { Key = key }; meta[key] = m; }

                    bool? pinned = ArgBool(args, "pinned");
                    if (act == "pin") pinned = true;
                    if (act == "unpin") pinned = false;
                    if (pinned.HasValue)
                    {
                        var pins = NoteFiles.LoadPins();
                        NoteFiles.SetPin(pins, note.Path, pinned.Value);
                    }

                    bool? archived = ArgBool(args, "archived");
                    if (act == "archive") archived = true;
                    if (act == "unarchive") archived = false;
                    if (archived.HasValue) m.Archived = archived.Value;

                    if (args != null && args.ContainsKey("color"))
                    {
                        string color = s("color").ToLowerInvariant();
                        m.Color = Array.IndexOf(NotePalette.Keys, color) >= 0 ? color : "";
                    }
                    if (args != null && args.ContainsKey("labels"))
                        m.Labels = NotePalette.CleanTags(s("labels"));

                    m.UpdatedUtc = DateTime.UtcNow;
                    NoteFiles.SaveMeta(meta);
                    RefreshNotesUi();
                    return ActOk(IcoNote, Lang.F("Catatan diperbarui: {0}", note.Title));
                }

                case "delete":
                case "remove":
                {
                    var note = FindNoteByKeyOrTitle(s("key"), s("title"), true);
                    if (note == null) return ActFail(Lang.T("Catatan tidak ditemukan"));
                    string delKey = NoteFiles.KeyOf(note.Path);
                    var meta = NoteFiles.LoadMeta();
                    NoteMeta m;
                    meta.TryGetValue(delKey, out m);
                    var prevDeletes = NoteFiles.LoadDeletedDrive();
                    if (m != null && !string.IsNullOrEmpty(m.DriveId)) NoteFiles.AddDeletedDrive(m.DriveId);
                    NoteFiles.AddDeletedKey(delKey);
                    if (!NoteFiles.Remove(note.Path))
                    {
                        NoteFiles.SaveDeletedDrive(prevDeletes);
                        return ActFail(Lang.T("Catatan gagal dihapus"));
                    }
                    var pins = NoteFiles.LoadPins();
                    if (NoteFiles.IsPinned(pins, note.Path)) NoteFiles.SetPin(pins, note.Path, false);
                    if (meta.Remove(delKey)) NoteFiles.SaveMeta(meta);
                    RefreshNotesUi();
                    return ActOk(IcoDone, Lang.F("Catatan dihapus: {0}", note.Title));
                }
            }
            return ActFail(Lang.T("Aksi catatan tidak dikenal"));
        }

        // ==================== CALENDAR / SCHEDULE ====================

        ClassSlot FindScheduleSlot(string eventId, string title, bool googleOnly)
        {
            if (classes == null) return null;
            eventId = (eventId ?? "").Trim();
            title = (title ?? "").Trim();
            if (eventId.Length > 0)
            {
                var byId = classes.Find(c => string.Equals(c.Id, eventId, StringComparison.OrdinalIgnoreCase) && (!googleOnly || c.Source == "google"));
                if (byId != null) return byId;
            }
            if (title.Length > 0)
            {
                var exact = classes.Find(c => string.Equals(c.Title.Trim(), title, StringComparison.OrdinalIgnoreCase) && (!googleOnly || c.Source == "google"));
                if (exact != null) return exact;
                var partial = classes.Find(c => c.Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0 && (!googleOnly || c.Source == "google"));
                if (partial != null) return partial;
            }
            return null;
        }

        // Runs on worker thread so HTTP to Google Calendar never blocks the UI
        ChatAct DoManageCalendarWorker(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            if (!GoogleCalendarConnected)
                return ActFail(Lang.T("Google Calendar belum terhubung"));

            if (act == "create" || act == "add")
            {
                string title = s("title");
                if (title.Length == 0) return ActFail(Lang.T("Judul acara kosong"));
                DateTime start;
                bool startHasTime;
                if (!TryParseAskDateTime(s("start"), out start, out startHasTime))
                    return ActFail(Lang.T("Waktu mulai acara tidak jelas"));
                bool allDay = ArgBool(args, "all_day") ?? !startHasTime;
                DateTime end;
                bool endHasTime;
                string endStr = s("end");
                if (endStr.Length > 0 && TryParseAskDateTime(endStr, out end, out endHasTime))
                {
                    if (allDay && end <= start) end = start.Date.AddDays(1);
                }
                else
                {
                    end = allDay ? start.Date.AddDays(1) : start.AddHours(1);
                }
                if (end <= start) end = allDay ? start.Date.AddDays(1) : start.AddHours(1);

                string err;
                bool ok = CreateGoogleCalendarEvent(title, start, end, s("location"), allDay, out err);
                if (!ok) return ActFail(err ?? Lang.T("Gagal menambahkan acara"));
                return ActOk(Ink.IcoCalendar, Lang.F("Acara dibuat: {0}", title));
            }

            if (act == "update" || act == "edit")
            {
                ClassSlot existing = (ClassSlot)Invoke(new Func<ClassSlot>(() => FindScheduleSlot(s("event_id"), s("title"), true)));
                if (existing == null || string.IsNullOrEmpty(existing.Id))
                    return ActFail(Lang.T("Acara Google Calendar tidak ditemukan"));

                string title = s("new_title").Length > 0 ? s("new_title") : existing.Title;
                DateTime start = existing.Start, end = existing.End;
                bool allDay = ArgBool(args, "all_day") ?? existing.AllDay;
                string startStr = s("start");
                bool startHasTime;
                if (startStr.Length > 0)
                {
                    if (!TryParseAskDateTime(startStr, out start, out startHasTime))
                        return ActFail(Lang.T("Waktu mulai acara tidak jelas"));
                    if (!ArgBool(args, "all_day").HasValue) allDay = !startHasTime;
                    if (s("end").Length == 0)
                    {
                        TimeSpan dur = existing.End > existing.Start ? (existing.End - existing.Start) : TimeSpan.FromHours(1);
                        end = allDay ? start.Date.AddDays(1) : start + dur;
                    }
                }
                string endStr = s("end");
                bool endHasTime;
                if (endStr.Length > 0)
                {
                    if (!TryParseAskDateTime(endStr, out end, out endHasTime))
                        return ActFail(Lang.T("Waktu selesai acara tidak jelas"));
                }
                if (end <= start) end = allDay ? start.Date.AddDays(1) : start.AddHours(1);
                string location = args != null && args.ContainsKey("location") ? s("location") : existing.Room;

                string err;
                bool ok = UpdateGoogleCalendarEvent(existing.Id, title, start, end, location, allDay, out err);
                if (!ok) return ActFail(err ?? Lang.T("Gagal memperbarui acara"));
                return ActOk(Ink.IcoCalendar, Lang.F("Acara diperbarui: {0}", title));
            }

            if (act == "delete" || act == "remove")
            {
                ClassSlot existing = (ClassSlot)Invoke(new Func<ClassSlot>(() => FindScheduleSlot(s("event_id"), s("title"), true)));
                if (existing == null || string.IsNullOrEmpty(existing.Id))
                    return ActFail(Lang.T("Acara Google Calendar tidak ditemukan"));
                string err;
                bool ok = DeleteGoogleCalendarEvent(existing.Id, out err);
                if (!ok) return ActFail(err ?? Lang.T("Gagal menghapus acara"));
                return ActOk(IcoDone, Lang.F("Acara dihapus: {0}", existing.Title));
            }

            return ActFail(Lang.T("Aksi kalender tidak dikenal"));
        }

        ChatAct DoManageCalendarUi(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            if (act == "open_link")
            {
                var slot = FindScheduleSlot(s("event_id"), s("title"), false);
                if (slot == null || string.IsNullOrEmpty(slot.Url))
                    return ActFail(Lang.T("Tautan acara tidak ditemukan"));
                OpenUrl(slot.Url);
                return ActOk(Ink.IcoCalendar, Lang.F("Dibuka: {0}", slot.Title));
            }
            return ActFail(Lang.T("Aksi kalender tidak dikenal"));
        }

        // ==================== REMINDERS & WELLNESS & IBADAH ====================

        ChatAct DoManageReminder(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            switch (act)
            {
                case "add":
                case "set":
                {
                    DateTime at;
                    bool hasTime;
                    if (!TryParseAskDateTime(s("at"), out at, out hasTime) || at <= DateTime.Now)
                        return ActFail(Lang.T("Waktu pengingat tidak jelas"));
                    string what = s("text").Length > 0 ? s("text") : Lang.T("Pengingat");
                    var o = AddOneShot(at, what);
                    return new ChatAct { Glyph = Ink.IcoBell, Text = ReminderSaid(at, what), Reminder = o };
                }

                case "cancel":
                case "delete":
                case "remove":
                {
                    string text = s("text");
                    string atStr = s("at");
                    OneShot match = null;
                    foreach (var o in oneShots)
                    {
                        bool okText = text.Length == 0 || o.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                        bool okAt = atStr.Length == 0 || AskStamp(o.At).IndexOf(atStr, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (okText && okAt) { match = o; break; }
                    }
                    if (match == null) return ActFail(Lang.T("Pengingat tidak ditemukan"));
                    string label = match.Text;
                    CancelOneShot(match);
                    return ActOk(IcoDone, Lang.F("Pengingat dibatalkan: {0}", label));
                }

                case "log_drink":
                {
                    Drank();
                    return ActOk(Ink.IcoBell, Lang.F("Minum dicatat ({0}/{1})", DrinkCount(), DrinkGoal));
                }

                case "set_wellness":
                {
                    string kind = s("kind").ToLowerInvariant();
                    bool? on = ArgBool(args, "enabled");
                    int? every = ArgInt(args, "every_minutes");
                    if (kind == "drink" || kind == "minum")
                    {
                        if (on.HasValue) settings.Set("drink", on.Value);
                        if (every.HasValue && every.Value >= 15 && every.Value <= 240) settings.Set("drink.every", every.Value);
                    }
                    else if (kind == "eyes" || kind == "mata")
                    {
                        if (on.HasValue) settings.Set("eyes", on.Value);
                    }
                    else if (kind == "stretch" || kind == "gerak")
                    {
                        if (on.HasValue) settings.Set("stretch", on.Value);
                        if (every.HasValue && every.Value >= 30 && every.Value <= 240) settings.Set("stretch.every", every.Value);
                    }
                    else return ActFail(Lang.T("Jenis pengingat kesehatan tidak dikenal"));
                    settings.Save();
                    Kick();
                    return ActOk(Ink.IcoBell, Lang.T("Pengingat kesehatan diperbarui"));
                }

                case "add_ibadah":
                {
                    string name = s("text").Length > 0 ? s("text") : s("name");
                    string timeStr = s("time");
                    if (name.Length == 0) return ActFail(Lang.T("Nama pengingat ibadah kosong"));
                    DateTime parsed;
                    if (!DateTime.TryParseExact(timeStr, new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                        return ActFail(Lang.T("Jam pengingat tidak jelas (HH:mm)"));
                    string daysStr = s("days").ToLowerInvariant();
                    int days = daysStr == "weekdays" ? IbadahItem.Weekdays : daysStr == "weekend" ? IbadahItem.Weekend : IbadahItem.Daily;
                    int before = Math.Max(0, Math.Min(60, ArgInt(args, "before_minutes") ?? 0));
                    var list = new List<IbadahItem>(Ibadah());
                    if (list.Count >= IbadahMax) return ActFail(Lang.T("Pengingat ibadah sudah penuh"));
                    list.Add(new IbadahItem { Name = name, Minute = parsed.Hour * 60 + parsed.Minute, Days = days, Before = before });
                    SaveIbadah(list);
                    Kick();
                    return ActOk(Ink.IcoBell, Lang.F("Pengingat rutin ditambah: {0} ({1})", name, timeStr));
                }

                case "remove_ibadah":
                {
                    string name = s("text").Length > 0 ? s("text") : s("name");
                    var list = new List<IbadahItem>(Ibadah());
                    int idx = list.FindIndex(x => x.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (idx < 0) return ActFail(Lang.T("Pengingat rutin tidak ditemukan"));
                    string removed = list[idx].Name;
                    list.RemoveAt(idx);
                    SaveIbadah(list);
                    Kick();
                    return ActOk(IcoDone, Lang.F("Pengingat rutin dihapus: {0}", removed));
                }
            }
            return ActFail(Lang.T("Aksi pengingat tidak dikenal"));
        }

        // ==================== TIMER & FOCUS ====================

        ChatAct DoManageTimer(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            string taskTitle = s("task");
            if (taskTitle.Length > 0) SetFocusTask(taskTitle);

            switch (act)
            {
                case "start_countdown":
                {
                    int min = ArgInt(args, "minutes") ?? 0;
                    if (min <= 0 || min > 600) return ActFail(Lang.T("Lama timer tidak jelas"));
                    StartTimer(TimerKind.Countdown, min);
                    return ActOk(Ink.IcoTimer, Lang.F("Timer {0} menit", min));
                }
                case "start_focus":
                {
                    int min = ArgInt(args, "minutes") ?? 25;
                    if (min <= 0 || min > 600) return ActFail(Lang.T("Lama fokus tidak jelas"));
                    StartFocus(min);
                    return ActOk(Ink.IcoTimer, Lang.F("Fokus {0} menit", min));
                }
                case "start_stopwatch":
                    StartTimer(TimerKind.Stopwatch, 0);
                    return ActOk(Ink.IcoTimer, Lang.T("Stopwatch jalan"));
                case "pause":
                    if (!TimerOn) return ActFail(Lang.T("Tidak ada timer yang jalan"));
                    PauseTimer();
                    return ActOk(Ink.IcoTimer, Lang.T("Timer dijeda"));
                case "resume":
                    if (!TimerOn) return ActFail(Lang.T("Tidak ada timer yang jalan"));
                    ResumeTimer();
                    return ActOk(Ink.IcoTimer, Lang.T("Timer dilanjutkan"));
                case "stop":
                    if (!TimerOn) return ActFail(Lang.T("Tidak ada timer yang jalan"));
                    StopTimer();
                    return ActOk(Ink.IcoTimer, Lang.T("Timer dihentikan"));
                case "add_minute":
                    if (!TimerOn) return ActFail(Lang.T("Tidak ada timer yang jalan"));
                    AddTimerMinute();
                    return ActOk(Ink.IcoTimer, Lang.T("Timer ditambah 1 menit"));
                case "lap":
                    if (!TimerOn || timerKind != TimerKind.Stopwatch) return ActFail(Lang.T("Stopwatch tidak sedang jalan"));
                    Lap();
                    return ActOk(Ink.IcoTimer, Lang.T("Putaran stopwatch dicatat"));
            }
            return ActFail(Lang.T("Aksi timer tidak dikenal"));
        }

        // ==================== MEDIA & AUDIO ====================

        ChatAct DoManageMedia(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            switch (act)
            {
                case "toggle":
                case "play":
                case "pause":
                    if (!mediaInfo.Present) return ActFail(Lang.T("Tidak ada media yang aktif"));
                    media.TogglePlay();
                    return ActOk(IcoDone, Lang.T("Perintah media dikirim"));
                case "next":
                    if (!mediaInfo.Present) return ActFail(Lang.T("Tidak ada media yang aktif"));
                    media.Next();
                    return ActOk(IcoDone, Lang.T("Lagu berikutnya"));
                case "previous":
                    if (!mediaInfo.Present) return ActFail(Lang.T("Tidak ada media yang aktif"));
                    media.Previous();
                    return ActOk(IcoDone, Lang.T("Lagu sebelumnya"));
                case "set_volume":
                {
                    int? lvl = ArgInt(args, "level");
                    if (!lvl.HasValue) return ActFail(Lang.T("Level volume tidak jelas"));
                    int clamped = Math.Max(0, Math.Min(100, lvl.Value));
                    float f = clamped / 100f;
                    vol = f;
                    volBar.Target = f;
                    volBar.Snap(f);
                    volume.SetLatest(f);
                    Kick();
                    return ActOk(IcoDone, Lang.F("Volume {0}%", clamped));
                }
                case "mute":
                    volume.SetMute(true);
                    return ActOk(IcoDone, Lang.T("Suara dibisukan"));
                case "unmute":
                    volume.SetMute(false);
                    return ActOk(IcoDone, Lang.T("Suara dinyalakan"));
                case "toggle_mute":
                    volume.SetMute(!muted);
                    return ActOk(IcoDone, !muted ? Lang.T("Suara dibisukan") : Lang.T("Suara dinyalakan"));
                case "toggle_mic":
                    if (!micPresent) return ActFail(Lang.T("Mikrofon tidak ditemukan"));
                    volume.SetMicMute(!micMuted);
                    return ActOk(IcoDone, !micMuted ? Lang.T("Mikrofon dibisukan") : Lang.T("Mikrofon dinyalakan"));
                case "set_mic_mute":
                {
                    if (!micPresent) return ActFail(Lang.T("Mikrofon tidak ditemukan"));
                    bool m = ArgBool(args, "enabled") ?? !micMuted;
                    volume.SetMicMute(m);
                    return ActOk(IcoDone, m ? Lang.T("Mikrofon dibisukan") : Lang.T("Mikrofon dinyalakan"));
                }
                case "switch_output":
                {
                    string devName = s("device");
                    if (devName.Length == 0 || audioDevices == null || audioDevices.Count == 0)
                        return ActFail(Lang.T("Perangkat audio tidak ditemukan"));
                    AudioDevice match = audioDevices.Find(d => d.Name.IndexOf(devName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match == null) return ActFail(Lang.T("Perangkat audio tidak ditemukan"));
                    volume.SetDefault(match.Id);
                    return ActOk(IcoDone, Lang.F("Output audio: {0}", match.Name));
                }
            }
            return ActFail(Lang.T("Aksi media tidak dikenal"));
        }

        // ==================== SYSTEM / CONTROL CENTER ====================

        ChatAct DoManageSystem(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            bool? on = ArgBool(args, "enabled");
            switch (act)
            {
                case "wifi":
                {
                    if (radios == null || !radios.HasWifi) return ActFail(Lang.T("Wi-Fi tidak tersedia"));
                    if (radios.Denied) return ActFail(Lang.T("Izin kontrol Wi-Fi ditolak Windows"));
                    bool target = on.HasValue ? on.Value : !radios.WifiOn;
                    radios.SetPower(true, target);
                    return ActOk(IcoDone, target ? Lang.T("Wi-Fi dinyalakan") : Lang.T("Wi-Fi dimatikan"));
                }
                case "bluetooth":
                {
                    if (radios == null || !radios.HasBluetooth) return ActFail(Lang.T("Bluetooth tidak tersedia"));
                    if (radios.Denied) return ActFail(Lang.T("Izin kontrol Bluetooth ditolak Windows"));
                    bool target = on.HasValue ? on.Value : !radios.BluetoothOn;
                    radios.SetPower(false, target);
                    return ActOk(IcoDone, target ? Lang.T("Bluetooth dinyalakan") : Lang.T("Bluetooth dimatikan"));
                }
                case "brightness":
                {
                    if (brightness == null || !brightness.Supported) return ActFail(Lang.T("Kecerahan layar tidak didukung"));
                    int? lvl = ArgInt(args, "level");
                    if (!lvl.HasValue) return ActFail(Lang.T("Level kecerahan tidak jelas"));
                    int clamped = Math.Max(0, Math.Min(100, lvl.Value));
                    brightLevel = clamped;
                    brightBar.Snap(clamped / 100f);
                    brightness.Set(clamped);
                    Kick();
                    return ActOk(IcoDone, Lang.F("Kecerahan {0}%", clamped));
                }
                case "dnd":
                case "quiet":
                {
                    bool target = on.HasValue ? on.Value : !QuietSwitch;
                    settings.Set("dnd", target);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, target ? Lang.T("Mode senyap aktif") : Lang.T("Mode senyap dimatikan"));
                }
                case "awake":
                {
                    int? min = ArgInt(args, "minutes");
                    if (min.HasValue)
                    {
                        if (min.Value <= 0) SetAwake(TimeSpan.Zero);
                        else SetAwake(TimeSpan.FromMinutes(Math.Min(720, min.Value)));
                    }
                    else if (on.HasValue)
                    {
                        SetAwake(on.Value ? TimeSpan.MaxValue : TimeSpan.Zero);
                    }
                    else
                    {
                        ToggleAwake();
                    }
                    return ActOk(IcoDone, AwakeOn ? Lang.T("Mode terjaga aktif") : Lang.T("Mode terjaga dimatikan"));
                }
                case "dark_mode":
                {
                    bool target = on.HasValue ? on.Value : !winDark;
                    if (target != winDark) ToggleWindowsDark();
                    return ActOk(IcoDone, target ? Lang.T("Mode gelap aktif") : Lang.T("Mode terang aktif"));
                }
                case "island_hide":
                {
                    bool target = on.HasValue ? on.Value : !hiddenByUser;
                    if (target != hiddenByUser) ToggleHidden();
                    return ActOk(IcoDone, target ? Lang.T("Island disembunyikan") : Lang.T("Island ditampilkan"));
                }
            }
            return ActFail(Lang.T("Aksi sistem tidak dikenal"));
        }

        // ==================== SHELF & CLIPBOARD ====================

        ChatAct DoManageShelfClipboard(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            switch (act)
            {
                case "copy_text":
                {
                    string text = Arg(args, "text");
                    if (string.IsNullOrEmpty(text)) return ActFail(Lang.T("Teks kosong"));
                    try { Clipboard.SetText(text); }
                    catch (Exception) { return ActFail(Lang.T("Gagal menyalin ke clipboard")); }
                    return ActOk(IcoCopy, Lang.T("Disalin ke clipboard"));
                }
                case "copy_clip":
                {
                    int? idx = ArgInt(args, "index");
                    ClipItem match = null;
                    if (idx.HasValue && idx.Value >= 0 && idx.Value < clips.Count) match = clips[idx.Value];
                    else
                    {
                        string q = s("query");
                        if (q.Length > 0) match = clips.Find(c => (c.Text ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    if (match == null) return ActFail(Lang.T("Item clipboard tidak ditemukan"));
                    CopyClip(match);
                    return ActOk(IcoCopy, Lang.T("Disalin ke clipboard"));
                }
                case "clear_clips":
                    ClearClips(clips);
                    return ActOk(IcoDone, Lang.T("Riwayat clipboard dibersihkan"));
                case "clear_shelf":
                    ClearShelf();
                    return ActOk(IcoDone, Lang.T("Rak dibersihkan"));
                case "remove_shelf":
                {
                    string q = s("name").Length > 0 ? s("name") : s("id");
                    var item = shelf.Find(x => string.Equals(x.Id.ToString(CultureInfo.InvariantCulture), q, StringComparison.OrdinalIgnoreCase) ||
                        x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (item == null) return ActFail(Lang.T("Item rak tidak ditemukan"));
                    string name = item.Name;
                    RemoveFromShelf(item);
                    return ActOk(IcoDone, Lang.F("Dihapus dari rak: {0}", name));
                }
                case "open_shelf":
                {
                    string q = s("name").Length > 0 ? s("name") : s("id");
                    var item = shelf.Find(x => string.Equals(x.Id.ToString(CultureInfo.InvariantCulture), q, StringComparison.OrdinalIgnoreCase) ||
                        x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (item == null) return ActFail(Lang.T("Item rak tidak ditemukan"));
                    OpenShelfItem(item);
                    return ActOk(IcoDone, Lang.F("Dibuka: {0}", item.Name));
                }
            }
            return ActFail(Lang.T("Aksi rak/clipboard tidak dikenal"));
        }

        // ==================== INTEGRATIONS / SYNC ====================

        ChatAct DoManageIntegrations(Dictionary<string, object> args)
        {
            string act = Arg(args, "action").Trim().ToLowerInvariant();
            switch (act)
            {
                case "sync_all":
                    RefreshAll(0);
                    if (GoogleCalendarConnected) SyncGoogleWorkAsync(null);
                    if (notifWatcher != null) notifWatcher.Refresh();
                    return ActOk(IcoDone, Lang.T("Sinkronisasi semua layanan dimulai"));
                case "sync_google":
                    if (!GoogleCalendarConnected) return ActFail(Lang.T("Akun Google belum terhubung"));
                    RefreshGoogleCalendar();
                    SyncGoogleWorkAsync(null);
                    return ActOk(IcoDone, Lang.T("Sinkronisasi Google dimulai"));
                case "refresh_schedule":
                    RefreshAll(2);
                    return ActOk(Ink.IcoCalendar, Lang.T("Jadwal disegarkan"));
                case "refresh_tasks":
                    RefreshAll(1);
                    if (GoogleCalendarConnected) SyncGoogleWorkAsync(null);
                    return ActOk(Ink.IcoCalendar, Lang.T("Tugas disegarkan"));
                case "refresh_notifs":
                    if (notifWatcher != null) notifWatcher.Refresh();
                    return ActOk(IcoDone, Lang.T("Notifikasi disegarkan"));
            }
            return ActFail(Lang.T("Aksi integrasi tidak dikenal"));
        }

        // ==================== TOOLS & UTILITIES ====================

        ChatAct DoManageTools(Dictionary<string, object> args)
        {
            Func<string, string> s = k => Arg(args, k).Trim();
            string act = s("action").ToLowerInvariant();
            switch (act)
            {
                case "start_prompter":
                {
                    string text = Arg(args, "text");
                    if (text.Trim().Length == 0) { OpenPrompter(); return ActOk(IcoDone, Lang.T("Prompter dibuka")); }
                    StartPrompter(text);
                    return ActOk(IcoDone, Lang.T("Prompter dimulai"));
                }
                case "stop_prompter":
                    StopPrompter();
                    return ActOk(IcoDone, Lang.T("Prompter dihentikan"));
                case "start_rapat":
                    StartRapat();
                    return ActOk(IcoDone, Lang.T("Catatan rapat dimulai"));
                case "stop_rapat":
                    StopRapat();
                    return ActOk(IcoDone, Lang.T("Catatan rapat dihentikan"));
                case "open_color_picker":
                    OpenPicker();
                    return ActOk(IcoDone, Lang.T("Pemilih warna dibuka"));
                case "open_quick_note":
                    OpenNote();
                    return ActOk(IcoNote, Lang.T("Catatan kilat dibuka"));
                case "tidy_downloads":
                    RunTidy();
                    return ActOk(IcoDone, Lang.T("Folder unduhan dirapikan"));
                case "undo_tidy":
                    UndoTidy();
                    return ActOk(IcoDone, Lang.T("Perapian unduhan dibatalkan"));
            }
            return ActFail(Lang.T("Aksi alat tidak dikenal"));
        }

        // ==================== SETTINGS ====================

        ChatAct DoManageSettings(Dictionary<string, object> args)
        {
            string setting = Arg(args, "setting").Trim().ToLowerInvariant();
            string val = Arg(args, "value").Trim();
            bool? on = ArgBool(args, "value");

            switch (setting)
            {
                case "language":
                case "lang":
                {
                    bool eng = val.Equals("en", StringComparison.OrdinalIgnoreCase) || val.Equals("english", StringComparison.OrdinalIgnoreCase);
                    SetLanguage(eng);
                    return ActOk(IcoDone, eng ? "Language set to English" : "Bahasa diubah ke Indonesia");
                }
                case "dock":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("dock", on.Value);
                    settings.Save();
                    ApplyDock();
                    return ActOk(IcoDone, on.Value ? Lang.T("Dock dinyalakan") : Lang.T("Dock dimatikan"));
                case "pet":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("pet", on.Value);
                    settings.Save();
                    ApplyPet();
                    return ActOk(IcoDone, on.Value ? Lang.T("Pet dinyalakan") : Lang.T("Pet dimatikan"));
                case "island_eyes":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("island.eyes", on.Value);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, on.Value ? Lang.T("Mata island dinyalakan") : Lang.T("Mata island dimatikan"));
                case "shelf":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("shelf", on.Value);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, on.Value ? Lang.T("Rak dinyalakan") : Lang.T("Rak dimatikan"));
                case "clipboard":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("clips", on.Value);
                    settings.Save();
                    ApplyClips();
                    return ActOk(IcoDone, on.Value ? Lang.T("Clipboard dinyalakan") : Lang.T("Clipboard dimatikan"));
                case "ingat":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("ingat", on.Value);
                    settings.Save();
                    ApplyIngat();
                    return ActOk(IcoDone, on.Value ? Lang.T("Ingatan dinyalakan") : Lang.T("Ingatan dimatikan"));
                case "pray":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("pray", on.Value);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, on.Value ? Lang.T("Jadwal sholat dinyalakan") : Lang.T("Jadwal sholat dimatikan"));
                case "pray_city":
                {
                    if (val.Length == 0) return ActFail(Lang.T("Nama kota kosong"));
                    var found = Cities.Find(val);
                    if (found == null) return ActFail(Lang.F("Kota tidak ditemukan: {0}", val));
                    settings.Set("pray.city", Convert.ToString(found[0], CultureInfo.InvariantCulture));
                    settings.Set("pray", true);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, Lang.F("Kota jadwal sholat: {0}", Convert.ToString(found[0], CultureInfo.InvariantCulture)));
                }
                case "drink_remind":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("drink", on.Value);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, on.Value ? Lang.T("Pengingat minum dinyalakan") : Lang.T("Pengingat minum dimatikan"));
                case "eyes_remind":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("eyes", on.Value);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, on.Value ? Lang.T("Pengingat mata dinyalakan") : Lang.T("Pengingat mata dimatikan"));
                case "stretch_remind":
                    if (!on.HasValue) return ActFail(Lang.T("Nilai pengaturan tidak jelas"));
                    settings.Set("stretch", on.Value);
                    settings.Save();
                    Kick();
                    return ActOk(IcoDone, on.Value ? Lang.T("Pengingat peregangan dinyalakan") : Lang.T("Pengingat peregangan dimatikan"));
            }
            return ActFail(Lang.T("Pengaturan tidak dikenal"));
        }
    }
}
