using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    // Deskmate.exe --ingat --parent <pid>: the activity memory service, like PiecesOS beside its apps.
    // Its own process: a slow UI Automation call or a model on the CPU never touches the dock.
    sealed class IngatService
    {
        public static string MutexName { get { return Settings.MutexName.Replace("DeskmateIsland", "DeskmateIngat"); } }
        public static string ApiFile { get { return Path.Combine(IngatStore.Dir, "api.json"); } }

        // A switch to another window must hold this long to count
        const int SettleMs = 1500;
        // Same app or project after a gap this short continues the episode
        const double JoinSeconds = 120;
        const double AwaySeconds = 180;
        const int LookMs = 5000, RereadMs = 30000;

        public static int Run(string[] args)
        {
            int parent = 0;
            int pi = Array.IndexOf(args, "--parent");
            if (pi > 0 && pi + 1 < args.Length) int.TryParse(args[pi + 1], out parent);
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) return 0;
                try
                {
                    using (var self = Process.GetCurrentProcess()) self.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch (Exception) { }
                IngatService svc = null;
                try
                {
                    svc = new IngatService(parent);
                    svc.Loop();
                }
                catch (Exception ex) { Log.Write("ingat: " + ex); Environment.Exit(1); }
                finally { if (svc != null) svc.Shut(); }
            }
            Log.Write("ingat: berhenti");
            // UI Automation leaves foreground threads behind; nothing else is left to finish
            Environment.Exit(0);
            return 0;
        }

        readonly int parentPid;
        readonly Settings settings = new Settings();
        public readonly IngatStore Store;
        public readonly IngatBrain Brain;
        IngatApi api;
        readonly AutoResetEvent nudge = new AutoResetEvent(false);
        volatile bool stopping;

        // Current episode, guarded by gate
        readonly object gate = new object();
        IngatEpisode current;
        string currentKey = "";
        IntPtr currentHwnd;
        DateTime lastSeenUtc;
        ulong lastHash;
        DateTime lastReadUtc, lastLookUtc;
        DateTime pausedUntilUtc = DateTime.MinValue;
        public string State = "on";

        IngatService(int parent)
        {
            parentPid = parent;
            Ai.Bind(settings);
            Lang.English = settings.Get("lang", "id") == "en";
            Directory.CreateDirectory(IngatStore.Dir);
            Store = new IngatStore(IngatStore.DbPath);
            Store.CloseStale();
            Brain = new IngatBrain(Store, settings);
            long until;
            if (long.TryParse(Store.Meta("paused.until"), out until)) pausedUntilUtc = IngatDb.FromUnix(until);
        }

        void Shut()
        {
            stopping = true;
            lock (gate) CloseCurrent(DateTime.UtcNow);
            if (api != null) api.Stop();
            try { if (File.Exists(ApiFile)) File.Delete(ApiFile); } catch (Exception) { }
            Store.Dispose();
        }

        void Loop()
        {
            string token = NewToken();
            int port = AgentHub.FreePort();
            api = new IngatApi(this, port, token);
            if (!api.Start()) throw new InvalidOperationException("api port " + port);
            AtomicFile.Write(ApiFile, new UTF8Encoding(false).GetBytes(Json.Encode(new Dictionary<string, object> { { "port", port }, { "token", token }, { "pid", Process.GetCurrentProcess().Id } })));
            Log.Write("ingat: siap di port " + port);

            var hooks = new Thread(Prof.Wrap(HookLoop)) { IsBackground = true, Name = "ingat hooks" };
            hooks.SetApartmentState(ApartmentState.STA);
            hooks.Start();
            var worker = new Thread(Prof.Wrap(WorkLoop)) { IsBackground = true, Name = "ingat worker", Priority = ThreadPriority.Lowest };
            worker.Start();

            // Capture runs here: one thread, so UI Automation never overlaps itself
            DateTime settingsCheck = DateTime.MinValue;
            while (!stopping)
            {
                // Typing in a chat: look more often so the sent line is caught whole
                nudge.WaitOne(Composing ? 1500 : LookMs);
                if (ParentGone()) { Log.Write("ingat: Deskmate sudah tutup"); break; }
                if ((DateTime.UtcNow - settingsCheck).TotalSeconds >= 10)
                {
                    settingsCheck = DateTime.UtcNow;
                    settings.Load();
                    if (!settings.GetBool("ingat", false)) break;
                }
                try { Capture(); }
                catch (Exception ex) { Log.Write("ingat capture: " + ex.Message); }
            }
            stopping = true;
        }

        bool ParentGone()
        {
            if (parentPid <= 0) return false;
            try { using (var p = Process.GetProcessById(parentPid)) return p.HasExited; }
            catch (ArgumentException) { return true; }
            catch (InvalidOperationException) { return true; }
        }

        static string NewToken()
        {
            var b = new byte[24];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // Window events

        Native.WinEventProc proc;
        DateTime changedUtc = DateTime.MinValue;

        void HookLoop()
        {
            proc = OnWinEvent;
            // Foreground switches, and title changes (browser tabs, editor files)
            var h1 = Native.SetWinEventHook(3, 3, IntPtr.Zero, proc, 0, 0, 0);
            var h2 = Native.SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, proc, 0, 0, 0);
            Application.Run();
            Native.UnhookWinEvent(h1);
            Native.UnhookWinEvent(h2);
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            // Name change: only the window itself, not its children
            if (evt == 0x800C && (idObject != 0 || hwnd != Native.GetForegroundWindow())) return;
            changedUtc = DateTime.UtcNow;
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(SettleMs); nudge.Set(); });
        }

        // One look at the screen

        public bool Paused { get { return DateTime.UtcNow < pausedUntilUtc; } }

        public void Pause(int minutes)
        {
            pausedUntilUtc = minutes <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddMinutes(minutes);
            Store.SetMeta("paused.until", IngatDb.Unix(pausedUntilUtc).ToString(CultureInfo.InvariantCulture));
            if (minutes > 0) lock (gate) CloseCurrent(DateTime.UtcNow);
            nudge.Set();
        }

        public DateTime PausedUntilUtc { get { return pausedUntilUtc; } }

        void Capture()
        {
            var now = DateTime.UtcNow;
            if ((now - changedUtc).TotalMilliseconds < SettleMs - 50) return;
            if (Paused) { State = "paused"; return; }
            if (IdleSeconds() >= AwaySeconds) { State = "away"; lock (gate) CloseCurrent(lastSeenUtc); return; }

            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            int pid;
            string exe = Exe(ref hwnd, out pid);
            // Deskmate's own windows (pet, island, settings) are not activity, and must not cut the episode in two
            if (exe.Length == 0 || pid == Process.GetCurrentProcess().Id || exe == SelfExe) return;
            string title = Title(hwnd);
            if (title.Length == 0 || exe == "explorer" && (title == "Program Manager" || title.Length == 0)) return;

            var blockedApps = List("ingat.block.apps");
            var blockedSites = List("ingat.block.sites");
            if (IngatPrivacy.BlockedApp(exe, blockedApps) || IngatPrivacy.PrivateWindow(title, "", blockedSites))
            {
                State = "private";
                lock (gate) CloseCurrent(now);
                return;
            }
            State = "on";

            bool newWindow = hwnd != currentHwnd;
            bool titleChanged = current == null || current.Title != IngatPrivacy.Redact(title);
            bool due = (now - lastReadUtc).TotalMilliseconds >= RereadMs;
            // While typing the loop runs faster; the heavy read still waits its 5 s
            bool look = settings.GetBool("ingat.text", true) && (now - lastLookUtc).TotalMilliseconds >= LookMs - 200;

            // Text: on arrival, on a new title, or when the picture changed enough
            string text = "", url = "", source = "window";
            Bitmap shot = null;
            try
            {
                bool read = settings.GetBool("ingat.text", true) && (newWindow || titleChanged || due);
                if (!read && look)
                {
                    lastLookUtc = now;
                    shot = IngatText.Snap(hwnd);
                    if (shot != null && IngatText.Distance(IngatText.Hash(shot), lastHash) > 10) read = true;
                }
                if (read)
                {
                    bool secret;
                    text = IngatText.Read(hwnd, exe, out url, out secret);
                    source = "uia";
                    if (secret) { text = ""; source = "window"; }
                    // Nothing readable: let OCR look, unless saving battery or a game owns the screen
                    else if (text.Length < 80 && settings.GetBool("ingat.ocr", true) && !LowPower() && !Fullscreen(hwnd))
                    {
                        if (shot == null) shot = IngatText.Snap(hwnd);
                        if (shot != null) { string seen = IngatText.Ocr(shot); if (seen.Length > text.Length) { text = seen; source = "ocr"; } }
                    }
                    if (shot == null) shot = IngatText.Snap(hwnd);
                    if (shot != null) lastHash = IngatText.Hash(shot);
                    lastReadUtc = lastLookUtc = now;
                }
            }
            finally { if (shot != null) shot.Dispose(); }

            url = IngatPrivacy.CleanUrl(url);
            if (url.Length == 0 && current != null && !newWindow) url = current.Url;
            if (url.Length > 0 && IngatPrivacy.PrivateWindow(title, url, blockedSites)) { State = "private"; lock (gate) CloseCurrent(now); return; }
            text = IngatPrivacy.Redact(text);
            string cleanTitle = IngatPrivacy.Redact(title);

            string app = AppName(exe);
            string project = IngatPrivacy.Project(exe, title);
            string host = IngatPrivacy.Host(url);
            // Chats and mail: each conversation is its own episode when the title names it
            string channel = settings.GetBool("ingat.chat", true) ? IngatChat.Kind(exe, url, title) : "";
            string conversation = channel == "chat" ? IngatChat.Conversation(title) : "";
            if (channel.Length > 0 && source != "window") source = "chat";
            string key = exe + "|" + (project.Length > 0 ? project : conversation.Length > 0 ? "c:" + IngatChat.Norm(conversation) : host.Length > 0 ? host : "");

            // What the user is writing: the focused field's own text, never keystrokes
            string draft = null, field = "";
            if (settings.GetBool("ingat.typed", true))
            {
                bool secretField;
                draft = IngatChat.Draft(hwnd, out secretField, out field);
                if (secretField) draft = null;
                if (draft != null) draft = IngatPrivacy.Redact(draft);
            }

            lock (gate)
            {
                bool join = current != null && key == currentKey && (now - lastSeenUtc).TotalSeconds <= JoinSeconds;
                if (!join)
                {
                    CloseCurrent(lastSeenUtc > DateTime.MinValue && current != null ? lastSeenUtc : now);
                    long id = Store.Open(now, app, exe, project, cleanTitle, url, "");
                    if (channel.Length > 0) Store.SetChannel(id, channel);
                    current = Store.Get(id);
                    currentKey = key;
                }
                Store.Touch(current.Id, now, cleanTitle, url, text);
                if (text.Length > 0 || titleChanged || newWindow) Store.AddEvent(now, current.Id, source, app, cleanTitle, url, text);
                TrackDraft(now, hwnd, field, draft, app, channel, conversation, cleanTitle, url);
                current.Title = cleanTitle;
                current.Url = url.Length > 0 ? url : current.Url;
                current.EndUtc = now;
                currentHwnd = hwnd;
                lastSeenUtc = now;
            }
        }

        // Drafts: kept when they settle, and remembered as sent when the field empties in the same window
        string lastDraft = "";
        IntPtr lastDraftHwnd;
        string lastDraftField = "", lastDraftTitle = "";
        string lastDraftSaved = "";
        public volatile bool Composing;
        Dictionary<string, object> composing;
        DateTime composingSinceUtc;

        void TrackDraft(DateTime now, IntPtr hwnd, string field, string draft, string app, string channel, string conversation, string title, string url)
        {
            string d = draft ?? "";
            // Same window, same box, same tab or chat: anything else emptying is a switch, not a send
            bool sameField = hwnd == lastDraftHwnd && field.Length > 0 && field == lastDraftField && title == lastDraftTitle;
            if (sameField && lastDraft.Length >= 2 && d.Length == 0 && draft != null)
            {
                // The text left the box: in a chat that means it was sent
                Store.AddEvent(now, current.Id, channel.Length > 0 ? "sent" : "typed", app, title, url, lastDraft);
                Store.Touch(current.Id, now, "", "", (channel.Length > 0 ? "Aku menulis: " : "") + lastDraft);
                if (channel == "chat" && conversation.Length > 0)
                {
                    long conv = Store.FindConversation(app, conversation);
                    if (conv == 0) conv = Store.Conversation(app, conversation, "person", 0, now);
                    Store.AddMessage(conv, now, "me", true, lastDraft, current.Id);
                }
                lastDraftSaved = "";
            }
            else if (d.Length >= 2 && d == lastDraft && d != lastDraftSaved)
            {
                // Unchanged for one look: worth keeping as the latest draft
                Store.AddEvent(now, current.Id, "draft", app, title, url, d);
                lastDraftSaved = d;
            }
            if (d.Length >= 2 && channel.Length > 0)
            {
                if (!Composing || !sameField) composingSinceUtc = now;
                composing = new Dictionary<string, object>
                {
                    { "app", app }, { "channel", channel }, { "conversation", conversation }, { "title", title },
                    { "chars", d.Length }, { "since", composingSinceUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) },
                };
                Composing = true;
            }
            else { Composing = false; composing = null; }
            lastDraft = d;
            lastDraftHwnd = hwnd;
            lastDraftField = field;
            lastDraftTitle = title;
        }

        public Dictionary<string, object> ComposingNow()
        {
            var c = composing;
            return c != null ? new Dictionary<string, object>(c) : new Dictionary<string, object>();
        }

        // Heads-up for the message being written now
        public Dictionary<string, object> BriefNow()
        {
            var c = composing;
            if (c == null) return new Dictionary<string, object> { { "brief", "" } };
            string app = Json.Str(c, "app"), conv = Json.Str(c, "conversation");
            string brief = Brain.Brief(app, conv, ChatScreen(), lastDraft);
            return new Dictionary<string, object> { { "brief", brief }, { "app", app }, { "conversation", conv } };
        }

        // What the open chat shows right now, for the writing brief
        public string ChatScreen()
        {
            lock (gate)
            {
                if (current == null) return "";
                var ev = Store.EventsOf(current.Id, 400);
                for (int i = ev.Count - 1; i >= 0; i--) if (ev[i][1] == "chat" || ev[i][1] == "uia") return ev[i][3];
                return "";
            }
        }

        void CloseCurrent(DateTime at)
        {
            if (current == null) return;
            try
            {
                Store.Close(current.Id, at > current.StartUtc ? at : current.EndUtc);
                Store.DropTiny(current.Id);
            }
            catch (Exception ex) { Log.Write("ingat tutup: " + ex.Message); }
            current = null;
            currentKey = "";
            currentHwnd = IntPtr.Zero;
        }

        // Clipboard text and other events from the island process
        public void External(string source, string text)
        {
            text = IngatPrivacy.Redact(text ?? "");
            if (text.Length == 0 || Paused) return;
            if (text.Length > 4000) text = text.Substring(0, 4000);
            lock (gate)
            {
                if (current == null) return;
                Store.AddEvent(DateTime.UtcNow, current.Id, source, current.App, current.Title, current.Url, text);
                Store.Touch(current.Id, DateTime.UtcNow, "", "", text);
            }
        }

        // What the user is on right now, and how long they have kept at it
        public Dictionary<string, object> Now()
        {
            var d = new Dictionary<string, object>
            {
                { "state", State },
                { "idle", (int)IdleSeconds() },
            };
            if (Paused) d["pausedUntil"] = PausedUntilUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            IngatEpisode ep;
            lock (gate) ep = current;
            if (ep == null) return d;
            d["episode"] = ep.ToJson(false);
            var c = composing;
            if (c != null) d["composing"] = c;
            // Streak: same project (or app) with no break over 10 minutes
            var recent = Store.Timeline(DateTime.UtcNow.AddHours(-6), DateTime.UtcNow, 400);
            double minutes = 0;
            DateTime edge = DateTime.UtcNow;
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var e = recent[i];
                bool same = ep.Project.Length > 0 ? e.Project == ep.Project : e.Exe == ep.Exe;
                if (!same) { if ((e.EndUtc - e.StartUtc).TotalMinutes > 10) break; continue; }
                if ((edge - e.EndUtc).TotalMinutes > 10) break;
                minutes += (e.EndUtc - e.StartUtc).TotalMinutes;
                edge = e.StartUtc;
            }
            d["streakMinutes"] = (int)Math.Round(minutes);
            return d;
        }

        // Background work: summaries, embeddings, days, meetings, retention

        void WorkLoop()
        {
            DateTime retained = DateTime.MinValue, meetings = DateTime.MinValue;
            while (!stopping)
            {
                Thread.Sleep(15000);
                try
                {
                    if ((DateTime.UtcNow - meetings).TotalMinutes >= 2) { meetings = DateTime.UtcNow; ScanMeetings(); }
                    if ((DateTime.UtcNow - retained).TotalHours >= 6)
                    {
                        retained = DateTime.UtcNow;
                        Store.Retain(settings.GetInt("ingat.keep.events", 14), settings.GetInt("ingat.keep.days", 180));
                    }
                    // Model work waits for a calm moment and wall power
                    if (LowPower()) continue;
                    if (IdleSeconds() < 20 && !settings.GetBool("ingat.busywork", true)) continue;
                    for (int round = 0; round < 4 && !stopping && Brain.Work(); round++) { }
                }
                catch (Exception ex) { Log.Write("ingat worker: " + ex.Message); }
            }
        }

        // Rapat writes <date time app>.md; each new one becomes a meeting episode
        void ScanMeetings()
        {
            string dir = Path.Combine(Settings.Dir, "rapat");
            if (!Directory.Exists(dir)) return;
            foreach (var path in Directory.GetFiles(dir, "*.md"))
            {
                if (Store.HasMeeting(path)) continue;
                var info = new FileInfo(path);
                if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < 30) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                DateTime start;
                if (name.Length < 16 || !DateTime.TryParseExact(name.Substring(0, 16), "yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out start))
                    start = info.CreationTime;
                string app = name.Length > 17 ? name.Substring(17).Trim() : "";
                string text;
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch (IOException) { continue; }
                var end = info.LastWriteTimeUtc > start.ToUniversalTime() ? info.LastWriteTimeUtc : start.ToUniversalTime().AddMinutes(1);
                Store.AddMeeting(path, start.ToUniversalTime(), end, app.Length > 0 ? app : Lang.T("Rapat"), Lang.T("Rapat") + (app.Length > 0 ? " · " + app : ""), IngatPrivacy.Redact(text));
            }
        }

        // Helpers

        HashSet<string> List(string key)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in settings.Get(key, "").Split(',', ';', '\n'))
                if (s.Trim().Length > 0) set.Add(s.Trim());
            return set;
        }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr p, int flags, StringBuilder s, ref int size);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        static readonly string SelfExe = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName).ToLowerInvariant();

        public static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return (uint)(Environment.TickCount - (int)li.dwTime) / 1000.0;
        }

        static string Title(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString().Trim();
        }

        static string ImagePath(int pid)
        {
            IntPtr p = OpenProcess(0x1000, false, pid);
            if (p == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : "";
            }
            finally { CloseHandle(p); }
        }

        readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Store apps live inside ApplicationFrameHost: the real app is a child from another process
        string Exe(ref IntPtr hwnd, out int pid)
        {
            Native.GetWindowThreadProcessId(hwnd, out pid);
            string path = ImagePath(pid);
            string exe = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (exe == "applicationframehost")
            {
                int host = pid, found = 0;
                IntPtr child = IntPtr.Zero;
                EnumChildWindows(hwnd, delegate (IntPtr c, IntPtr l)
                {
                    int cp;
                    Native.GetWindowThreadProcessId(c, out cp);
                    if (cp != host) { found = cp; child = c; return false; }
                    return true;
                }, IntPtr.Zero);
                if (found != 0) { pid = found; path = ImagePath(found); exe = Path.GetFileNameWithoutExtension(path).ToLowerInvariant(); }
            }
            if (exe.Length > 0) lock (paths) paths[exe] = path;
            return exe;
        }

        readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // "Visual Studio Code" rather than "code"
        string AppName(string exe)
        {
            string n;
            lock (names) if (names.TryGetValue(exe, out n)) return n;
            n = "";
            string path;
            lock (paths) paths.TryGetValue(exe, out path);
            try { if (!string.IsNullOrEmpty(path)) n = (FileVersionInfo.GetVersionInfo(path).FileDescription ?? "").Trim(); }
            catch (Exception) { }
            if (n.Length == 0 || n.Length > 48) n = exe.Length > 0 ? char.ToUpperInvariant(exe[0]) + exe.Substring(1) : "";
            lock (names) names[exe] = n;
            return n;
        }

        static bool LowPower()
        {
            var ps = SystemInformation.PowerStatus;
            return ps.PowerLineStatus == PowerLineStatus.Offline && ps.BatteryLifePercent < 0.25f;
        }

        // A game or a presentation owns the screen; maximized windows do not count
        static bool Fullscreen(IntPtr hwnd)
        {
            int s;
            return SHQueryUserNotificationState(out s) == 0 && (s == 3 || s == 4);
        }
    }
}
