using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deskmate
{
    static class GoogleWorkspaceHttp
    {
        public static bool JsonBool(object o, string key)
        {
            return string.Equals(Json.Str(o, key), "true", StringComparison.OrdinalIgnoreCase);
        }

        // Offline checks replace the network with this (method, url, body) -> response
        internal static Func<string, string, string, FetchResult> Transport { get; set; }

        public static FetchResult SendJson(string method, string url, string accessToken, string jsonBody, string contentType)
        {
            var fake = Transport;
            if (fake != null) return fake(method, url, jsonBody);
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + accessToken },
                { "Accept", "application/json" }
            };
            if (!string.IsNullOrEmpty(contentType)) headers["Content-Type"] = contentType;
            return Deskmate.Fetch.Send(method, url, headers, jsonBody, Deskmate.Fetch.JsonCap, 20000);
        }

        public static FetchResult SendWithRefresh(ref GoogleCalVault vault, string method, string url, string jsonBody, string contentType, out string error)
        {
            error = null;
            FetchResult r = SendJson(method, url, vault.AccessToken, jsonBody, contentType);
            if (r.Status == 401)
            {
                SourceResult refreshErr = GoogleCalendarSource.RefreshAccessTokenStatic(vault);
                if (refreshErr != null)
                {
                    error = refreshErr.Error ?? Lang.T("Token Google ditolak");
                    return r;
                }
                r = SendJson(method, url, vault.AccessToken, jsonBody, contentType);
            }
            if (r.Status == 403)
            {
                error = Lang.T("Izin Google Tasks/Drive belum aktif — putuskan lalu sambungkan ulang di Pengaturan › Integrasi.");
                return r;
            }
            return r;
        }
    }

    static class GoogleTasksClient
    {
        const string ListsRoot = "https://tasks.googleapis.com/tasks/v1/users/@me/lists";
        const string ListsEndpoint = ListsRoot + "?maxResults=100";
        const string TasksBase = "https://tasks.googleapis.com/tasks/v1/lists/";

        static DateTime ParseUtc(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                return d.ToUniversalTime();
            return DateTime.MinValue;
        }

        static DateTime? ParseDueLocal(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                return d.ToLocalTime().Date;
            return null;
        }

        static string FormatDueRfc3339(DateTime? due)
        {
            if (!due.HasValue) return null;
            return due.Value.Date.ToString("yyyy-MM-dd'T'00:00:00.000'Z'", CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> BuildTaskPayload(TodoItem t)
        {
            var d = new Dictionary<string, object>();
            d["title"] = t.Title ?? "";
            d["notes"] = t.Notes ?? "";
            d["status"] = t.Done ? "completed" : "needsAction";
            // Reopening a task needs its completion stamp cleared as well
            if (!t.Done) d["completed"] = null;
            if (t.Due.HasValue) d["due"] = FormatDueRfc3339(t.Due);
            else d["due"] = null;
            return d;
        }

        // Every page of a collection; false when a page failed, fatal carries an auth-level error
        static bool GetPages(GoogleCalVault vault, string url, List<object> items, out string fatal)
        {
            fatal = null;
            string token = null;
            for (int page = 0; page < 50; page++)
            {
                string u = string.IsNullOrEmpty(token) ? url : url + "&pageToken=" + Uri.EscapeDataString(token);
                FetchResult r = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET", u, null, null, out fatal);
                if (fatal != null || !r.Ok) return false;
                object parsed = null;
                try { parsed = Json.Parse(r.Text); }
                catch (Exception) { }
                if (parsed == null) return false;
                foreach (var it in Json.Arr(parsed, "items")) items.Add(it);
                token = Json.Str(parsed, "nextPageToken");
                if (string.IsNullOrEmpty(token)) return true;
            }
            return false;
        }

        static List<KeyValuePair<string, string>> FetchLists(ref GoogleCalVault vault, out string error)
        {
            bool real;
            return FetchLists(vault, out error, out real);
        }

        // real: the account answered with its lists; false when the "@default" stand-in was used
        static List<KeyValuePair<string, string>> FetchLists(GoogleCalVault vault, out string error, out bool real)
        {
            var lists = new List<KeyValuePair<string, string>>();
            real = false;
            var items = new List<object>();
            if (!GetPages(vault, ListsEndpoint, items, out error))
            {
                if (error == null) error = Lang.T("Gagal memuat Google Tasks");
                return lists;
            }
            foreach (var item in items)
            {
                string id = Json.Str(item, "id");
                string title = Json.Str(item, "title");
                if (!string.IsNullOrEmpty(id))
                    lists.Add(new KeyValuePair<string, string>(id, string.IsNullOrEmpty(title) ? "Tasks" : title));
            }
            real = lists.Count > 0;
            if (lists.Count == 0)
                lists.Add(new KeyValuePair<string, string>("@default", "Tasks"));
            return lists;
        }

        // Two-way sync of tasks and task lists. true when the run completed (error = first per-item
        // failure, or null); false only on a fatal failure. changed: local data was modified
        public static bool Sync(List<TodoItem> local, List<string[]> pendingDeletes, List<TaskListInfo> lists, List<string> pendingListDeletes, out bool changed, out string error)
        {
            changed = false;
            error = null;
            if (local == null) return false;
            GoogleCalVault vault = GoogleCalendarSource.EnsureVaultToken(out error);
            if (vault == null) return false;
            return SyncWith(vault, local, pendingDeletes, lists, pendingListDeletes, out changed, out error);
        }

        // Token already in hand: everything below talks to Google through GoogleWorkspaceHttp only
        internal static bool SyncWith(GoogleCalVault vault, List<TodoItem> local, List<string[]> pendingDeletes, List<TaskListInfo> lists, List<string> pendingListDeletes, out bool changed, out string error)
        {
            var run = new TasksRun { Vault = vault, Local = local, Lists = lists ?? new List<TaskListInfo>() };
            bool ok = run.Go(pendingDeletes, pendingListDeletes);
            changed = run.Changed;
            error = run.Fatal ?? run.FirstErr;
            return ok;
        }

        sealed class TasksRun
        {
            public GoogleCalVault Vault;
            public List<TodoItem> Local;
            public List<TaskListInfo> Lists;
            public bool Changed;
            public string Fatal, FirstErr;

            // Local list name -> Google list id, and back
            readonly Dictionary<string, string> idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, string> nameById = new Dictionary<string, string>(StringComparer.Ordinal);
            string defaultTarget = "@default";

            void Soft() { if (FirstErr == null) FirstErr = Lang.T("Gagal menyimpan ke Google Tasks"); }

            // null on an auth-level failure (Fatal is set)
            FetchResult Call(string method, string url, string body)
            {
                string err;
                FetchResult r = GoogleWorkspaceHttp.SendWithRefresh(ref Vault, method, url, body, body != null ? "application/json; charset=utf-8" : null, out err);
                if (err != null) { Fatal = err; return null; }
                return r;
            }

            static bool Gone(FetchResult r) { return r.Status == 404 || r.Status == 410; }

            static string NewId(FetchResult r)
            {
                try
                {
                    object o = Json.Parse(r.Text);
                    return o != null ? Json.Str(o, "id") : "";
                }
                catch (Exception) { return ""; }
            }

            static string TasksOf(string listId) { return TasksBase + Uri.EscapeDataString(listId) + "/tasks"; }

            public bool Go(List<string[]> pendingDeletes, List<string> pendingListDeletes)
            {
                if (!SendPendingDeletes(pendingDeletes, pendingListDeletes)) return false;

                // Lists
                bool real;
                string err;
                List<KeyValuePair<string, string>> remote = FetchLists(Vault, out err, out real);
                if (err != null) { Fatal = err; return false; }
                string defaultRemote = "";
                if (real)
                {
                    FetchResult dr = Call("GET", ListsRoot + "/@default", null);
                    if (dr == null) return false;
                    if (dr.Ok)
                    {
                        try { object o = Json.Parse(dr.Text); defaultRemote = o != null ? Json.Str(o, "id") : ""; }
                        catch (Exception) { }
                    }
                }
                defaultTarget = defaultRemote.Length > 0 ? defaultRemote : remote[0].Key;
                // Google's default list is "Tugas Saya" here whatever its own title says
                if (defaultRemote.Length > 0)
                    foreach (var t in Local)
                        if (t.GoogleListId == defaultRemote && !t.Dirty && !TaskLists.IsDefault(TaskLists.NameOf(t)))
                        {
                            var old = TaskLists.Find(Lists, TaskLists.NameOf(t));
                            if (old != null && string.IsNullOrEmpty(old.GoogleId)) Lists.Remove(old);
                            t.ListName = TaskLists.Default;
                            Changed = true;
                        }
                TaskLists.Ensure(Lists, Local);
                if (!ReconcileLists(remote, defaultRemote, real)) return false;
                foreach (var e in Lists)
                    if (!string.IsNullOrEmpty(e.GoogleId)) { idByName[e.Name] = e.GoogleId; nameById[e.GoogleId] = e.Name; }
                var def = TaskLists.Find(Lists, TaskLists.Default);
                if (def != null && !string.IsNullOrEmpty(def.GoogleId)) defaultTarget = def.GoogleId;

                // Remote tasks, page by page
                var remoteById = new Dictionary<string, object>(StringComparer.Ordinal);
                var remoteList = new Dictionary<string, string>(StringComparer.Ordinal);
                var complete = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in remote)
                {
                    if (!nameById.ContainsKey(kv.Key)) continue;
                    var items = new List<object>();
                    string fatal;
                    bool ok = GetPages(Vault, TasksOf(kv.Key) + "?showCompleted=true&showHidden=true&maxResults=100", items, out fatal);
                    if (fatal != null) { Fatal = fatal; return false; }
                    if (!ok) { if (FirstErr == null) FirstErr = Lang.T("Gagal memuat Google Tasks"); continue; }
                    complete.Add(kv.Key);
                    foreach (var item in items)
                    {
                        if (GoogleWorkspaceHttp.JsonBool(item, "deleted")) continue;
                        string tid = Json.Str(item, "id");
                        if (string.IsNullOrEmpty(tid)) continue;
                        remoteById[tid] = item;
                        remoteList[tid] = kv.Key;
                    }
                }

                // Gone from Google (and untouched here) means deleted there
                var gone = new List<TodoItem>();
                foreach (var t in Local)
                    if (!string.IsNullOrEmpty(t.GoogleTaskId) && !t.Dirty && !t.Moved && complete.Contains(t.GoogleListId) && !remoteById.ContainsKey(t.GoogleTaskId))
                        gone.Add(t);
                if (gone.Count > 0)
                {
                    foreach (var t in gone) Local.Remove(t);
                    Changed = true;
                }

                var pushed = new HashSet<string>();
                if (!Push(pushed)) return false;
                Pull(remoteById, remoteList, pushed);
                return true;
            }

            bool SendPendingDeletes(List<string[]> pendingDeletes, List<string> pendingListDeletes)
            {
                if (pendingDeletes != null)
                    for (int i = pendingDeletes.Count - 1; i >= 0; i--)
                    {
                        string[] pair = pendingDeletes[i];
                        if (pair == null || pair.Length < 2 || string.IsNullOrEmpty(pair[1])) { pendingDeletes.RemoveAt(i); continue; }
                        string listId = string.IsNullOrEmpty(pair[0]) ? "@default" : pair[0];
                        FetchResult dr = Call("DELETE", TasksOf(listId) + "/" + Uri.EscapeDataString(pair[1]), null);
                        if (dr == null) return false;
                        if (dr.Ok || Gone(dr)) { pendingDeletes.RemoveAt(i); Changed = true; }
                        else Soft();
                    }
                if (pendingListDeletes != null)
                    for (int i = pendingListDeletes.Count - 1; i >= 0; i--)
                    {
                        string id = pendingListDeletes[i];
                        if (string.IsNullOrEmpty(id)) { pendingListDeletes.RemoveAt(i); continue; }
                        FetchResult dr = Call("DELETE", ListsRoot + "/" + Uri.EscapeDataString(id), null);
                        if (dr == null) return false;
                        if (dr.Ok || Gone(dr)) { pendingListDeletes.RemoveAt(i); Changed = true; }
                        else Soft();
                    }
                return true;
            }

            // Registry <-> Google lists: adopt, rename, create, and drop lists deleted on the other side
            bool ReconcileLists(List<KeyValuePair<string, string>> remote, string defaultRemote, bool real)
            {
                var remoteIds = new HashSet<string>(StringComparer.Ordinal);
                var def = TaskLists.Find(Lists, TaskLists.Default);
                foreach (var kv in remote)
                {
                    remoteIds.Add(kv.Key);
                    TaskListInfo e = null;
                    foreach (var x in Lists) if (x.GoogleId == kv.Key) { e = x; break; }
                    bool isDefault = kv.Key == defaultRemote && def != null;
                    if (e == null)
                    {
                        e = isDefault ? def : null;
                        if (e == null)
                        {
                            var byName = TaskLists.Find(Lists, kv.Value);
                            if (byName != null && string.IsNullOrEmpty(byName.GoogleId) && !TaskLists.IsDefault(byName.Name)) e = byName;
                        }
                        if (e != null) { e.GoogleId = kv.Key; e.Dirty = false; Changed = true; }
                        else
                        {
                            string name = TaskLists.Clean(kv.Value);
                            if (name.Length == 0) name = "Tasks";
                            string unique = name;
                            for (int n = 2; TaskLists.Find(Lists, unique) != null; n++) unique = name + " (" + n + ")";
                            Lists.Add(new TaskListInfo { Name = unique, GoogleId = kv.Key });
                            Changed = true;
                        }
                    }
                    else if (!isDefault && !TaskLists.IsDefault(e.Name))
                    {
                        if (e.Dirty)
                        {
                            if (e.Name == kv.Value) e.Dirty = false;
                            else
                            {
                                var body = new Dictionary<string, object>();
                                body["title"] = e.Name;
                                FetchResult pr = Call("PATCH", ListsRoot + "/" + Uri.EscapeDataString(kv.Key), Json.Encode(body));
                                if (pr == null) return false;
                                if (pr.Ok) { e.Dirty = false; Changed = true; }
                                else Soft();
                            }
                        }
                        else if (TaskLists.Clean(kv.Value).Length > 0 && e.Name != TaskLists.Clean(kv.Value))
                        {
                            e.Name = TaskLists.Clean(kv.Value);
                            Changed = true;
                        }
                    }
                }

                foreach (var e in Lists.ToArray())
                {
                    bool isDefault = TaskLists.IsDefault(e.Name);
                    if (string.IsNullOrEmpty(e.GoogleId))
                    {
                        if (isDefault) continue;
                        var body = new Dictionary<string, object>();
                        body["title"] = e.Name;
                        FetchResult cr = Call("POST", ListsRoot, Json.Encode(body));
                        if (cr == null) return false;
                        string id = cr.Ok ? NewId(cr) : "";
                        if (id.Length > 0) { e.GoogleId = id; e.Dirty = false; Changed = true; }
                        else Soft();
                    }
                    else if (real && !remoteIds.Contains(e.GoogleId))
                    {
                        // The list no longer exists on Google
                        string dead = e.GoogleId;
                        bool keep = isDefault || e.Dirty;
                        foreach (var t in Local.ToArray())
                        {
                            if (t.GoogleListId != dead) continue;
                            if (!t.Dirty && !t.Moved && !keep) Local.Remove(t);
                            else { t.GoogleTaskId = ""; t.GoogleListId = ""; t.Dirty = true; keep = true; }
                        }
                        e.GoogleId = "";
                        if (!keep) Lists.Remove(e);
                        Changed = true;
                    }
                }
                return true;
            }

            string TargetList(TodoItem t)
            {
                string id;
                if (idByName.TryGetValue(TaskLists.NameOf(t), out id)) return id;
                if (!string.IsNullOrEmpty(t.GoogleListId) && nameById.ContainsKey(t.GoogleListId)) return t.GoogleListId;
                return defaultTarget;
            }

            // Remote parent and previous sibling for t, from local order. pending: parent not on Google yet
            void Place(TodoItem t, string list, out string parent, out string previous, out bool pending)
            {
                parent = ""; previous = ""; pending = false;
                if (!string.IsNullOrEmpty(t.ParentId) && t.ParentId != t.Id)
                {
                    var p = Local.Find(x => x.Id == t.ParentId);
                    if (p != null && TaskLists.Same(TaskLists.NameOf(p), TaskLists.NameOf(t)))
                    {
                        if (!string.IsNullOrEmpty(p.GoogleTaskId) && (p.GoogleListId == list || string.IsNullOrEmpty(p.GoogleListId))) parent = p.GoogleTaskId;
                        else pending = true;
                    }
                }
                var sib = Local.FindAll(x => x.Id != t.Id && TaskLists.Same(TaskLists.NameOf(x), TaskLists.NameOf(t)) && (x.ParentId ?? "") == (t.ParentId ?? ""));
                sib.Add(t);
                sib.Sort(TaskTree.CompareManual);
                for (int i = sib.IndexOf(t) - 1; i >= 0; i--)
                    if (!string.IsNullOrEmpty(sib[i].GoogleTaskId) && sib[i].GoogleListId == list) { previous = sib[i].GoogleTaskId; break; }
            }

            static string Query(string parent, string previous, string dest)
            {
                var q = new List<string>();
                if (!string.IsNullOrEmpty(dest)) q.Add("destinationTasklist=" + Uri.EscapeDataString(dest));
                if (!string.IsNullOrEmpty(parent)) q.Add("parent=" + Uri.EscapeDataString(parent));
                if (!string.IsNullOrEmpty(previous)) q.Add("previous=" + Uri.EscapeDataString(previous));
                return q.Count == 0 ? "" : "?" + string.Join("&", q.ToArray());
            }

            // Parents first, so a subtask can name its parent's Google id
            bool Push(HashSet<string> pushed)
            {
                var todo = Local.FindAll(t => string.IsNullOrEmpty(t.GoogleTaskId) || t.Dirty || t.Moved);
                todo.Sort(delegate(TodoItem a, TodoItem b)
                {
                    int c = (string.IsNullOrEmpty(a.ParentId) ? 0 : 1).CompareTo(string.IsNullOrEmpty(b.ParentId) ? 0 : 1);
                    return c != 0 ? c : TaskTree.CompareManual(a, b);
                });
                foreach (var t in todo)
                {
                    string imageError;
                    if ((t.Notes ?? "").Length > 8192)
                    {
                        if (FirstErr == null) FirstErr = Lang.T("Deskripsi Google Tasks melebihi 8192 karakter");
                        continue;
                    }
                    if (!TaskNoteImages.Sync(ref Vault, t.Notes, true, out imageError))
                    {
                        if (FirstErr == null) FirstErr = imageError;
                        continue; // Keep Dirty: never publish a task before its images are available.
                    }
                    int r = string.IsNullOrEmpty(t.GoogleTaskId) ? Create(t, pushed) : Update(t, pushed);
                    if (r < 0) return false;
                }
                return true;
            }

            // 1 done, 0 soft failure or skipped, -1 fatal
            int Create(TodoItem t, HashSet<string> pushed)
            {
                string list = TargetList(t), parent, previous;
                bool pending;
                Place(t, list, out parent, out previous, out pending);
                if (pending) return 0;
                FetchResult pr = Call("POST", TasksOf(list) + Query(parent, previous, null), Json.Encode(BuildTaskPayload(t)));
                if (pr == null) return -1;
                string id = pr.Ok ? NewId(pr) : "";
                if (id.Length == 0) { Soft(); return 0; }
                t.GoogleTaskId = id;
                t.GoogleListId = list;
                t.Dirty = false;
                t.Moved = false;
                t.UpdatedUtc = DateTime.UtcNow;
                pushed.Add(t.Id);
                Changed = true;
                return 1;
            }

            int Update(TodoItem t, HashSet<string> pushed)
            {
                string want = TargetList(t);
                if (string.IsNullOrEmpty(t.GoogleListId)) t.GoogleListId = want;
                bool across = want != t.GoogleListId;
                if (across || t.Moved)
                {
                    string parent, previous;
                    bool pending;
                    Place(t, want, out parent, out previous, out pending);
                    if (pending) { parent = ""; }
                    FetchResult mr = Call("POST", TasksOf(t.GoogleListId) + "/" + Uri.EscapeDataString(t.GoogleTaskId) + "/move" + Query(parent, previous, across ? want : null), "");
                    if (mr == null) return -1;
                    if (Gone(mr)) { t.GoogleTaskId = ""; t.GoogleListId = ""; return Create(t, pushed); }
                    if (!mr.Ok) { Soft(); return 0; }
                    string moved = NewId(mr);
                    if (moved.Length > 0) t.GoogleTaskId = moved;
                    t.GoogleListId = want;
                    t.Moved = false;
                    pushed.Add(t.Id);
                    Changed = true;
                }
                if (t.Dirty)
                {
                    FetchResult pr = Call("PATCH", TasksOf(t.GoogleListId) + "/" + Uri.EscapeDataString(t.GoogleTaskId), Json.Encode(BuildTaskPayload(t)));
                    if (pr == null) return -1;
                    if (Gone(pr)) { t.GoogleTaskId = ""; t.GoogleListId = ""; return Create(t, pushed); }
                    if (!pr.Ok) { Soft(); return 0; }
                    t.Dirty = false;
                    t.UpdatedUtc = DateTime.UtcNow;
                    pushed.Add(t.Id);
                    Changed = true;
                }
                return 1;
            }

            static int ByPosition(KeyValuePair<string, string> a, KeyValuePair<string, string> b)
            {
                int c = a.Key.Length.CompareTo(b.Key.Length);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            }

            void Pull(Dictionary<string, object> remoteById, Dictionary<string, string> remoteList, HashSet<string> pushed)
            {
                var byRemote = new Dictionary<string, TodoItem>(StringComparer.Ordinal);
                foreach (var t in Local) if (!string.IsNullOrEmpty(t.GoogleTaskId)) byRemote[t.GoogleTaskId] = t;

                foreach (var kv in remoteById)
                {
                    string tid = kv.Key;
                    object item = kv.Value;
                    string rListId = remoteList[tid];
                    string rListName = nameById.ContainsKey(rListId) ? nameById[rListId] : "Tasks";
                    string rTitle = Json.Str(item, "title");
                    string rNotes = Json.Str(item, "notes");
                    string imageError;
                    if (!TaskNoteImages.Sync(ref Vault, rNotes, false, out imageError) && FirstErr == null)
                        FirstErr = imageError; // Preserve Markdown even when an image is unavailable; retry next sync.
                    bool rDone = Json.Str(item, "status") == "completed";
                    DateTime? rDue = ParseDueLocal(Json.Str(item, "due"));
                    DateTime rUpdated = ParseUtc(Json.Str(item, "updated"));
                    DateTime? rDoneAt = null;
                    DateTime comp = ParseUtc(Json.Str(item, "completed"));
                    if (comp > DateTime.MinValue) rDoneAt = comp.ToLocalTime();

                    TodoItem existing;
                    if (!byRemote.TryGetValue(tid, out existing))
                    {
                        if (string.IsNullOrEmpty(rTitle)) continue;
                        var nt = new TodoItem
                        {
                            Title = rTitle,
                            Notes = rNotes ?? "",
                            ListName = rListName,
                            Done = rDone,
                            DoneAt = rDone ? (rDoneAt ?? DateTime.Now) : (DateTime?)null,
                            Star = false,
                            Due = rDue,
                            GoogleTaskId = tid,
                            GoogleListId = rListId,
                            Dirty = false,
                            UpdatedUtc = rUpdated > DateTime.MinValue ? rUpdated : DateTime.UtcNow
                        };
                        Local.Add(nt);
                        byRemote[tid] = nt;
                        Changed = true;
                    }
                    else if (!pushed.Contains(existing.Id) && (!existing.Dirty || (rUpdated > existing.UpdatedUtc)))
                    {
                        bool itemChanged = false;
                        if (!string.IsNullOrEmpty(rTitle) && existing.Title != rTitle) { existing.Title = rTitle; itemChanged = true; }
                        if ((existing.Notes ?? "") != (rNotes ?? "")) { existing.Notes = rNotes ?? ""; itemChanged = true; }
                        if (!TaskLists.Same(existing.ListName, rListName)) { existing.ListName = rListName; itemChanged = true; }
                        if (existing.GoogleListId != rListId) { existing.GoogleListId = rListId; itemChanged = true; }
                        if (existing.Done != rDone)
                        {
                            existing.Done = rDone;
                            existing.DoneAt = rDone ? (rDoneAt ?? DateTime.Now) : (DateTime?)null;
                            itemChanged = true;
                        }
                        if (rDue.HasValue)
                        {
                            if (!existing.Due.HasValue)
                            {
                                existing.Due = rDue;
                                itemChanged = true;
                            }
                            else if (existing.Due.Value.Date != rDue.Value.Date)
                            {
                                existing.Due = rDue.Value.Date.Add(existing.Due.Value.TimeOfDay);
                                itemChanged = true;
                            }
                        }
                        else if (existing.Due.HasValue)
                        {
                            existing.Due = null;
                            itemChanged = true;
                        }
                        if (itemChanged)
                        {
                            existing.UpdatedUtc = rUpdated > DateTime.MinValue ? rUpdated : DateTime.UtcNow;
                            existing.Dirty = false;
                            Changed = true;
                        }
                    }
                }

                // Structure: parent and rank among siblings, as Google ordered them
                var groups = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
                foreach (var kv in remoteById)
                {
                    string key = remoteList[kv.Key] + "|" + Json.Str(kv.Value, "parent");
                    List<KeyValuePair<string, string>> g;
                    if (!groups.TryGetValue(key, out g)) groups[key] = g = new List<KeyValuePair<string, string>>();
                    g.Add(new KeyValuePair<string, string>(Json.Str(kv.Value, "position"), kv.Key));
                }
                foreach (var kv in groups)
                {
                    kv.Value.Sort(ByPosition);
                    for (int rank = 0; rank < kv.Value.Count; rank++)
                    {
                        string tid = kv.Value[rank].Value;
                        TodoItem t;
                        if (!byRemote.TryGetValue(tid, out t) || pushed.Contains(t.Id) || t.Dirty || t.Moved) continue;
                        string rParent = Json.Str(remoteById[tid], "parent");
                        TodoItem p;
                        string parentLocal = rParent.Length > 0 && byRemote.TryGetValue(rParent, out p) ? p.Id : "";
                        if ((t.ParentId ?? "") != parentLocal) { t.ParentId = parentLocal; Changed = true; }
                        if (t.Position != rank) { t.Position = rank; Changed = true; }
                    }
                }
            }
        }

        public static List<KuliaTask> FetchNotchTasks(out string error)
        {
            var result = new List<KuliaTask>();
            GoogleCalVault vault = GoogleCalendarSource.EnsureVaultToken(out error);
            if (vault == null) return result;

            List<KeyValuePair<string, string>> lists = FetchLists(ref vault, out error);
            if (error != null) return result;

            foreach (var kv in lists)
            {
                string url = TasksBase + Uri.EscapeDataString(kv.Key) + "/tasks?showCompleted=false&showHidden=false&maxResults=100";
                FetchResult tr = GoogleWorkspaceHttp.SendWithRefresh(ref vault, "GET", url, null, null, out error);
                if (error != null) return result;
                if (!tr.Ok) continue;
                object parsed = null;
                try { parsed = Json.Parse(tr.Text); }
                catch (Exception) { }
                object[] items = parsed != null ? Json.Arr(parsed, "items") : null;
                if (items == null || items.Length == 0) continue;
                foreach (var item in items)
                {
                    if (GoogleWorkspaceHttp.JsonBool(item, "deleted")) continue;
                    if (Json.Str(item, "status") == "completed") continue;
                    string title = Json.Str(item, "title");
                    if (string.IsNullOrEmpty(title)) continue;
                    DateTime? due = ParseDueLocal(Json.Str(item, "due"));
                    if (!due.HasValue) continue;
                    result.Add(new KuliaTask
                    {
                        Source = "gtasks",
                        Id = Json.Str(item, "id"),
                        Title = title,
                        DeadlineUtc = due.Value.Date.AddHours(23).AddMinutes(59).ToUniversalTime(),
                        ReadOnly = true
                    });
                }
            }
            return result;
        }
    }
}
