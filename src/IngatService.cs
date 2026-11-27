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
    // Its own process: UI Automation and local models never touch the dock's frame budget.
    sealed class IngatService
    {
        public static string MutexName { get { return Settings.MutexName.Replace("DeskmateIsland", "DeskmateIngat"); } }
        public static string ApiFile { get { return Path.Combine(IngatStore.Dir, "api.json"); } }

        // Short settle skips Alt+Tab pass-throughs while catching real switches fast
        const int SwitchSettleMs = 350;
        const int InteractSettleMs = 220;
        const int MaxDeferMs = 800;
        const int ComposeMs = 800;
        const int ActiveLookMs = 1800, IdleLookMs = 5000;
        const int ActiveRereadMs = 12000, IdleRereadMs = 30000;
        // Same app or project after a gap this short continues the episode
        const double JoinSeconds = 120;
        const double AwaySeconds = 180;
        // No keyboard or mouse for this long before the worker calls the model
        const double AiIdleSeconds = 45;
        // A window that keeps changing on its own (Task Manager, video, a log, a timer) while the user is
        // hands-off is looked at less and less often, up to MaxLookMs; any input or switch resets it
        const int MaxLookMs = 15000;
        const double HandsOffSeconds = 3;

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
        readonly int selfPid = Process.GetCurrentProcess().Id;
        readonly Settings settings = new Settings();
        public readonly IngatStore Store;
        public readonly IngatBrain Brain;
        IngatApi api;
        readonly AutoResetEvent nudge = new AutoResetEvent(false);
        readonly AutoResetEvent workNudge = new AutoResetEvent(false);
        volatile bool stopping;
        volatile bool loopReady;

        // Synchronous fresh-capture coordination for live API/MCP queries
        readonly object freshGate = new object();
        long captureStartSeq;
        long captureDoneSeq;
        long freshTargetSeq;
        bool freshWanted;
        long lastReadTicks;
        bool stampStale;

        // Current episode and latest structured screen snapshot, guarded by gate
        readonly object gate = new object();
        IngatEpisode current;
        IngatSnapshot latestSnap;
        string currentKey = "";
        string lastConversation = "";
        IntPtr currentHwnd;
        IntPtr lastUserHwnd;
        DateTime lastSeenUtc;
        IngatStamp lastStamp;
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
            nudge.Set();
            workNudge.Set();
            lock (freshGate) Monitor.PulseAll(freshGate);
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

            // Capture immediately on startup so current_context is populated right away
            loopReady = true;
            RunCapture(true);

            // Capture runs here: one thread, so UI Automation never overlaps itself
            DateTime settingsCheck = DateTime.UtcNow;
            while (!stopping)
            {
                int wait = NextWaitMs();
                if (wait > 0) nudge.WaitOne(wait);
                if (stopping) break;
                if (ParentGone()) { Log.Write("ingat: Deskmate sudah tutup"); break; }
                bool force;
                lock (freshGate) force = freshWanted;
                // Prioritize live forced queries ahead of periodic settings disk reload
                if (force) RunCapture(true);
                if ((DateTime.UtcNow - settingsCheck).TotalSeconds >= 8)
                {
                    settingsCheck = DateTime.UtcNow;
                    settings.Load();
                    if (!settings.GetBool("ingat", false)) break;
                }
                if (!force) RunCapture(false);
            }
            stopping = true;
            lock (freshGate) Monitor.PulseAll(freshGate);
        }

        // Looks in a row that found the foreground window changed while the user was hands-off (capture thread only)
        int churn;

        // Normal pace, doubled per changed hands-off look after the first, capped at MaxLookMs
        int LookMs(double idle)
        {
            int baseMs = idle < 15 ? ActiveLookMs : IdleLookMs;
            int shift = Math.Max(0, Math.Min(churn - 1, 4));
            return Math.Min(MaxLookMs, baseMs << shift);
        }

        int NextWaitMs()
        {
            lock (freshGate) if (freshWanted) return 0;
            int baseWait = Composing ? ComposeMs : LookMs(IdleSeconds());
            lock (eventGate)
            {
                if (dueUtc == DateTime.MinValue) return baseWait;
                int left = (int)Math.Ceiling((dueUtc - DateTime.UtcNow).TotalMilliseconds);
                return left <= 0 ? 0 : Math.Min(baseWait, Math.Max(15, left));
            }
        }

        void RunCapture(bool force)
        {
            long seq;
            lock (freshGate) seq = ++captureStartSeq;
            bool completed = false;
            try { completed = Capture(force); }
            catch (Exception ex) { Log.Write("ingat capture: " + ex.Message); completed = true; }
            if (!completed) return;
            lock (freshGate)
            {
                if (seq > captureDoneSeq) captureDoneSeq = seq;
                if (captureDoneSeq >= freshTargetSeq) freshWanted = false;
                Monitor.PulseAll(freshGate);
            }
        }

        bool ForegroundNeedsRefresh()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                int pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                if (pid != selfPid && (parentPid <= 0 || pid != parentPid) && fg != currentHwnd) return true;
            }
            lock (gate) return latestSnap == null && current == null;
        }

        // Requests an immediate single-thread capture when a caller asks for live context
        public void EnsureFresh(int maxWaitMs)
        {
            if (stopping || !loopReady || Paused) return;
            bool pending;
            lock (eventGate) pending = contentDirty || dueUtc > DateTime.MinValue;
            if (!pending && !ForegroundNeedsRefresh() && (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastReadTicks)) < 400 * TimeSpan.TicksPerMillisecond) return;

            long want;
            lock (freshGate)
            {
                want = captureStartSeq + 1;
                if (want > freshTargetSeq) freshTargetSeq = want;
                freshWanted = true;
            }
            nudge.Set();

            var sw = Stopwatch.StartNew();
            lock (freshGate)
            {
                while (!stopping && captureDoneSeq < want)
                {
                    int rem = maxWaitMs - (int)sw.ElapsedMilliseconds;
                    if (rem <= 0) break;
                    Monitor.Wait(freshGate, rem);
                }
            }
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

        // Window, focus, title, and scroll events

        Native.WinEventProc proc;
        readonly object eventGate = new object();
        IntPtr eventHwnd;
        DateTime switchUtc = DateTime.MinValue;
        DateTime deferSinceUtc = DateTime.MinValue;
        DateTime dueUtc = DateTime.MinValue;
        DateTime lastInteractNudgeUtc = DateTime.MinValue;
        bool contentDirty;

        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        void HookLoop()
        {
            proc = OnWinEvent;
            // WINEVENT_SKIPOWNPROCESS (2): foreground switches, scrolling end, focus moves, and window title changes
            var hFg = Native.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, proc, 0, 0, 2);
            var hScroll = Native.SetWinEventHook(0x0013, 0x0013, IntPtr.Zero, proc, 0, 0, 2);
            var hFocus = Native.SetWinEventHook(0x8005, 0x8005, IntPtr.Zero, proc, 0, 0, 2);
            var hName = Native.SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, proc, 0, 0, 2);
            Application.Run();
            Native.UnhookWinEvent(hFg);
            Native.UnhookWinEvent(hScroll);
            Native.UnhookWinEvent(hFocus);
            Native.UnhookWinEvent(hName);
        }

        void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero) return;
            // Name change: only the top-level window title, never noisy child text mutations
            if (evt == 0x800C && idObject != 0) return;

            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            int fgPid;
            Native.GetWindowThreadProcessId(fg, out fgPid);
            // Opening Deskmate's own Pet/Suruh/Island overlay must not clobber the user's active window settle state
            if (fgPid == selfPid || (parentPid > 0 && fgPid == parentPid)) return;
            if (hwnd != fg)
            {
                if (evt == 0x0003 || evt == 0x800C) return;
                IntPtr root = GetAncestor(hwnd, 2);
                if (root != fg) return;
            }

            var now = DateTime.UtcNow;
            bool wake = false;
            lock (eventGate)
            {
                if (evt == 0x0003 && fg != eventHwnd)
                {
                    eventHwnd = fg;
                    lastUserHwnd = fg;
                    switchUtc = now;
                    deferSinceUtc = now;
                    contentDirty = true;
                    dueUtc = now.AddMilliseconds(SwitchSettleMs);
                    wake = true;
                }
                else
                {
                    // Focus, scroll, or title change inside the active window: debounce briefly, cap max deferral so it never starves
                    contentDirty = true;
                    if ((now - lastInteractNudgeUtc).TotalMilliseconds >= 160)
                    {
                        lastInteractNudgeUtc = now;
                        if (deferSinceUtc == DateTime.MinValue) deferSinceUtc = now;
                        var want = now.AddMilliseconds(InteractSettleMs);
                        var ceiling = deferSinceUtc.AddMilliseconds(MaxDeferMs);
                        dueUtc = want < ceiling ? want : ceiling;
                        wake = true;
                    }
                }
            }
            if (wake) nudge.Set();
        }

        // One look at the screen; returns false only when skipped while waiting for settle debounce

        public bool Paused { get { return DateTime.UtcNow < pausedUntilUtc; } }

        public void Pause(int minutes)
        {
            pausedUntilUtc = minutes <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddMinutes(minutes);
            Store.SetMeta("paused.until", IngatDb.Unix(pausedUntilUtc).ToString(CultureInfo.InvariantCulture));
            if (minutes > 0)
            {
                lock (gate) { latestSnap = null; CloseCurrent(DateTime.UtcNow); }
            }
            nudge.Set();
        }

        public DateTime PausedUntilUtc { get { return pausedUntilUtc; } }

        bool Capture(bool force)
        {
            var now = DateTime.UtcNow;
            bool interactDirty = false;
            lock (eventGate)
            {
                if (!force)
                {
                    if ((now - switchUtc).TotalMilliseconds < SwitchSettleMs - 30) return false;
                    if (dueUtc > now && (now - deferSinceUtc).TotalMilliseconds < MaxDeferMs) return false;
                }
                interactDirty = contentDirty;
                contentDirty = false;
                dueUtc = DateTime.MinValue;
                deferSinceUtc = DateTime.MinValue;
            }

            if (Paused) { State = "paused"; lock (gate) latestSnap = null; return true; }
            double idle = IdleSeconds();
            if (idle >= AwaySeconds) { State = "away"; lock (gate) { latestSnap = null; CloseCurrent(lastSeenUtc); } return true; }

            int pid;
            string exe;
            // When the user opens Pet/Suruh to ask about their screen, Deskmate owns focus: read the target window right behind it
            IntPtr hwnd = ResolveTargetWindow(force || interactDirty, out pid, out exe);
            if (hwnd == IntPtr.Zero) return true;
            string title = Title(hwnd);
            if (title.Length == 0 || exe == "explorer" && (title == "Program Manager" || title == "Windows Input Experience")) return true;
            lastUserHwnd = hwnd;

            var blockedApps = List("ingat.block.apps");
            var blockedSites = List("ingat.block.sites");
            if (IngatPrivacy.BlockedApp(exe, blockedApps) || IngatPrivacy.PrivateWindow(title, "", blockedSites) || IngatText.ChromiumPrivate(hwnd, title, exe))
            {
                State = "private";
                lock (gate) { latestSnap = null; CloseCurrent(now); }
                return true;
            }
            State = "on";

            bool newWindow = hwnd != currentHwnd;
            string cleanTitle = IngatPrivacy.Redact(title);
            bool titleChanged = current == null || current.Title != cleanTitle;
            int rereadMs = idle < 15 ? ActiveRereadMs : IdleRereadMs;
            // Input, a switch, or a caller asking for fresh context: back to the normal pace
            if (newWindow || interactDirty || force || idle < HandsOffSeconds) churn = 0;
            int lookMs = LookMs(idle);
            bool due = (now - lastReadUtc).TotalMilliseconds >= rereadMs;
            bool look = settings.GetBool("ingat.text", true) && (interactDirty || (now - lastLookUtc).TotalMilliseconds >= lookMs - 150);

            string text = "", url = "", source = "window";
            IngatSnapshot snap = null;
            Bitmap shot = null;
            try
            {
                bool wantText = settings.GetBool("ingat.text", true);
                bool read = wantText && (newWindow || titleChanged || due || force);
                // Scroll/focus events read UIA directly: the dHash gate can miss changes in dark or uniform windows
                if (!read && wantText && interactDirty) read = true;
                else if (!read && look && !Fullscreen(hwnd))
                {
                    lastLookUtc = now;
                    // Foreground window: fingerprint straight off the screen; full PrintWindow only when it is behind something
                    var st = IngatText.ScreenStamp(hwnd);
                    if (!st.Valid)
                    {
                        shot = IngatText.Snap(hwnd);
                        if (shot != null) st = IngatText.Stamp(shot);
                    }
                    if (st.Valid)
                    {
                        // First look after a read without a screenshot only sets the baseline
                        if (stampStale) { lastStamp = st; stampStale = false; }
                        else if (IngatText.Changed(lastStamp, st)) read = true;
                        // Changed again with nobody touching anything: the window moves on its own, look later next time.
                        // A still look means it settled.
                        if (read && churn < 8) churn++;
                        else if (!read) churn = 0;
                    }
                }

                string guess = settings.GetBool("ingat.chat", true) ? IngatChat.Kind(exe, current != null && !newWindow ? current.Url : "", title) : "";
                if (read && guess == "chat")
                {
                    snap = IngatText.ReadSnapshot(hwnd, exe, title, true);
                    url = snap.Url;
                    source = "chat";
                    if (snap.Secret)
                    {
                        text = "";
                        source = "window";
                    }
                    else
                    {
                        text = snap.FormatText(IngatText.ChatMaxChars);
                        if (text.Length < 200 && settings.GetBool("ingat.ocr", true) && !LowPower())
                        {
                            if (shot == null) shot = IngatText.Snap(hwnd);
                            if (shot != null)
                            {
                                string seen = IngatText.OcrPlaced(shot);
                                if (seen.Length > text.Length)
                                {
                                    snap.MainLines.Clear();
                                    IngatStructure.FromPlacedOcr(seen, snap, true);
                                    text = snap.FormatText(IngatText.ChatMaxChars);
                                }
                            }
                        }
                    }
                    Rebase(hwnd, shot);
                    lastReadUtc = lastLookUtc = now;
                    Interlocked.Exchange(ref lastReadTicks, DateTime.UtcNow.Ticks);
                    read = false;
                }
                if (read)
                {
                    snap = IngatText.ReadSnapshot(hwnd, exe, title, false);
                    url = snap.Url;
                    source = "uia";
                    if (snap.Secret)
                    {
                        text = "";
                        source = "window";
                    }
                    else
                    {
                        text = snap.FormatText(IngatText.MaxChars);
                        int mainChars = snap.MainTextLength();
                        // Trigger viewport OCR when MainLines are empty/sparse even if sidebar/toolbar UIA had >80 chars
                        if (snap.MainLines.Count == 0 || mainChars < 60)
                        {
                            if (settings.GetBool("ingat.ocr", true) && !LowPower() && !Fullscreen(hwnd))
                            {
                                if (shot == null) shot = IngatText.Snap(hwnd);
                                if (shot != null)
                                {
                                    bool hasSidebar = snap.NavLines.Count >= 3;
                                    string seen = IngatText.OcrViewport(shot, hasSidebar);
                                    if (seen.Length > 0)
                                    {
                                        var ocrSnap = new IngatSnapshot { Title = snap.Title, Url = snap.Url };
                                        IngatStructure.FromPlacedOcr(seen, ocrSnap, false);
                                        if (ocrSnap.MainTextLength() > mainChars)
                                        {
                                            snap.Source = snap.MainLines.Count > 0 ? "uia+ocr" : "ocr";
                                            snap.MainLines.Clear();
                                            snap.MainLines.AddRange(ocrSnap.MainLines);
                                            for (int i = 0; i < ocrSnap.Headings.Count; i++) IngatContext.AddUnique(snap.Headings, ocrSnap.Headings[i], 8);
                                            for (int i = 0; i < ocrSnap.Anchors.Count; i++) IngatContext.AddUnique(snap.Anchors, ocrSnap.Anchors[i], 12);
                                            text = snap.FormatText(IngatText.MaxChars);
                                            source = "ocr";
                                        }
                                    }
                                }
                            }
                        }
                    }
                    Rebase(hwnd, shot);
                    lastReadUtc = lastLookUtc = now;
                    Interlocked.Exchange(ref lastReadTicks, DateTime.UtcNow.Ticks);
                }
            }
            finally { if (shot != null) shot.Dispose(); }

            url = IngatPrivacy.CleanUrl(url);
            if (url.Length == 0 && current != null && !newWindow) url = current.Url;
            if (url.Length > 0 && IngatPrivacy.PrivateWindow(title, url, blockedSites))
            {
                State = "private";
                lock (gate) { latestSnap = null; CloseCurrent(now); }
                return true;
            }
            text = IngatPrivacy.Redact(text);

            string app = AppName(exe);
            string project = IngatPrivacy.Project(exe, title);
            string host = IngatPrivacy.Host(url);
            // Chats and mail: each conversation is its own episode when the title or chat header names it
            string channel = settings.GetBool("ingat.chat", true) ? IngatChat.Kind(exe, url, title) : "";
            string conversation = channel == "chat" ? IngatChat.Conversation(title) : "";
            if (channel == "chat" && conversation.Length == 0)
            {
                if (snap != null && !snap.Secret && snap.Focus.Length >= 2) conversation = snap.Focus;
                else if (!newWindow && lastConversation.Length > 0) conversation = lastConversation;
            }
            if (channel != "chat") lastConversation = "";
            else if (conversation.Length > 0) lastConversation = conversation;

            if (channel.Length > 0 && source != "window") source = "chat";
            string kind = IngatStructure.Classify(exe, url, title, text);

            if (snap != null && !snap.Secret)
            {
                snap.App = app;
                snap.Project = project;
                snap.Title = cleanTitle;
                if (url.Length > 0) snap.Url = url;
                if (snap.Kind.Length == 0 || snap.Kind == "other") snap.Kind = kind;
                if (snap.Companion.Length == 0 && !Fullscreen(hwnd))
                    snap.Companion = FindCompanion(hwnd, blockedApps, blockedSites);
                snap.RedactAll();
            }

            string key = exe + "|" + (project.Length > 0 ? project : conversation.Length > 0 ? "c:" + IngatChat.Norm(conversation) : host.Length > 0 ? host : "");

            // What the user is writing: reuse the focused element from ReadSnapshot when available,
            // and skip hands-off UIA focus polling when idle with no active draft
            string draft = null, field = "";
            if (settings.GetBool("ingat.typed", true))
            {
                if (snap != null && snap.HasDraftCheck)
                {
                    if (!snap.Secret)
                    {
                        draft = snap.Draft;
                        field = snap.DraftField;
                    }
                }
                else if (idle < 6 || Composing || lastDraft.Length > 0 || interactDirty || newWindow || force)
                {
                    bool secretField;
                    draft = IngatChat.Draft(hwnd, out secretField, out field);
                    if (secretField) draft = null;
                }
                if (draft != null) draft = IngatPrivacy.Redact(draft);
            }

            lock (gate)
            {
                bool join = current != null && key == currentKey && (now - lastSeenUtc).TotalSeconds <= JoinSeconds;
                if (!join)
                {
                    CloseCurrent(lastSeenUtc > DateTime.MinValue && current != null ? lastSeenUtc : now);
                    long id = Store.Open(now, app, exe, project, cleanTitle, url, kind, channel);
                    current = Store.GetLite(id);
                    currentKey = key;
                }
                // Most looks see nothing new: same episode, title and URL, no text read. Only the end time moves then;
                // the full Touch (body merge, context re-encode, row rewrite) would write back what is already there.
                bool unchanged = join && !newWindow && !titleChanged && text.Length == 0 && snap == null && url == current.Url;
                if (unchanged) Store.TouchEnd(current.Id, now);
                else
                {
                    bool addEv = text.Length > 0 || titleChanged || newWindow;
                    current = Store.RecordCapture(current.Id, now, cleanTitle, url, text, snap != null && !snap.Secret ? snap : null, addEv, source, app) ?? current;
                }
                bool drafted = TrackDraft(now, hwnd, field, draft, app, channel, conversation, cleanTitle, url);
                if (drafted) current = Store.GetLite(current.Id) ?? current;
                current.EndUtc = now;
                if (snap != null)
                {
                    if (snap.Secret) latestSnap = null;
                    else if (!newWindow && !titleChanged && snap.MainLines.Count == 0 && snap.Focus.Length == 0 && latestSnap != null && latestSnap.MainLines.Count > 0)
                    {
                        // Transient empty UIA read on the same window/title: keep prior rich screen content and retry next look
                        latestSnap.CapturedUtc = now;
                        if (url.Length > 0) latestSnap.Url = url;
                        stampStale = true;
                    }
                    else latestSnap = snap;
                }
                else if (latestSnap != null && !newWindow)
                {
                    latestSnap.Title = cleanTitle;
                    if (url.Length > 0) latestSnap.Url = url;
                    latestSnap.CapturedUtc = now;
                }
                else if (newWindow)
                {
                    latestSnap = new IngatSnapshot
                    {
                        Source = "window", App = app, Exe = exe, Project = project, Title = cleanTitle, Url = url, Kind = kind, CapturedUtc = now
                    };
                }
                currentHwnd = hwnd;
                lastSeenUtc = now;
            }
            return true;
        }

        // Drafts: kept when they settle, and remembered as sent when the field empties in the same window
        string lastDraft = "";
        IntPtr lastDraftHwnd;
        string lastDraftField = "", lastDraftTitle = "";
        string lastDraftSaved = "";
        public volatile bool Composing;
        Dictionary<string, object> composing;
        DateTime composingSinceUtc;

        // True when the episode row itself changed (a sent message went into its body)
        bool TrackDraft(DateTime now, IntPtr hwnd, string field, string draft, string app, string channel, string conversation, string title, string url)
        {
            bool touched = false;
            string d = draft ?? "";
            // Same window, same box, same tab or chat: anything else emptying is a switch, not a send
            bool sameField = hwnd == lastDraftHwnd && field.Length > 0 && field == lastDraftField && title == lastDraftTitle;
            if (sameField && lastDraft.Length >= 2 && d.Length == 0 && draft != null)
            {
                // The text left the box: in a chat that means it was sent
                Store.AddEvent(now, current.Id, channel.Length > 0 ? "sent" : "typed", app, title, url, lastDraft);
                Store.Touch(current.Id, now, "", "", (channel.Length > 0 ? "Aku menulis: " : "") + lastDraft);
                touched = true;
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
            return touched;
        }

        // Stamp baseline after a read: off the screen when the window is in front, else from the shot already taken
        void Rebase(IntPtr hwnd, Bitmap shot)
        {
            var st = IngatText.ScreenStamp(hwnd);
            if (!st.Valid && shot != null) st = IngatText.Stamp(shot);
            if (st.Valid) { lastStamp = st; stampStale = false; }
            else stampStale = true;
        }

        public Dictionary<string, object> ComposingNow()
        {
            var c = composing;
            return c != null ? new Dictionary<string, object>(c) : new Dictionary<string, object>();
        }

        // Heads-up for the message being written now
        public Dictionary<string, object> BriefNow()
        {
            EnsureFresh(650);
            var c = composing;
            if (c == null) return new Dictionary<string, object> { { "brief", "" } };
            string app = Json.Str(c, "app"), conv = Json.Str(c, "conversation");
            string brief = Brain.Brief(app, conv, ChatScreen(), lastDraft);
            return new Dictionary<string, object> { { "brief", brief }, { "app", app }, { "conversation", conv } };
        }

        // What the open chat shows right now, for the writing brief
        public string ChatScreen()
        {
            long id;
            lock (gate) { if (current == null) return ""; id = current.Id; }
            return Store.LastScreenEvent(id);
        }

        // Forget a stretch: the live episode and screen go too when they fall inside it, or capture would keep
        // answering from the forgotten screen and writing into an episode that no longer exists
        public int Forget(DateTime fromUtc, DateTime toUtc)
        {
            lock (gate)
            {
                int n = Store.Forget(fromUtc, toUtc);
                bool liveHit = current != null && current.EndUtc >= fromUtc && current.StartUtc <= toUtc;
                bool snapHit = latestSnap != null && latestSnap.CapturedUtc >= fromUtc && latestSnap.CapturedUtc <= toUtc;
                if (liveHit || snapHit) DropLive();
                return n;
            }
        }

        public void ForgetAll()
        {
            lock (gate)
            {
                Store.ForgetAll();
                DropLive();
            }
        }

        // Under gate: let go of the open episode, the screen, and the draft without writing anything back
        void DropLive()
        {
            current = null;
            currentKey = "";
            currentHwnd = IntPtr.Zero;
            latestSnap = null;
            lastConversation = "";
            lastDraft = lastDraftSaved = lastDraftField = lastDraftTitle = "";
            lastDraftHwnd = IntPtr.Zero;
            Composing = false;
            composing = null;
        }

        void CloseCurrent(DateTime at)
        {
            if (current == null) return;
            try
            {
                Store.Close(current.Id, at > current.StartUtc ? at : current.EndUtc);
                Store.DropTiny(current.Id);
                workNudge.Set();
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
                current = Store.RecordCapture(current.Id, DateTime.UtcNow, "", "", text, null, true, source, current.App) ?? current;
            }
        }

        // Cheap live peek (no timeline query) for the status row: app, title and focus of the screen being read
        public Dictionary<string, object> LiveFocus()
        {
            var d = new Dictionary<string, object>();
            IngatEpisode ep;
            IngatSnapshot snap;
            lock (gate) { ep = current; snap = latestSnap; }
            if (State != "on") return d;
            if (snap != null)
            {
                var j = snap.ToJson();
                d["app"] = Json.Str(j, "app");
                d["title"] = Json.Str(j, "title");
                d["focus"] = Json.Str(j, "focus");
            }
            if (ep != null)
            {
                if (!d.ContainsKey("app") || Json.Str(d, "app").Length == 0) d["app"] = ep.App;
                if (!d.ContainsKey("title") || Json.Str(d, "title").Length == 0) d["title"] = ep.Title;
            }
            return d;
        }

        // What the user is on right now, its structured screen context, and how long they have kept at it
        public Dictionary<string, object> Now()
        {
            var d = new Dictionary<string, object>
            {
                { "state", State },
                { "idle", (int)IdleSeconds() },
            };
            if (Paused) d["pausedUntil"] = PausedUntilUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            IngatEpisode ep;
            IngatSnapshot snap;
            lock (gate) { ep = current; snap = latestSnap; }
            if (snap != null && State == "on") d["screen"] = snap.ToJson();
            if (ep == null) return d;
            d["episode"] = ep.ToJson(false);
            var c = composing;
            if (c != null) d["composing"] = c;
            // Streak: same project (or app) with no break over 10 minutes
            d["streakMinutes"] = Store.StreakMinutes(ep.Project, ep.Exe, DateTime.UtcNow);
            return d;
        }

        // Full structured context for live assistants: screen regions, active episode trail, recent moments, and today's breakdown
        public Dictionary<string, object> ContextNow(int recentLimit)
        {
            EnsureFresh(800);
            var d = Now();
            var recent = Store.RecentLite(Math.Max(1, Math.Min(20, recentLimit)));
            var recJson = new object[recent.Count];
            for (int i = 0; i < recent.Count; i++) recJson[i] = recent[i].ToJson(false);
            d["recent"] = recJson;

            var today = DateTime.Today;
            var todayEps = Store.TimelineLite(today.ToUniversalTime(), DateTime.UtcNow.AddMinutes(1), 400);
            d["today"] = IngatStructure.GroupTimeline(todayEps);
            d["inboxWaiting"] = Store.Inbox(10).Count;
            d["openPromises"] = Store.Commitments(0, false, 20).Count;
            return d;
        }

        // Background work: summaries, embeddings, days, meetings, retention — woken as soon as an episode closes

        void WorkLoop()
        {
            DateTime retained = DateTime.MinValue, meetings = DateTime.MinValue;
            while (!stopping)
            {
                workNudge.WaitOne(5000);
                if (stopping) break;
                try
                {
                    if ((DateTime.UtcNow - meetings).TotalSeconds >= 45) { meetings = DateTime.UtcNow; ScanMeetings(); }
                    if ((DateTime.UtcNow - retained).TotalHours >= 6)
                    {
                        retained = DateTime.UtcNow;
                        Store.Retain(settings.GetInt("ingat.keep.events", 14), settings.GetInt("ingat.keep.days", 180));
                    }
                    if (LowPower()) continue;
                    double idle = IdleSeconds();
                    if (idle < 10 && !settings.GetBool("ingat.busywork", true)) continue;
                    // Model calls wait for a pause: with a CLI provider each one is a new process of its own
                    for (int round = 0; round < 5 && !stopping && Brain.Work(idle >= AiIdleSeconds); round++) { }
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
                if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds < 20) continue;
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
                workNudge.Set();
            }
        }

        // Helpers

        readonly Dictionary<string, KeyValuePair<string, HashSet<string>>> listCache = new Dictionary<string, KeyValuePair<string, HashSet<string>>>(StringComparer.Ordinal);

        HashSet<string> List(string key)
        {
            string raw = settings.Get(key, "");
            KeyValuePair<string, HashSet<string>> cached;
            if (listCache.TryGetValue(key, out cached) && cached.Key == raw) return cached.Value;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in raw.Split(',', ';', '\n'))
                if (s.Trim().Length > 0) set.Add(s.Trim());
            listCache[key] = new KeyValuePair<string, HashSet<string>>(raw, set);
            return set;
        }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [StructLayout(LayoutKind.Sequential)] struct WRECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WRECT r);
        [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr p, int flags, StringBuilder s, ref int size);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        static readonly string SelfExe = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName).ToLowerInvariant();

        bool IsSelfProcess(int pid, string exe)
        {
            return pid == selfPid || (parentPid > 0 && pid == parentPid) || exe == SelfExe;
        }

        bool IsUsableUserWindow(IntPtr h, out int pid, out string exe)
        {
            pid = 0;
            exe = "";
            if (h == IntPtr.Zero || !IsWindow(h) || !IsWindowVisible(h) || IsIconic(h)) return false;
            int exStyle = GetWindowLong(h, -20);
            if ((exStyle & 0x00000080) != 0) return false; // WS_EX_TOOLWINDOW
            int cloaked;
            if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            WRECT r;
            if (!GetWindowRect(h, out r) || (r.R - r.L) < 200 || (r.B - r.T) < 120) return false;
            string t = Title(h);
            if (t.Length == 0) return false;
            IntPtr probe = h;
            exe = Exe(ref probe, out pid);
            if (exe.Length == 0 || IsSelfProcess(pid, exe)) return false;
            if (exe == "explorer" && (t == "Program Manager" || t == "Windows Input Experience")) return false;
            return true;
        }

        // Fast Win32 Z-order peek (<0.5ms, zero UIA): detects a visibly side-by-side split-screen companion window
        string FindCompanion(IntPtr activeHwnd, HashSet<string> blockedApps, HashSet<string> blockedSites)
        {
            WRECT ar;
            if (!GetWindowRect(activeHwnd, out ar)) return "";
            var activeRect = Rectangle.FromLTRB(ar.L, ar.T, ar.R, ar.B);
            IntPtr cur = GetTopWindow(IntPtr.Zero);
            for (int n = 0; cur != IntPtr.Zero && n < 20; n++, cur = GetWindow(cur, 2))
            {
                if (cur == activeHwnd) continue;
                int cpid;
                string cexe;
                if (!IsUsableUserWindow(cur, out cpid, out cexe)) continue;
                WRECT cr;
                if (!GetWindowRect(cur, out cr)) continue;
                var candRect = Rectangle.FromLTRB(cr.L, cr.T, cr.R, cr.B);
                if (!IngatText.IsSideBySide(activeRect, candRect)) continue;
                string ct = Title(cur);
                if (IngatPrivacy.BlockedApp(cexe, blockedApps) || IngatPrivacy.PrivateWindow(ct, "", blockedSites) || IngatText.ChromiumPrivate(cur, ct, cexe))
                    continue;
                string capp = AppName(cexe);
                string cproj = IngatPrivacy.Project(cexe, ct);
                return capp + (cproj.Length > 0 ? " [" + cproj + "]" : "") + " · " + Fmt.OneLine(IngatPrivacy.Redact(ct), 90);
            }
            return "";
        }

        // Foreground window first; if Deskmate's own Pet/Suruh overlay holds focus during a query, inspect the active user window behind it
        IntPtr ResolveTargetWindow(bool allowBehindSelf, out int pid, out string exe)
        {
            pid = 0;
            exe = "";
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                IntPtr probe = fg;
                exe = Exe(ref probe, out pid);
                if (exe.Length > 0 && !IsSelfProcess(pid, exe)) return fg;
            }
            if (!allowBehindSelf) return IntPtr.Zero;

            IntPtr recent;
            lock (eventGate) recent = eventHwnd;
            foreach (var cand in new[] { recent, lastUserHwnd, currentHwnd })
                if (cand != IntPtr.Zero && IsUsableUserWindow(cand, out pid, out exe)) return cand;

            IntPtr cur = GetTopWindow(IntPtr.Zero);
            for (int n = 0; cur != IntPtr.Zero && n < 60; n++, cur = GetWindow(cur, 2))
                if (IsUsableUserWindow(cur, out pid, out exe)) return cur;
            return IntPtr.Zero;
        }

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
