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
    // Subscribed sources; sync off the UI thread
    class SourceHub
    {
        public NotionSource Notion;
        public TodoistSource Todoist;
        readonly GoogleCalendarSource google = new GoogleCalendarSource();
        public readonly NationalHolidaysSource Holidays = new NationalHolidaysSource();
        public readonly GoogleTasksSource GoogleTasks = new GoogleTasksSource();
        public readonly List<IcsFeed> Feeds = new List<IcsFeed>();

        public GoogleCalendarSource Google { get { return google; } }
        public bool GoogleConnected { get { return google.Connected; } }
        public string GoogleEmail { get { return google.Email; } }
        // true: data changed
        public event Action<bool> Changed;

        readonly Action<Action> post;
        readonly object gate = new object();
        readonly Queue<Job> pending = new Queue<Job>();
        bool running;

        class Job
        {
            public Source Src;
            public object Snap;
            public int Version;
            public SourceResult Result;
        }

        public SourceHub(Action<Action> post) { this.post = post; }

        public void Load()
        {
            Notion = NotionSource.Load();
            Todoist = TodoistSource.Load();
            Feeds.Clear();
            Feeds.AddRange(IcsFeed.LoadAll());
            google.Reload();
        }

        public List<Source> All()
        {
            var list = new List<Source>();
            if (Notion != null) list.Add(Notion);
            if (Todoist != null) list.Add(Todoist);
            list.AddRange(Feeds);
            if (google.Connected) list.Add(google);
            if (google.Connected && GoogleTasks.Enabled) list.Add(GoogleTasks);
            if (Holidays.Enabled) list.Add(Holidays);
            return list;
        }

        public bool Any { get { return Notion != null || Todoist != null || google.Connected || Holidays.Enabled || Feeds.Count > 0; } }

        public bool Busy
        {
            get
            {
                if ((Notion != null && Notion.Busy) || (Todoist != null && Todoist.Busy)) return true;
                if (google.Busy) return true;
                if (GoogleTasks.Busy) return true;
                if (Holidays.Busy) return true;
                foreach (var f in Feeds) if (f.Busy) return true;
                return false;
            }
        }

        public bool HasTasks
        {
            get
            {
                if (Notion != null || Todoist != null) return true;
                foreach (var f in Feeds) if (f.GivesTasks) return true;
                if (google.Connected && GoogleTasks.Enabled) return true;
                return false;
            }
        }

        public bool HasEvents
        {
            get
            {
                foreach (var f in Feeds) if (f.GivesEvents) return true;
                if (google.Connected && google.GivesEvents) return true;
                if (Holidays.Enabled && Holidays.GivesEvents) return true;
                return false;
            }
        }

        // Never tried yet, or syncing
        public bool Waiting(bool tasks)
        {
            if (Notion != null && Pending(Notion, tasks)) return true;
            if (Todoist != null && Pending(Todoist, tasks)) return true;
            if (google.Connected && Pending(google, tasks)) return true;
            if (google.Connected && GoogleTasks.Enabled && Pending(GoogleTasks, tasks)) return true;
            foreach (var f in Feeds) if (Pending(f, tasks)) return true;
            if (Holidays.Enabled && Pending(Holidays, tasks)) return true;
            return false;
        }

        static bool Pending(Source s, bool tasks) { return (tasks ? s.GivesTasks : s.GivesEvents) && (s.Busy || s.TriedUtc == DateTime.MinValue); }

        public Source Find(string key)
        {
            foreach (var s in All()) if (s.Key == key) return s;
            return null;
        }

        bool Has(Source s) { return s == Notion || s == Todoist || (s == google && google.Connected) || (s == GoogleTasks && google.Connected && GoogleTasks.Enabled) || (s == Holidays && Holidays.Enabled) || (s is IcsFeed && Feeds.Contains((IcsFeed)s)); }

        // Every second; queues what is due
        public void Tick(DateTime now)
        {
            if (Notion != null && Due(Notion, now)) Queue(Notion);
            if (Todoist != null && Due(Todoist, now)) Queue(Todoist);
            if (google.Connected && Due(google, now)) Queue(google);
            if (google.Connected && GoogleTasks.Enabled && Due(GoogleTasks, now)) Queue(GoogleTasks);
            for (int i = 0; i < Feeds.Count; i++) if (Due(Feeds[i], now)) Queue(Feeds[i]);
            if (Holidays.Enabled && Due(Holidays, now)) Queue(Holidays);
        }

        static bool Due(Source s, DateTime now) { return !s.Busy && now >= s.NextUtc && now >= s.HoldUtc; }

        // Page opened: refresh stale, healthy sources
        public void Stale(TimeSpan age)
        {
            var now = DateTime.UtcNow;
            foreach (var s in All())
                if (!s.Busy && !s.Stopped && s.Failures == 0 && now - s.TriedUtc > age && now >= s.HoldUtc) Queue(s);
        }

        // Manual refresh; ten seconds apart
        public int Refresh()
        {
            var now = DateTime.UtcNow;
            int n = 0;
            foreach (var s in All())
                if (!s.Busy && (now - s.TriedUtc).TotalSeconds >= 10 && now >= s.HoldUtc) { s.Stopped = false; Queue(s); n++; }
            return n;
        }

        // Network back: retry failures now
        public void Retry()
        {
            var now = DateTime.UtcNow;
            foreach (var s in All()) if (!s.Busy && !s.Stopped && s.Failures > 0 && now >= s.HoldUtc) Queue(s);
        }

        public void Queue(Source s)
        {
            if (s.Busy) return;
            s.Busy = true;
            var job = new Job { Src = s, Snap = s.Snapshot(), Version = s.Version };
            bool start;
            lock (gate)
            {
                pending.Enqueue(job);
                start = !running;
                running = true;
            }
            if (start) ThreadPool.QueueUserWorkItem(delegate { Work(); });
            Raise(false);
        }

        // One request queue for every source
        void Work()
        {
            while (true)
            {
                Job job;
                lock (gate)
                {
                    if (pending.Count == 0) { running = false; return; }
                    job = pending.Dequeue();
                }
                // Dropped or reconfigured meanwhile: no request
                if (!job.Src.Gone && Thread.VolatileRead(ref job.Src.Version) == job.Version)
                {
                    try { job.Result = job.Src.Fetch(job.Snap, DateTime.UtcNow); }
                    catch (Exception ex) { job.Result = SourceResult.Fail(Lang.T("Gagal memuat"), ex.GetType().Name, false); }
                }
                if (job.Result == null) job.Result = SourceResult.Fail(Lang.T("Gagal memuat"), "empty", false);
                var done = job;
                post(delegate { Apply(done); });
            }
        }

        void Apply(Job job)
        {
            var s = job.Src;
            var now = DateTime.UtcNow;
            s.Busy = false;
            s.TriedUtc = now;
            if (!Has(s)) { Raise(false); return; }
            var r = job.Result;
            if (r.RetryAfter > 0) s.HoldUtc = now.AddSeconds(r.RetryAfter);
            // Settings changed mid-fetch: fetch again
            if (job.Version != s.Version) { s.NextUtc = now; Raise(false); return; }
            bool data = r.Tasks != null || r.Events != null;
            if (data)
            {
                s.Tasks = r.Tasks;
                s.Events = r.Events;
                s.OkUtc = now;
            }
            s.Error = r.Error;
            s.Stopped = r.Stop && !data;
            if (r.Error == null || data)
            {
                s.Failures = 0;
                s.NextUtc = now + Interval(s, r);
            }
            else if (s.Stopped) s.NextUtc = DateTime.MaxValue;
            else
            {
                s.Failures++;
                s.NextUtc = now + Backoff(s.Failures, r.RetryAfter);
            }
            if (s.Absorb(r)) Persist(s);
            Log.Write("source " + s.Key + (r.Error == null ? ": ok " + Count(s.Tasks) + "/" + Count(s.Events) : ": " + (r.Log ?? "error")));
            Raise(data || r.Error != null);
        }

        void Persist(Source s)
        {
            if (s == Notion) Notion.Save();
            else if (s is IcsFeed) IcsFeed.SaveAll(Feeds);
        }

        static int Count<T>(List<T> list) { return list == null ? 0 : list.Count; }

        // Feed hint wins, within 15 min..6 h
        static TimeSpan Interval(Source s, SourceResult r)
        {
            var every = r.Every > s.Every ? r.Every : s.Every;
            return every > TimeSpan.FromHours(6) ? TimeSpan.FromHours(6) : every;
        }

        static TimeSpan Backoff(int failures, int retryAfter)
        {
            if (retryAfter > 0) return TimeSpan.FromSeconds(Math.Max(30, retryAfter));
            int[] steps = { 2, 5, 10, 20, 40, 60 };
            return TimeSpan.FromMinutes(steps[Math.Min(failures, steps.Length) - 1]);
        }

        void Raise(bool data)
        {
            var h = Changed;
            if (h != null) h(data);
        }

        // Quick note as a task; result on the UI thread
        public void Create(string key, string text, Action<string> done)
        {
            var s = Find(key);
            if (s == null) { done(Lang.T("Sumbernya sudah dilepas")); return; }
            var snap = s.Snapshot();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                try { err = s.Create(snap, text); }
                catch (Exception ex) { err = Lang.T("Gagal tersimpan"); Log.Write("source " + s.Key + " create: " + ex.GetType().Name); }
                post(delegate { done(err); });
            });
        }

        // Mark done; result on the UI thread
        public void Close(KuliaTask t, Action<string> done)
        {
            var s = Find(t.Source);
            if (s == null) { done(Lang.T("Sumbernya sudah dilepas")); return; }
            var snap = s.Snapshot();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                try { err = s.Close(snap, t); }
                catch (Exception ex) { err = Lang.T("Gagal tersimpan"); Log.Write("source " + s.Key + " close: " + ex.GetType().Name); }
                post(delegate
                {
                    // A fetch in flight may predate this
                    if (err == null && Has(s))
                    {
                        if (s.Busy) s.Version++;
                        else s.NextUtc = DateTime.UtcNow.AddSeconds(3);
                    }
                    if (err != null && s == Notion && Notion.NoWrite) Notion.Save();
                    done(err);
                    Raise(true);
                });
            });
        }

        // Settings changed: fetch with new config
        public void Touch(Source s)
        {
            // Stale handle from an old settings row
            if (!Has(s)) return;
            s.Version++;
            s.Stopped = false;
            // Rate limited: Tick sends it later
            if (!s.Busy && DateTime.UtcNow >= s.HoldUtc) Queue(s);
            else s.NextUtc = DateTime.UtcNow;
            Raise(true);
        }

        static void Retire(Source s)
        {
            if (s == null) return;
            s.Version++;
            s.Gone = true;
        }

        public void SetNotion(NotionSource n)
        {
            Retire(Notion);
            Notion = n;
            n.Save();
            Queue(n);
            Raise(true);
        }

        public void DropNotion()
        {
            if (Notion == null) return;
            Retire(Notion);
            Notion = null;
            NotionSource.Forget();
            Raise(true);
        }

        public void SetTodoist(string token)
        {
            Retire(Todoist);
            Todoist = new TodoistSource { Token = token };
            Todoist.Save();
            Queue(Todoist);
            Raise(true);
        }

        public void DropTodoist()
        {
            if (Todoist == null) return;
            Retire(Todoist);
            Todoist = null;
            TodoistSource.Forget();
            Raise(true);
        }

        public void RefreshGoogle()
        {
            google.Reload();
            if (!google.Connected) return;
            google.Gone = false;
            google.Version++;
            google.Stopped = false;
            google.Error = null;
            google.HoldUtc = DateTime.MinValue;
            google.Failures = 0;
            if (!google.Busy) Queue(google);
            else google.NextUtc = DateTime.UtcNow;
            if (GoogleTasks.Enabled)
            {
                GoogleTasks.Gone = false;
                GoogleTasks.Version++;
                GoogleTasks.Stopped = false;
                GoogleTasks.Error = null;
                GoogleTasks.HoldUtc = DateTime.MinValue;
                GoogleTasks.Failures = 0;
                if (!GoogleTasks.Busy) Queue(GoogleTasks);
                else GoogleTasks.NextUtc = DateTime.UtcNow;
            }
            Raise(true);
        }

        public void RefreshHolidays()
        {
            Holidays.Version++;
            Holidays.Stopped = false;
            Holidays.Error = null;
            Holidays.HoldUtc = DateTime.MinValue;
            Holidays.Failures = 0;
            if (!Holidays.Enabled)
            {
                Holidays.Events = null;
                Raise(true);
                return;
            }
            if (!Holidays.Busy) Queue(Holidays);
            else Holidays.NextUtc = DateTime.UtcNow;
            Raise(true);
        }

        public void DisconnectGoogle()
        {
            bool was = google.Connected || google.Events != null || google.Error != null;
            GoogleCalVault.Forget();
            google.Reload();
            google.Version++;
            google.Events = null;
            google.Error = null;
            google.OkUtc = DateTime.MinValue;
            google.TriedUtc = DateTime.MinValue;
            google.NextUtc = DateTime.MinValue;
            google.HoldUtc = DateTime.MinValue;
            google.Failures = 0;
            google.Stopped = false;
            if (was) Raise(true);
        }

        public IcsFeed AddFeed(string id, string url, string kind, SourceResult first)
        {
            var f = IcsFeed.Make(id, url, first != null ? first.Name : "", kind);
            Feeds.Add(f);
            if (first != null && first.Error == null)
            {
                f.Tasks = first.Tasks;
                f.Events = first.Events;
                f.OkUtc = f.TriedUtc = DateTime.UtcNow;
                f.NextUtc = DateTime.UtcNow + Interval(f, first);
                f.Absorb(first);
            }
            else Queue(f);
            IcsFeed.SaveAll(Feeds);
            Raise(true);
            return f;
        }

        public void RemoveFeed(IcsFeed f)
        {
            Retire(f);
            Feeds.Remove(f);
            IcsFeed.SaveAll(Feeds);
            Raise(true);
        }

        public void SetFeedKind(IcsFeed f, string kind)
        {
            if (f.Kind == kind || !Feeds.Contains(f)) return;
            f.Kind = kind;
            IcsFeed.SaveAll(Feeds);
            Touch(f);
        }

        // Status line for settings
        public static string Status(Source s)
        {
            if (s.Busy && s.OkUtc == DateTime.MinValue) return Lang.T("Memuat…");
            if (s.Error != null) return s.OkUtc == DateTime.MinValue ? s.Error : s.Error + " · " + Lang.F("data {0}", Fmt.Ago(s.OkUtc));
            if (s.OkUtc == DateTime.MinValue) return Lang.T("Menunggu sinkron");
            var parts = new List<string>();
            if (s.Tasks != null && (s.Tasks.Count > 0 || s.Events == null)) parts.Add(s.Tasks.Count == 1 ? Lang.T("1 tugas") : Lang.F("{0} tugas", s.Tasks.Count));
            if (s.Events != null && (s.Events.Count > 0 || s.Tasks == null || s.Tasks.Count == 0)) parts.Add(s.Events.Count == 1 ? Lang.T("1 acara") : Lang.F("{0} acara", s.Events.Count));
            parts.Add(s.Busy ? Lang.T("memperbarui…") : Fmt.Ago(s.OkUtc));
            return string.Join(" · ", parts);
        }

        // Labels only when lists mix sources
        static Dictionary<string, string> names = new Dictionary<string, string>();
        static bool taskLabels, eventLabels;

        public static void SetLabels(Dictionary<string, string> map, bool tasks, bool events)
        {
            names = map;
            taskLabels = tasks;
            eventLabels = events;
        }

        public static string TaskLabel(string key) { return taskLabels ? Name(key) : ""; }
        public static string EventLabel(string key) { return eventLabels ? Name(key) : ""; }

        static string Name(string key)
        {
            string n;
            return key != null && names.TryGetValue(key, out n) ? n : "";
        }
    }
}
