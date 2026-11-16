using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace Deskmate
{
    // One task in the work window
    sealed class TodoItem
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Title = "";
        public bool Done, Star;
        public DateTime? Due;
        public DateTime Created = DateTime.Now;
        public DateTime? DoneAt;
        public string ListName = "Tugas Saya", Notes = "", GoogleTaskId = "", GoogleListId = "";
        public DateTime UpdatedUtc = DateTime.UtcNow;
        public bool Dirty = true;
        // Subtask of the task with this Id (Google Tasks "parent"); one level deep
        public string ParentId = "";
        // Rank among siblings in "my order"
        public int Position;
        // Parent or rank changed locally, not yet sent to Google
        public bool Moved;
        public override string ToString() { return Title; }
    }

    // tugas.tsv: id, done, due, star, created, doneAt, title; plain text on purpose
    static class TodoFile
    {
        const string Stamp = "yyyy-MM-ddTHH:mm";
        static string FilePath { get { return Path.Combine(Settings.Dir, "tugas.tsv"); } }
        static string DeletedPath { get { return Path.Combine(Settings.Dir, "tugas-deleted.tsv"); } }
        const string UtcStamp = "yyyy-MM-ddTHH:mm:ssZ";

        static DateTime? Parse(string s)
        {
            DateTime d;
            return s.Length > 0 && DateTime.TryParseExact(s, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (DateTime?)d : null;
        }

        static DateTime ParseUtc(string s)
        {
            DateTime d;
            if (s.Length > 0 && DateTime.TryParseExact(s, UtcStamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d)) return d;
            return DateTime.UtcNow;
        }

        static string Put(DateTime? d) { return d.HasValue ? d.Value.ToString(Stamp, CultureInfo.InvariantCulture) : ""; }
        static string PutUtc(DateTime d) { return d.ToUniversalTime().ToString(UtcStamp, CultureInfo.InvariantCulture); }

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

        public static List<TodoItem> Load()
        {
            var list = new List<TodoItem>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    if (f.Length < 7 || f[6].Trim().Length == 0) continue;
                    var t = new TodoItem { Id = f[0], Done = f[1] == "1", Due = Parse(f[2]), Star = f[3] == "1", Title = f[6], Dirty = false };
                    var made = Parse(f[4]);
                    if (made.HasValue) t.Created = made.Value;
                    t.DoneAt = Parse(f[5]);
                    if (f.Length >= 8 && f[7].Length > 0) t.ListName = Unesc(f[7]);
                    if (f.Length >= 9) t.GoogleTaskId = Unesc(f[8]);
                    if (f.Length >= 10) t.GoogleListId = Unesc(f[9]);
                    if (f.Length >= 11) t.UpdatedUtc = ParseUtc(f[10]);
                    if (f.Length >= 12) t.Dirty = f[11] == "1";
                    if (f.Length >= 13) t.Notes = Unesc(f[12]);
                    if (f.Length >= 14) t.ParentId = Unesc(f[13]);
                    int pos;
                    if (f.Length >= 15 && int.TryParse(f[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out pos)) t.Position = pos;
                    if (f.Length >= 16) t.Moved = f[15] == "1";
                    list.Add(t);
                }
            }
            catch (Exception ex) { Log.Write("tugas: " + ex.Message); }
            return list;
        }

        public static bool Save(List<TodoItem> items)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var t in items)
                    sb.Append(t.Id).Append('\t').Append(t.Done ? '1' : '0').Append('\t').Append(Put(t.Due)).Append('\t').Append(t.Star ? '1' : '0').Append('\t')
                      .Append(Put(t.Created)).Append('\t').Append(Put(t.DoneAt)).Append('\t')
                      .Append(t.Title.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')).Append('\t')
                      .Append(Esc(string.IsNullOrEmpty(t.ListName) ? "Tugas Saya" : t.ListName)).Append('\t')
                      .Append(Esc(t.GoogleTaskId)).Append('\t').Append(Esc(t.GoogleListId)).Append('\t')
                      .Append(PutUtc(t.UpdatedUtc)).Append('\t').Append(t.Dirty ? '1' : '0').Append('\t')
                      .Append(Esc(t.Notes)).Append('\t')
                      .Append(Esc(t.ParentId)).Append('\t').Append(t.Position.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(t.Moved ? '1' : '0').Append('\n');
                Directory.CreateDirectory(Settings.Dir);
                AtomicFile.Write(FilePath, new UTF8Encoding(false).GetBytes(sb.ToString()));
                return true;
            }
            catch (Exception ex) { Log.Write("simpan tugas: " + ex.Message); return false; }
        }

        public static List<string[]> LoadDeletedGoogle()
        {
            var list = new List<string[]>();
            try
            {
                if (!File.Exists(DeletedPath)) return list;
                foreach (var line in File.ReadAllLines(DeletedPath, Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    if (f.Length >= 2 && f[1].Trim().Length > 0) list.Add(new[] { f[0].Trim(), f[1].Trim() });
                }
            }
            catch (Exception ex) { Log.Write("tugas hapus: " + ex.Message); }
            return list;
        }

        public static void AddDeletedGoogle(string listId, string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return;
            var list = LoadDeletedGoogle();
            foreach (var item in list) if (item[1] == taskId) return;
            list.Add(new[] { listId ?? "", taskId });
            SaveDeletedGoogle(list);
        }

        public static void SaveDeletedGoogle(List<string[]> items)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var it in items)
                    if (it != null && it.Length >= 2 && !string.IsNullOrEmpty(it[1]))
                        sb.Append(it[0] ?? "").Append('\t').Append(it[1]).Append('\n');
                Directory.CreateDirectory(Settings.Dir);
                AtomicFile.Write(DeletedPath, new UTF8Encoding(false).GetBytes(sb.ToString()));
            }
            catch (Exception ex) { Log.Write("simpan tugas hapus: " + ex.Message); }
        }
    }

    sealed class NoteMeta
    {
        public string Key = "", Color = "", Labels = "", DriveId = "";
        public bool Archived;
        public DateTime UpdatedUtc = DateTime.UtcNow;
    }

    sealed class NoteInfo
    {
        public string Path = "", Title = "", Preview = "";
        public DateTime Time;
        public bool Pin, Quick;
        public string Color = "", Labels = "", DriveId = "";
        public bool Archived;
        public int CheckDone, CheckTotal;
        public override string ToString() { return Title; }
    }

    // One Markdown file per note; the quick-note log (catatan.md) is listed too
    static class NoteFiles
    {
        public static string Dir { get { return System.IO.Path.Combine(Settings.Dir, "catatan"); } }
        public static string QuickPath { get { return System.IO.Path.Combine(Settings.Dir, "catatan.md"); } }
        static string PinPath { get { return System.IO.Path.Combine(Settings.Dir, "catatan-sematan.txt"); } }
        static string MetaPath { get { return System.IO.Path.Combine(Settings.Dir, "catatan-meta.tsv"); } }
        static string DeletedDrivePath { get { return System.IO.Path.Combine(Settings.Dir, "catatan-deleted.txt"); } }
        public const string QuickKey = ":kilat";

        public static string KeyOf(string path) { return path == QuickPath ? QuickKey : System.IO.Path.GetFileName(path); }

        static readonly object noteStateLock = new object();
        const string DeletedKeyPrefix = "key:";
        const string UtcStamp = "yyyy-MM-ddTHH:mm:ss.fffffffZ";
        static DateTime ParseUtc(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParseExact(s, new[] { UtcStamp, "yyyy-MM-ddTHH:mm:ssZ" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d)) return d;
            return DateTime.UtcNow;
        }

        public static Dictionary<string, NoteMeta> LoadMeta()
        {
            lock (noteStateLock)
            {
                var map = new Dictionary<string, NoteMeta>(StringComparer.OrdinalIgnoreCase);
                try
                {
                if (!File.Exists(MetaPath)) return map;
                foreach (var line in File.ReadAllLines(MetaPath, Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    if (f.Length < 1 || f[0].Trim().Length == 0) continue;
                    var m = new NoteMeta { Key = f[0].Trim() };
                    if (f.Length >= 2) m.Color = f[1].Trim();
                    if (f.Length >= 3) m.Labels = f[2].Trim();
                    if (f.Length >= 4) m.Archived = f[3].Trim() == "1";
                    if (f.Length >= 5) m.DriveId = f[4].Trim();
                    if (f.Length >= 6) m.UpdatedUtc = ParseUtc(f[5].Trim());
                    map[m.Key] = m;
                }
            }
                catch (Exception ex) { Log.Write("meta catatan: " + ex.Message); }
                return map;
            }
        }

        public static bool SaveMeta(Dictionary<string, NoteMeta> meta)
        {
            lock (noteStateLock)
            {
                try
                {
                var sb = new StringBuilder();
                foreach (var kv in meta)
                {
                    var m = kv.Value;
                    if (m == null || string.IsNullOrEmpty(m.Key)) continue;
                    sb.Append(m.Key).Append('\t')
                      .Append((m.Color ?? "").Replace('\t', ' ')).Append('\t')
                      .Append((m.Labels ?? "").Replace('\t', ' ')).Append('\t')
                      .Append(m.Archived ? '1' : '0').Append('\t')
                      .Append((m.DriveId ?? "").Replace('\t', ' ')).Append('\t')
                      .Append(m.UpdatedUtc.ToUniversalTime().ToString(UtcStamp, CultureInfo.InvariantCulture)).Append('\n');
                }
                Directory.CreateDirectory(Settings.Dir);
                AtomicFile.Write(MetaPath, new UTF8Encoding(false).GetBytes(sb.ToString()));
                return true;
            }
                catch (Exception ex) { Log.Write("simpan meta catatan: " + ex.Message); return false; }
            }
        }

        public static List<string> LoadDeletedDrive()
        {
            lock (noteStateLock)
            {
                var list = new List<string>();
                try
                {
                    if (!File.Exists(DeletedDrivePath)) return list;
                    foreach (var l in File.ReadAllLines(DeletedDrivePath, Encoding.UTF8))
                        if (l.Trim().Length > 0) list.Add(l.Trim());
                }
                catch (Exception ex) { Log.Write("catatan hapus: " + ex.Message); }
                return list;
            }
        }

        public static void AddDeletedDrive(string driveId)
        {
            if (string.IsNullOrEmpty(driveId)) return;
            lock (noteStateLock)
            {
                var list = LoadDeletedDrive();
                if (list.Contains(driveId)) return;
                list.Add(driveId);
                SaveDeletedDrive(list);
            }
        }

        public static void AddDeletedKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            string entry = DeletedKeyPrefix + key;
            lock (noteStateLock)
            {
                var list = LoadDeletedDrive();
                if (list.Exists(x => string.Equals(x, entry, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(entry);
                SaveDeletedDrive(list);
            }
        }

        public static bool IsDeletedRemote(string driveId, string key)
        {
            lock (noteStateLock)
            {
                var list = LoadDeletedDrive();
                if (!string.IsNullOrEmpty(driveId) && list.Contains(driveId)) return true;
                if (!string.IsNullOrEmpty(key))
                {
                    string entry = DeletedKeyPrefix + key;
                    if (list.Exists(x => string.Equals(x, entry, StringComparison.OrdinalIgnoreCase))) return true;
                }
                return false;
            }
        }

        public static void SaveDeletedDrive(List<string> ids)
        {
            lock (noteStateLock)
            {
                try
                {
                    Directory.CreateDirectory(Settings.Dir);
                    AtomicFile.Write(DeletedDrivePath, new UTF8Encoding(false).GetBytes(string.Join("\n", ids.ToArray())));
                }
                catch (Exception ex) { Log.Write("simpan catatan hapus: " + ex.Message); }
            }
        }

        public static HashSet<string> LoadPins()
        {
            lock (noteStateLock)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { if (File.Exists(PinPath)) foreach (var l in File.ReadAllLines(PinPath, Encoding.UTF8)) if (l.Trim().Length > 0) set.Add(l.Trim()); }
                catch (Exception ex) { Log.Write("sematan catatan: " + ex.Message); }
                return set;
            }
        }

        public static void SavePins(HashSet<string> pins)
        {
            lock (noteStateLock)
            {
                try { Directory.CreateDirectory(Settings.Dir); AtomicFile.Write(PinPath, new UTF8Encoding(false).GetBytes(string.Join("\n", new List<string>(pins).ToArray()))); }
                catch (Exception ex) { Log.Write("simpan sematan: " + ex.Message); }
            }
        }

        public static bool IsPinned(HashSet<string> pins, string path) { return pins.Contains(KeyOf(path)); }

        public static void SetPin(HashSet<string> pins, string path, bool on)
        {
            if (on) pins.Add(KeyOf(path)); else pins.Remove(KeyOf(path));
            SavePins(pins);
        }

        public static string NewPath()
        {
            Directory.CreateDirectory(Dir);
            string stem = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), p = System.IO.Path.Combine(Dir, stem + ".md");
            for (int i = 2; File.Exists(p); i++) p = System.IO.Path.Combine(Dir, stem + "-" + i + ".md");
            return p;
        }

        public static bool TryRead(string path, out string text)
        {
            text = null;
            try { text = File.ReadAllText(path, Encoding.UTF8); return true; }
            catch (Exception ex) { Log.Write("baca catatan: " + ex.Message); return false; }
        }
        public static string Read(string path)
        {
            string text;
            return TryRead(path, out text) ? text : "";
        }

        public static bool Write(string path, string text)
        {
            lock (noteStateLock)
            {
                try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); AtomicFile.Write(path, new UTF8Encoding(false).GetBytes(text)); return true; }
                catch (Exception ex) { Log.Write("simpan catatan: " + ex.Message); return false; }
            }
        }
        internal static bool WriteFromDrive(string path, string text, string driveId, string key,
            bool existed, DateTime localTime, DateTime remoteTime, out bool skipped)
        {
            lock (noteStateLock)
            {
                skipped = IsDeletedRemote(driveId, key) || File.Exists(path) != existed
                    || (existed && File.GetLastWriteTimeUtc(path) != localTime);
                if (skipped || !Write(path, text)) return false;
                if (remoteTime != DateTime.MinValue)
                {
                    try { File.SetLastWriteTimeUtc(path, remoteTime); }
                    catch (Exception ex) { Log.Write("waktu catatan: " + ex.Message); }
                }
                return true;
            }
        }

        internal static void AppendQuick(string line)
        {
            lock (noteStateLock)
            {
                string text = File.Exists(QuickPath) ? File.ReadAllText(QuickPath, Encoding.UTF8) : "";
                if (text.Length != 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += Environment.NewLine;
                if (!Write(QuickPath, text + line)) throw new IOException("Unable to append quick note");
            }
        }

        public static bool Remove(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                string staged = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deskmate-del-" + Guid.NewGuid().ToString("N") + ".md");
                try
                {
                    File.Move(path, staged);
                }
                catch (Exception)
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                    return !File.Exists(path);
                }
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { FileSystem.DeleteFile(staged, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException); }
                    catch (Exception)
                    {
                        try { if (File.Exists(staged)) File.Delete(staged); }
                        catch (Exception) { }
                    }
                });
                return !File.Exists(path);
            }
            catch (Exception ex) { Log.Write("hapus catatan: " + ex.Message); return false; }
        }

        static string Head(string path, int max)
        {
            using (var r = new StreamReader(path, Encoding.UTF8))
            {
                var buf = new char[max];
                int n = r.Read(buf, 0, max);
                return new string(buf, 0, Math.Max(0, n));
            }
        }

        // Split a Markdown note into title (line 1, optional # prefix) + body
        public static void SplitNote(string text, out string title, out string body)
        {
            title = "";
            body = "";
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return;
            int nl = text.IndexOf('\n');
            string line1 = (nl < 0 ? text : text.Substring(0, nl)).TrimEnd('\r');
            if (line1.Trim().Length == 0)
            {
                body = text;
                return;
            }
            title = line1.TrimStart().StartsWith("#", StringComparison.Ordinal)
                ? line1.TrimStart(' ', '\t', '#').Trim()
                : line1.Trim();
            string rest = nl < 0 ? "" : text.Substring(nl + 1);
            if (rest.Length > 0)
            {
                int nl2 = rest.IndexOf('\n');
                string line2 = (nl2 < 0 ? rest : rest.Substring(0, nl2)).TrimEnd('\r');
                if (line2.Trim().Length == 0)
                    rest = nl2 < 0 ? "" : rest.Substring(nl2 + 1);
            }
            body = rest;
        }

        public static string JoinNote(string title, string body)
        {
            string t = (title ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            string b = body ?? "";
            if (t.Length > 0)
                return "# " + t + (b.Length > 0 ? "\r\n\r\n" + b : "\r\n");
            return b;
        }

        public static string NormalizeNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        // Title: first non-empty line without heading marks; preview: the next line
        static void Describe(string text, NoteInfo n)
        {
            string title = "", preview = "";
            int done = 0, total = 0;
            foreach (var raw in text.Split('\n'))
            {
                string tr = raw.Trim();
                string l;
                if (tr.StartsWith("- [ ] ", StringComparison.Ordinal)) { total++; l = tr.Substring(6).Trim(); }
                else if (tr.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase)) { total++; done++; l = tr.Substring(6).Trim(); }
                else l = tr.TrimStart('#', ' ', '-', '*').Trim();
                if (l.Length == 0) continue;
                if (title.Length == 0) title = l; else if (preview.Length == 0) preview = l;
            }
            n.Title = title.Length > 0 ? title : Lang.T("Catatan kosong");
            n.Preview = preview;
            n.CheckDone = done;
            n.CheckTotal = total;
        }

        public static List<NoteInfo> List(string query, HashSet<string> pins)
        {
            return List(query, pins, LoadMeta(), false, "");
        }

        public static List<NoteInfo> List(string query, HashSet<string> pins, Dictionary<string, NoteMeta> meta, bool showArchived, string colorFilter)
        {
            var list = new List<NoteInfo>();
            query = (query ?? "").Trim();
            colorFilter = (colorFilter ?? "").Trim();
            try
            {
                var paths = new List<string>();
                if (Directory.Exists(Dir)) paths.AddRange(Directory.GetFiles(Dir, "*.md"));
                if (File.Exists(QuickPath)) paths.Add(QuickPath);
                foreach (var p in paths)
                {
                    string key = KeyOf(p);
                    NoteMeta m = null;
                    if (meta != null) meta.TryGetValue(key, out m);
                    bool archived = m != null && m.Archived;
                    if (archived != showArchived) continue;
                    string color = m != null ? (m.Color ?? "") : "";
                    if (colorFilter.Length > 0 && !string.Equals(color, colorFilter, StringComparison.OrdinalIgnoreCase)) continue;

                    var n = new NoteInfo
                    {
                        Path = p,
                        Quick = p == QuickPath,
                        Time = File.GetLastWriteTime(p),
                        Color = color,
                        Labels = m != null ? (m.Labels ?? "") : "",
                        DriveId = m != null ? (m.DriveId ?? "") : "",
                        Archived = archived
                    };
                    n.Pin = pins.Contains(key);
                    Describe(Head(p, 1200), n);
                    if (n.Quick)
                    {
                        string qPrev = n.Title == Lang.T("Catatan kosong") ? "" : n.Title;
                        n.Title = Lang.T("Catatan kilat");
                        n.Preview = qPrev;
                    }
                    if (query.Length > 0)
                    {
                        string tagQuery = query.StartsWith("#", StringComparison.Ordinal) ? query.Substring(1) : query;
                        bool hit = n.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   n.Preview.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   (tagQuery.Length > 0 && n.Labels.IndexOf(tagQuery, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                   NotePalette.FormatTags(n.Labels).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   Read(p).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!hit) continue;
                    }
                    list.Add(n);
                }
            }
            catch (Exception ex) { Log.Write("daftar catatan: " + ex.Message); }
            list.Sort((a, b) => a.Pin != b.Pin ? (a.Pin ? -1 : 1) : b.Time.CompareTo(a.Time));
            return list;
        }
    }

    // Tasks and notes in one window
    partial class Island
    {
        const int WorkHotkey = 40;
        const string TodoTag = "Tugas: ";
        WorkWindow workWin;
        internal List<TodoItem> todos;
        internal Action WorkSyncHook;
        internal Func<bool> IsGoogleConnected;
        internal Action<Action<string>> SyncGoogleWorkAction;
        // Task lists, empty ones included; todos name their list by ListName
        internal List<TaskListInfo> taskLists;

        // Tasks and lists load once, whoever needs them first
        internal void EnsureWorkData()
        {
            if (todos == null) todos = TodoFile.Load();
            if (taskLists == null)
            {
                taskLists = TaskLists.Load();
                if (TaskLists.Ensure(taskLists, todos)) TaskLists.Save(taskLists);
            }
        }

        // After a list was created, renamed or deleted: save and let the sync know
        internal bool ListsChanged()
        {
            EnsureWorkData();
            TaskLists.Ensure(taskLists, todos);
            bool ok = TaskLists.Save(taskLists);
            if (WorkSyncHook != null) WorkSyncHook();
            return ok;
        }

        internal void ReloadWorkFromSync()
        {
            Post(delegate
            {
                if (workWin != null && !workWin.IsDisposed) workWin.ReloadFromSync();
            });
        }

        internal IntPtr WorkHwnd { get { return workWin != null && !workWin.IsDisposed && workWin.IsHandleCreated ? workWin.Handle : IntPtr.Zero; } }
        internal bool WorkOpen { get { return workWin != null && !workWin.IsDisposed && workWin.Visible; } }
        internal void SyncWorkDock() { foreach (var dock in dockWins.Values) if (!dock.IsDisposed && dock.IsHandleCreated) dock.SyncWork(); }

        void OpenWork() { OpenWork(workWin != null && !workWin.IsDisposed ? workWin.CurrentTab : 0); }

        void OpenWork(int tab)
        {
            EnsureWorkData();
            if (workWin == null || workWin.IsDisposed)
            {
                workWin = new WorkWindow(this);
                workWin.FormClosed += delegate { workWin = null; SyncWorkDock(); };
                workWin.Show();
            }
            else
            {
                // Pinned behaves like a real window: the dock button or hotkey brings it
                // forward, the X hides it, and nothing is rebuilt on reopening
                if (workWin.WindowState == FormWindowState.Minimized) workWin.WindowState = FormWindowState.Normal;
                if (!workWin.Visible) workWin.Show();
            }
            workWin.SelectTab(tab);
            ShellApp.Activate(workWin.Handle);
            workWin.FocusInput();
            SyncWorkDock();
        }

        // After any edit: save, then keep the due reminders in step
        internal bool TodosChanged() { return TodosChanged(false); }
        internal bool TodosChanged(bool fromSync)
        {
            var previous = TodoFile.Load();
            if (!TodoFile.Save(todos)) return false;
            for (int i = 0; i < todos.Count; i++)
            {
                var cur = todos[i];
                if (!cur.Done) continue;
                bool wasDone = false;
                for (int j = 0; j < previous.Count; j++)
                {
                    if ((!string.IsNullOrEmpty(cur.Id) ? previous[j].Id == cur.Id : (previous[j].Title == cur.Title && previous[j].Due == cur.Due)) && previous[j].Done)
                    {
                        wasDone = true;
                        break;
                    }
                }
                if (!wasDone) RecordTaskDoneXp(35);
            }
            // Only replace reminders matching a previously persisted task and due time.
            // A broad prefix match would remove unrelated user reminders.
            foreach (var old in previous)
                if (!old.Done && old.Due.HasValue)
                {
                    var reminder = oneShots.Find(o => o.At == old.Due.Value && o.Text == TodoTag + old.Title);
                    if (reminder != null) oneShots.Remove(reminder);
                }
            var now = DateTime.Now;
            foreach (var t in todos)
                if (!t.Done && t.Due.HasValue && t.Due.Value > now) oneShots.Add(new OneShot { At = t.Due.Value, Text = TodoTag + t.Title });
            oneShots.Sort((a, b) => a.At.CompareTo(b.At));
            SaveOneShots();
            Kick();
            if (!fromSync && WorkSyncHook != null) WorkSyncHook();
            return true;
        }
    }
}
