using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Merges every source into UI lists
    partial class Island
    {
        enum DataState { Off, Loading, Failed, Empty, Ready }

        SourceHub sources;
        List<KuliaTask> kuliaTasks;
        List<ClassSlot> kuliaClasses;
        bool kuliaLoading;
        // 1: tasks refresh asked, 2: schedule
        int refreshAsked;
        string taskNote, eventNote;

        // Kulia or any source syncing
        bool tasksLoading { get { return kuliaLoading || (sources != null && sources.Busy); } }

        // Settings listens for status lines
        event Action SourcesChanged;

        bool KuliaOn { get { return kulia != null && kulia.SignedIn; } }
        bool OnlyKuliaTasks { get { return sources == null || !sources.HasTasks; } }
        bool OnlyKuliaEvents { get { return sources == null || !sources.HasEvents; } }

        void StartSources()
        {
            sources = new SourceHub(Post);
            sources.Changed += OnSourcesChanged;
            try { sources.Load(); }
            catch (Exception ex) { Log.Write("sources load: " + ex.GetType().Name); }
            sources.Holidays.Enabled = settings.GetBool("calendar.holidays", true);
            sources.GoogleTasks.Enabled = settings.GetBool("google.tasks", true);
            sources.RefreshHolidays();
            sources.Tick(DateTime.UtcNow);
            if (GoogleCalendarConnected) SyncGoogleWorkAsync(null);
            WorkSyncHook = ScheduleGoogleWorkSync;
            IsGoogleConnected = delegate { return GoogleCalendarConnected; };
            SyncGoogleWorkAction = SyncGoogleWorkAsync;
            try { System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailability; }
            catch (Exception ex) { Log.Write("network watch: " + ex.Message); }
            if (googleWorkPeriodicTimer == null)
            {
                googleWorkPeriodicTimer = new System.Threading.Timer(delegate
                {
                    Post(delegate
                    {
                        if (!IsDisposed && GoogleCalendarConnected && !WorkSyncBusy)
                            SyncGoogleWorkAsync(null);
                    });
                }, null, 180000, 180000);
            }
        }

        public void ReloadHolidaySource()
        {
            if (sources == null) return;
            sources.Holidays.Enabled = settings.GetBool("calendar.holidays", true);
            sources.RefreshHolidays();
        }

        public bool GoogleCalendarConnected
        {
            get { return sources != null && sources.GoogleConnected; }
        }

        public string GoogleCalendarEmail
        {
            get { return sources != null ? sources.GoogleEmail : ""; }
        }

        public void RefreshGoogleCalendar()
        {
            if (sources != null) sources.RefreshGoogle();
            SyncGoogleWorkAsync(null);
        }

        public void DisconnectGoogleCalendar()
        {
            ResetDriveNotesSession();
            if (sources != null) sources.DisconnectGoogle();
            else GoogleCalVault.Forget();
        }

        public void SyncGoogleAllNow(Action<string> done)
        {
            if (sources != null) sources.RefreshGoogle();
            SyncGoogleWorkAsync(done);
        }

        System.Threading.Timer googleWorkSyncTimer;
        System.Threading.Timer googleWorkPeriodicTimer;
        readonly object googleWorkSyncGate = new object();
        bool googleWorkSyncing;
        bool driveNotesLoaded;
        Dictionary<string, DateTime> driveNoteVersions = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        long noteEditVersion = 1, noteUploadedVersion;
        int driveNotesSession;
        internal bool DriveNotesLoading { get; private set; }

        internal void ScheduleDriveNotesUpload()
        {
            noteEditVersion++;
            ScheduleGoogleWorkSync();
        }

        void ResetDriveNotesSession()
        {
            driveNotesSession++;
            driveNotesLoaded = false;
            driveNoteVersions = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }
        readonly List<Action<string>> googleWorkWaiters = new List<Action<string>>();

        public void ReloadGoogleWorkspaceSources()
        {
            if (sources != null)
                sources.GoogleTasks.Enabled = settings.GetBool("google.tasks", true);
            RefreshGoogleCalendar();
        }

        public void ScheduleGoogleWorkSync()
        {
            if (!GoogleCalendarConnected) return;
            if (googleWorkSyncTimer == null)
            {
                googleWorkSyncTimer = new System.Threading.Timer(delegate
                {
                    SyncGoogleWorkAsync(null);
                }, null, 2500, System.Threading.Timeout.Infinite);
            }
            else
            {
                googleWorkSyncTimer.Change(2500, System.Threading.Timeout.Infinite);
            }
        }

        static TodoItem CloneTodo(TodoItem s)
        {
            return new TodoItem { Id = s.Id, Title = s.Title, Done = s.Done, Star = s.Star, Due = s.Due, Created = s.Created, DoneAt = s.DoneAt, ListName = s.ListName, Notes = s.Notes, GoogleTaskId = s.GoogleTaskId, GoogleListId = s.GoogleListId, UpdatedUtc = s.UpdatedUtc, Dirty = s.Dirty, ParentId = s.ParentId, Position = s.Position, Moved = s.Moved };
        }

        // What the Work window shows about the sync; written on the UI thread
        internal bool WorkSyncBusy;
        internal string WorkSyncError;
        internal DateTime WorkSyncOkUtc = DateTime.MinValue, WorkSyncTriedUtc = DateTime.MinValue;
        internal event Action WorkSyncStatusChanged;
        internal DateTime LastSyncUtc { get { return WorkSyncOkUtc; } }
        internal string LastSyncError { get { return WorkSyncError; } }

        void PublishWorkSync()
        {
            var h = WorkSyncStatusChanged;
            if (h != null) h();
        }

        bool AnyGoogleWorkEnabled()
        {
            return settings.GetBool("google.tasks", true) || settings.GetBool("google.drive.notes", true);
        }

        // The window opening or coming forward: sync unless one just ran
        internal void SyncGoogleWorkIfStale(double seconds)
        {
            if (!GoogleCalendarConnected || WorkSyncBusy || !AnyGoogleWorkEnabled()) return;
            if ((DateTime.UtcNow - WorkSyncTriedUtc).TotalSeconds < seconds) return;
            SyncGoogleWorkAsync(null);
        }

        // Called every second by the clock; syncs every few minutes while connected
        void GoogleWorkTick(DateTime nowUtc)
        {
            if (WorkSyncBusy || !GoogleCalendarConnected || !AnyGoogleWorkEnabled()) return;
            if ((nowUtc - WorkSyncTriedUtc).TotalMinutes >= 3) SyncGoogleWorkAsync(null);
        }

        void OnNetworkAvailability(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable) Post(delegate { ScheduleGoogleWorkSync(); });
        }

        void MergeSyncedLists(List<TaskListInfo> synced, Dictionary<string, TaskListInfo> orig)
        {
            EnsureWorkData();
            var kept = new HashSet<string>();
            foreach (var c in synced)
            {
                kept.Add(c.Id);
                TaskListInfo o;
                orig.TryGetValue(c.Id, out o);
                var l = taskLists.Find(x => x.Id == c.Id);
                if (l == null)
                {
                    // Deleted here while the sync ran: whatever it made on Google goes too
                    if (o != null) { TaskLists.AddDeleted(c.GoogleId); continue; }
                    var same = TaskLists.Find(taskLists, c.Name);
                    if (same == null) taskLists.Add(c);
                    else if (string.IsNullOrEmpty(same.GoogleId)) same.GoogleId = c.GoogleId;
                    continue;
                }
                if (o != null && l.Name == o.Name && l.Dirty == o.Dirty)
                {
                    // Untouched meanwhile; a rename made on Google reaches the tasks as well
                    if (l.Name != c.Name)
                        foreach (var t in todos)
                            if (TaskLists.Same(TaskLists.NameOf(t), l.Name)) t.ListName = c.Name;
                    l.Name = c.Name; l.Dirty = c.Dirty; l.GoogleId = c.GoogleId;
                }
                else if (string.IsNullOrEmpty(l.GoogleId)) l.GoogleId = c.GoogleId;
            }
            // Dropped by the sync: deleted on Google
            foreach (var o in orig.Values)
            {
                if (kept.Contains(o.Id)) continue;
                var l = taskLists.Find(x => x.Id == o.Id);
                if (l == null) continue;
                if (l.Name == o.Name && l.Dirty == o.Dirty && !TaskLists.IsDefault(l.Name)) taskLists.Remove(l);
                else l.GoogleId = "";
            }
            TaskLists.Ensure(taskLists, todos ?? new List<TodoItem>());
            TaskLists.Save(taskLists);
        }

        void MergeSyncedTodos(List<TodoItem> synced, Dictionary<string, DateTime> origUpdated)
        {
            if (todos == null) todos = new List<TodoItem>();
            var live = new Dictionary<string, TodoItem>();
            foreach (var l in todos) live[l.Id] = l;
            var kept = new HashSet<string>();
            foreach (var c in synced)
            {
                kept.Add(c.Id);
                TodoItem l;
                if (!live.TryGetValue(c.Id, out l))
                {
                    if (origUpdated.ContainsKey(c.Id))
                    {
                        // Deleted here while the sync ran: what the sync made on Google must go too
                        if (!string.IsNullOrEmpty(c.GoogleTaskId)) TodoFile.AddDeletedGoogle(c.GoogleListId, c.GoogleTaskId);
                        continue;
                    }
                    todos.Add(c);
                    continue;
                }
                DateTime orig;
                if (origUpdated.TryGetValue(c.Id, out orig) && l.UpdatedUtc <= orig)
                {
                    l.Title = c.Title; l.Done = c.Done; l.Star = c.Star; l.Due = c.Due; l.Created = c.Created; l.DoneAt = c.DoneAt;
                    l.ListName = c.ListName; l.Notes = c.Notes; l.GoogleTaskId = c.GoogleTaskId; l.GoogleListId = c.GoogleListId;
                    l.ParentId = c.ParentId; l.Position = c.Position; l.Moved = c.Moved;
                    l.UpdatedUtc = c.UpdatedUtc; l.Dirty = c.Dirty;
                }
                else
                {
                    if (string.IsNullOrEmpty(l.GoogleTaskId)) l.GoogleTaskId = c.GoogleTaskId;
                    if (string.IsNullOrEmpty(l.GoogleListId)) l.GoogleListId = c.GoogleListId;
                }
            }
            // Gone from Google: removed, unless edited here meanwhile; then it is sent again
            foreach (var kv in origUpdated)
            {
                if (kept.Contains(kv.Key)) continue;
                TodoItem l;
                if (!live.TryGetValue(kv.Key, out l)) continue;
                if (l.UpdatedUtc <= kv.Value) todos.Remove(l);
                else { l.GoogleTaskId = ""; l.GoogleListId = ""; l.Dirty = true; }
            }
            TodosChanged(true);
        }

        public void SyncGoogleWorkAsync(Action<string> done)
        {
            if (InvokeRequired)
            {
                Post(delegate { SyncGoogleWorkAsync(done); });
                return;
            }
            bool doTasks = settings.GetBool("google.tasks", true);
            bool driveEnabled = settings.GetBool("google.drive.notes", true);
            bool doDrive = driveEnabled && (!driveNotesLoaded || noteEditVersion != noteUploadedVersion);
            if (!GoogleCalendarConnected || (!doTasks && !doDrive))
            {
                if (!doTasks && !doDrive) WorkSyncTriedUtc = DateTime.UtcNow;
                if (done != null) Post(delegate { done(null); });
                return;
            }
            lock (googleWorkSyncGate)
            {
                if (googleWorkSyncing)
                {
                    if (done != null) googleWorkWaiters.Add(done);
                    return;
                }
                googleWorkSyncing = true;
            }
            bool loadDrive = doDrive && !driveNotesLoaded;
            bool draftBlockedLoad = loadDrive && workWin != null && !workWin.IsDisposed && !workWin.FlushNoteDraft();
            if (draftBlockedLoad) loadDrive = doDrive = false;
            var driveVersions = driveNoteVersions;
            int driveSession = driveNotesSession;
            long uploadVersion = noteEditVersion;
            DriveNotesLoading = loadDrive;
            if (loadDrive && workWin != null && !workWin.IsDisposed) workWin.UpdateNoteAvailability();
            if (doTasks) EnsureWorkData();
            var snapshot = new List<TodoItem>();
            var origUpdated = new Dictionary<string, DateTime>();
            var listSnapshot = new List<TaskListInfo>();
            var origLists = new Dictionary<string, TaskListInfo>();
            if (doTasks && todos != null)
            {
                foreach (var s in todos)
                {
                    snapshot.Add(CloneTodo(s));
                    origUpdated[s.Id] = s.UpdatedUtc;
                }
                listSnapshot = TaskLists.CloneAll(taskLists);
                foreach (var l in taskLists) origLists[l.Id] = l.Clone();
            }
            WorkSyncBusy = true;
            WorkSyncTriedUtc = DateTime.UtcNow;
            PublishWorkSync();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = draftBlockedLoad ? Lang.T("Gagal menyimpan") : null;
                bool tasksChanged = false, notesChanged = false, notesTransferred = false;
                Dictionary<string, NoteMeta> meta = null;
                HashSet<string> pins = null;
                var completedDeletes = new List<string>();
                try
                {
                    if (doTasks)
                    {
                        var localTodos = snapshot;
                        var pendingTaskDeletes = TodoFile.LoadDeletedGoogle();
                        int prevDelCount = pendingTaskDeletes.Count;
                        var pendingListDeletes = TaskLists.LoadDeleted();
                        int prevListDelCount = pendingListDeletes.Count;
                        bool tChanged;
                        string tErr;
                        GoogleTasksClient.Sync(localTodos, pendingTaskDeletes, listSnapshot, pendingListDeletes, out tChanged, out tErr);
                        if (tChanged || pendingTaskDeletes.Count != prevDelCount)
                        {
                            TodoFile.SaveDeletedGoogle(pendingTaskDeletes);
                        }
                        if (pendingListDeletes.Count != prevListDelCount) TaskLists.SaveDeleted(pendingListDeletes);
                        if (tChanged)
                        {
                            tasksChanged = true;
                        }
                        if (tErr != null) err = tErr;
                    }
                    if (doDrive)
                    {
                        meta = NoteFiles.LoadMeta();
                        pins = NoteFiles.LoadPins();
                        string nErr;
                        notesTransferred = GoogleDriveNotesClient.Transfer(NoteFiles.Dir, NoteFiles.QuickPath, meta, pins,
                            NoteFiles.LoadDeletedDrive(), completedDeletes, loadDrive, driveVersions, out notesChanged, out nErr) && nErr == null;
                        if (nErr != null && err == null) err = nErr;
                    }
                }
                catch (Exception ex)
                {
                    err = Lang.T("Gagal memuat");
                    Log.Write("google work sync: " + ex.Message);
                }
                Post(delegate
                {
                    Action<string>[] waiters;
                    lock (googleWorkSyncGate)
                    {
                        googleWorkSyncing = false;
                        waiters = googleWorkWaiters.ToArray();
                        googleWorkWaiters.Clear();
                    }
                    DriveNotesLoading = false;
                    bool currentDriveSession = driveSession == driveNotesSession;
                    if (currentDriveSession && doDrive)
                    {
                        if (completedDeletes.Count > 0)
                        {
                            var pending = NoteFiles.LoadDeletedDrive();
                            pending.RemoveAll(d => completedDeletes.Exists(c => string.Equals(c, d, StringComparison.OrdinalIgnoreCase)));
                            NoteFiles.SaveDeletedDrive(pending);
                        }
                        if (notesChanged && loadDrive)
                        {
                            NoteFiles.SavePins(pins);
                            if (!NoteFiles.SaveMeta(meta))
                            {
                                notesTransferred = false;
                                if (err == null) err = Lang.T("Gagal menyimpan");
                            }
                        }
                        if (notesChanged && !loadDrive)
                        {
                            var fresh = NoteFiles.LoadMeta();
                            bool idsChanged = false;
                            foreach (var entry in meta)
                            {
                                string id = entry.Value.DriveId;
                                if (string.IsNullOrEmpty(id) || NoteFiles.IsDeletedRemote(id, entry.Key)) continue;
                                NoteMeta local;
                                if (!fresh.TryGetValue(entry.Key, out local))
                                    fresh[entry.Key] = local = new NoteMeta { Key = entry.Key };
                                if (local.DriveId == id) continue;
                                local.DriveId = id;
                                idsChanged = true;
                            }
                            if (idsChanged && NoteFiles.SaveMeta(fresh) && workWin != null && !workWin.IsDisposed)
                                workWin.RefreshNoteDriveIds(fresh);
                        }
                        if (notesTransferred)
                        {
                            if (loadDrive)
                            {
                                driveNotesLoaded = true;
                                ScheduleDriveNotesUpload();
                            }
                            else
                            {
                                noteUploadedVersion = uploadVersion;
                                if (noteEditVersion != uploadVersion) ScheduleGoogleWorkSync();
                            }
                        }
                    }
                    if (workWin != null && !workWin.IsDisposed)
                    {
                        if (loadDrive && notesChanged) workWin.ReloadNotesFromDisk();
                        workWin.UpdateNoteAvailability();
                    }
                    if (tasksChanged)
                    {
                        MergeSyncedLists(listSnapshot, origLists);
                        MergeSyncedTodos(snapshot, origUpdated);
                    }
                    if (tasksChanged && sources != null) sources.RefreshGoogle();
                    if (tasksChanged && workWin != null && !workWin.IsDisposed) workWin.ReloadTasksFromSync();
                    if (!currentDriveSession) ScheduleGoogleWorkSync();
                    WorkSyncBusy = false;
                    WorkSyncError = err;
                    if (err == null) WorkSyncOkUtc = DateTime.UtcNow;
                    PublishWorkSync();
                    if (done != null) done(err);
                    foreach (var w in waiters) w(err);
                });
            });
        }

        public bool CreateGoogleCalendarEvent(string title, DateTime startLocal, DateTime endLocal, string location, bool allDay, out string error)
        {
            if (sources == null)
            {
                error = Lang.T("Belum terhubung");
                return false;
            }
            ClassSlot slot = sources.Google.CreateEventSlot(title, startLocal, endLocal, location, allDay, out error);
            if (slot == null) return false;
            Action ui = delegate
            {
                if (IsDisposed || sources == null) return;
                if (sources.Google.Events == null) sources.Google.Events = new List<ClassSlot>();
                sources.Google.Events.Add(slot);
                sources.Google.Events.Sort(SlotOrder);
                if (classes == null) classes = new List<ClassSlot>();
                classes.Add(slot);
                classes.Sort(SlotOrder);
                Kick();
                ShowInfo(Ink.IcoCalendar, Lang.T("Acara ditambahkan"), slot.Title, true);
                RefreshGoogleCalendar();
            };
            if (InvokeRequired) Post(ui);
            else ui();
            return true;
        }

        public bool UpdateGoogleCalendarEvent(string eventId, string title, DateTime startLocal, DateTime endLocal, string location, bool allDay, out string error)
        {
            if (sources == null)
            {
                error = Lang.T("Belum terhubung");
                return false;
            }
            ClassSlot slot = sources.Google.UpdateEventSlot(eventId, title, startLocal, endLocal, location, allDay, out error);
            if (slot == null) return false;
            Action ui = delegate
            {
                if (IsDisposed || sources == null) return;
                if (sources.Google.Events == null) sources.Google.Events = new List<ClassSlot>();
                sources.Google.Events.RemoveAll(delegate(ClassSlot c) { return c != null && c.Id == eventId; });
                sources.Google.Events.Add(slot);
                sources.Google.Events.Sort(SlotOrder);
                if (classes == null) classes = new List<ClassSlot>();
                classes.RemoveAll(delegate(ClassSlot c) { return c != null && c.Source == "google" && c.Id == eventId; });
                classes.Add(slot);
                classes.Sort(SlotOrder);
                Kick();
                ShowInfo(Ink.IcoCalendar, Lang.T("Acara diperbarui"), slot.Title, true);
                RefreshGoogleCalendar();
            };
            if (InvokeRequired) Post(ui);
            else ui();
            return true;
        }

        public bool DeleteGoogleCalendarEvent(string eventId, out string error)
        {
            if (sources == null)
            {
                error = Lang.T("Belum terhubung");
                return false;
            }
            if (!sources.Google.DeleteEvent(eventId, out error)) return false;
            Action ui = delegate
            {
                if (IsDisposed || sources == null) return;
                if (sources.Google.Events != null)
                    sources.Google.Events.RemoveAll(delegate(ClassSlot c) { return c != null && c.Id == eventId; });
                if (classes != null)
                    classes.RemoveAll(delegate(ClassSlot c) { return c != null && c.Source == "google" && c.Id == eventId; });
                Kick();
                ShowInfo(Ink.IcoCalendar, Lang.T("Acara dihapus"), "", true);
                RefreshGoogleCalendar();
            };
            if (InvokeRequired) Post(ui);
            else ui();
            return true;
        }

        public void ConnectGoogleCalendar(string clientId, string clientSecret, Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string email, refreshToken, accessToken;
                DateTime expiresUtc;
                string err;
                try
                {
                    err = GoogleCalAuth.StartLoopbackLogin(clientId, clientSecret, out email, out refreshToken, out accessToken, out expiresUtc);
                }
                catch (Exception ex)
                {
                    err = Lang.T("Gagal terhubung");
                    Log.Write("googlecal connect: " + ex.GetType().Name);
                }
                Post(delegate
                {
                    if (err == null && sources != null)
                    {
                        ResetDriveNotesSession();
                        sources.RefreshGoogle();
                        SyncGoogleWorkAsync(null);
                    }
                    if (done != null) done(err);
                });
            });
        }

        void OnSourcesChanged(bool data)
        {
            if (data) MergeSources();
            else if (refreshAsked != 0 && !tasksLoading) MergeSources();
            Kick();
            var h = SourcesChanged;
            if (h != null) h();
        }

        void MergeSources()
        {
            var all = kuliaTasks != null ? new List<KuliaTask>(kuliaTasks) : null;
            var slots = kuliaClasses != null ? new List<ClassSlot>(kuliaClasses) : null;
            var labels = new Dictionary<string, string> { { "kulia", "Kulia" } };
            int taskFrom = kuliaTasks != null && kuliaTasks.Count > 0 ? 1 : 0, eventFrom = kuliaClasses != null && kuliaClasses.Count > 0 ? 1 : 0;
            taskNote = KuliaOn && tasksError != null ? tasksError : null;
            eventNote = KuliaOn && kuliaClasses == null ? classesError ?? tasksError : null;
            if (sources != null)
            {
                bool noWrite = sources.Notion != null && sources.Notion.NoWrite;
                int count = sources.All().Count + (KuliaOn ? 1 : 0);
                foreach (var s in sources.All())
                {
                    labels[s.Key] = s.Name;
                    if (s.Tasks != null)
                    {
                        if (all == null) all = new List<KuliaTask>();
                        foreach (var t in s.Tasks)
                        {
                            if (noWrite && t.Source == "notion") t.ReadOnly = true;
                            all.Add(t);
                        }
                        if (s.Tasks.Count > 0) taskFrom++;
                    }
                    if (s.Events != null)
                    {
                        if (slots == null) slots = new List<ClassSlot>();
                        slots.AddRange(s.Events);
                        if (s.Events.Count > 0) eventFrom++;
                    }
                    if (s.Error == null) continue;
                    string note = count > 1 ? s.Name + ": " + s.Error : s.Error;
                    if (s.GivesTasks && taskNote == null) taskNote = note;
                    if (s.GivesEvents && eventNote == null) eventNote = note;
                }
            }
            if (all != null)
            {
                // Hit regions key on ids: keep one
                var ids = new HashSet<string>();
                all.RemoveAll(t => t.Closed || !ids.Add(t.Id ?? ""));
                all.Sort((a, b) => a.DeadlineUtc.CompareTo(b.DeadlineUtc));
                if (all.Count > 300) all.RemoveRange(300, all.Count - 300);
            }
            if (slots != null)
            {
                var now = DateTime.Now;
                slots.RemoveAll(c => c.End < now);
                var nonHoliday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < slots.Count; i++)
                {
                    var c = slots[i];
                    if (c.Source != "holiday")
                        nonHoliday.Add(c.Start.Date.ToString("yyyy-MM-dd") + "|" + (c.Title ?? ""));
                }
                slots.RemoveAll(c => c.Source == "holiday" && nonHoliday.Contains(c.Start.Date.ToString("yyyy-MM-dd") + "|" + (c.Title ?? "")));
                slots.Sort(SlotOrder);
                if (slots.Count > 1500) slots.RemoveRange(1500, slots.Count - 1500);
            }
            tasks = all;
            classes = slots;
            SourceHub.SetLabels(labels, taskFrom > 1, eventFrom > 1);
            if (refreshAsked != 0 && !tasksLoading)
            {
                if (refreshAsked == 1 && tasks != null) ShowInfo(Ink.IcoRefresh, Lang.T("Tugas diperbarui"), tasks.Count + Lang.T(" aktif"));
                else if (refreshAsked == 2 && classes != null) ShowInfo(Ink.IcoRefresh, Lang.T("Jadwal diperbarui"), classes.Count == 1 ? Lang.T("1 acara") : Lang.F("{0} acara", classes.Count));
                refreshAsked = 0;
            }
        }

        // Day first, all-day on top, then time
        static int SlotOrder(ClassSlot a, ClassSlot b)
        {
            int d = a.Start.Date.CompareTo(b.Start.Date);
            if (d != 0) return d;
            if (a.AllDay != b.AllDay) return a.AllDay ? -1 : 1;
            return a.Start.CompareTo(b.Start);
        }

        // Room for a partial-failure line
        float TaskFoot() { return taskNote != null && tasks != null && tasks.Count > 0 ? 22 : 0; }
        float EventFoot() { return eventNote != null && classes != null && classes.Count > 0 ? 20 : 0; }

        DataState TaskState()
        {
            if (!KuliaOn && OnlyKuliaTasks) return DataState.Off;
            if (tasks != null) return tasks.Count > 0 ? DataState.Ready : DataState.Empty;
            if (tasksLoading || (sources != null && sources.Waiting(true)) || (KuliaOn && tasksError == null)) return DataState.Loading;
            return taskNote != null ? DataState.Failed : DataState.Loading;
        }

        DataState ScheduleState()
        {
            if (!KuliaOn && OnlyKuliaEvents) return DataState.Off;
            if (classes != null) return classes.Count > 0 ? DataState.Ready : DataState.Empty;
            if (tasksLoading || (sources != null && sources.Waiting(false)) || (KuliaOn && tasksError == null && classesError == null)) return DataState.Loading;
            return eventNote != null ? DataState.Failed : DataState.Loading;
        }

        // Kulia and every connected source
        void RefreshAll(int what)
        {
            refreshAsked = what;
            if (KuliaOn) RefreshTasks(false);
            int queued = sources != null ? sources.Refresh() : 0;
            if (!KuliaOn && queued == 0) refreshAsked = 0;
            Kick();
        }

        void SyncSourcesTick(DateTime now)
        {
            if (sources != null) sources.Tick(now);
        }

        // Nothing linked: one way to Settings
        void NothingConnected(float w, string id, bool forTasks)
        {
            bool any = sources != null && sources.Any;
            string title = !any ? Lang.T("Belum ada yang tersambung") : forTasks ? Lang.T("Belum ada sumber tugas") : Lang.T("Belum ada sumber jadwal");
            string detail = !any ? Lang.T("Kulia, Notion, Todoist, atau kalender iCal") : forTasks ? Lang.T("Kulia, Notion, Todoist, atau kalender tugas") : Lang.T("Kulia, atau kalender Google, Outlook, iCloud");
            EmptyState(new RectangleF(0, PanelTop, w, PanelHeight() - PanelTop - 10), Ink.IcoCalendar, title, detail, Lang.T("Buka Integrasi"), id, delegate { OpenIntegrations(); }, PillKind.Primary);
        }

        void OpenIntegrations()
        {
            expanded = false;
            Kick();
            if (prefs == null || prefs.IsDisposed) prefs = new PrefsWindow(this);
            prefs.ShowSection("integrasi");
            if (!prefs.Visible) prefs.Show();
            else prefs.Activate();
        }

        void CloseExternal(KuliaTask task)
        {
            string old = task.Status;
            task.Status = "selesai";
            if (tasks != null) tasks.Remove(task);
            Kick();
            sources.Close(task, err =>
            {
                if (err == null) { TaskFinished(task); return; }
                task.Status = old;
                MergeSources();
                ShowInfo(Ink.IcoLock, err, task.Title, true);
            });
        }

        // Date-only tasks show no time
        static string Relative(KuliaTask t, DateTime nowUtc)
        {
            if (!t.DateOnly) return Relative(t.DeadlineUtc, nowUtc);
            var day = t.DeadlineUtc.ToLocalTime().Date;
            var today = DateTime.Now.Date;
            if (day < today) return Lang.F("lewat {0}", (int)(today - day).TotalDays + Lang.T(" hari"));
            if (day == today) return Lang.T("hari ini");
            if (day == today.AddDays(1)) return Lang.T("besok");
            if ((day - today).TotalDays < 7) return day.ToString("dddd", Fmt.Id);
            return day.ToString("d MMM", Fmt.Id);
        }
    }
}
