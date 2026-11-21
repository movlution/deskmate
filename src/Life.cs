using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Windows.UI.Notifications.Management;

namespace Deskmate
{
    // Event sources feeding island state
    partial class Island
    {
        readonly MediaService media = new MediaService();
        readonly LyricsService lyricsSvc = new LyricsService();
        readonly VolumeWatcher volume = new VolumeWatcher();
        readonly Spectrum spectrum = new Spectrum();
        readonly NetWatcher net = new NetWatcher();
        readonly PrivacyWatcher privacy = new PrivacyWatcher();
        readonly NotifWatcher notifWatcher = new NotifWatcher();

        MediaInfo mediaInfo = MediaInfo.None;
        DateTime pausedSinceUtc = DateTime.MinValue;
        bool mediaSeen;
        string pendingTrack;
        Lyrics lyrics;
        int lyricIndex = -1, lyricPrev = -1;
        DateTime lyricSwitchUtc = DateTime.MinValue;

        float vol = 0.5f;
        bool muted, volKnown;
        readonly Spring volBar = new Spring(0.5f, 320, 30, 0.001f);
        readonly Roller volRoller = new Roller();
        bool hasBattery, onAc = true, powerKnown;
        int battery = 100;
        readonly Spring batteryFill = new Spring(0, 90, 12, 0.002f);
        string[] micApps = new string[0], camApps = new string[0];

        List<NotifItem> notifs = new List<NotifItem>();
        readonly HashSet<uint> seenNotifs = new HashSet<uint>();
        bool notifPrimed;
        int unread, notifBurst;
        UserNotificationListenerAccessStatus notifAccess = UserNotificationListenerAccessStatus.Allowed;

        AgentHub hub;
        readonly Dictionary<string, AgentSession> sessions = new Dictionary<string, AgentSession>();
        readonly List<Approval> approvals = new List<Approval>();

        KuliaClient kulia;
        List<KuliaTask> tasks;
        string tasksError;
        DateTime tasksAtUtc = DateTime.MinValue, nextTasksUtc = DateTime.MinValue;
        readonly HashSet<string> warnedTasks = new HashSet<string>();

        bool NotifOn { get { return settings.GetBool("notif", true); } }

        void StartFeeds()
        {
            Ai.Bind(settings);
            media.Changed += m => Post(() => OnMedia(m));
            lyricsSvc.Ready += l => Post(() => OnLyrics(l));
            volume.Changed += (level, mute, first) => Post(() => OnVolume(level, mute, first));
            volume.DeviceChanged += () => spectrum.Restart();
            volume.MicChanged += (m, present, first) => Post(() => OnMic(m, present, first));
            volume.AppsChanged += l => Post(() => OnApps(l));
            net.Changed += (a, b) => Post(() => OnNet(a, b));
            privacy.Changed += (m, c) => Post(() => OnPrivacy(m, c));
            notifWatcher.Updated += (access, list) => Post(() => OnNotifs(access, list));
            // Read failures used to live only in the log; say it once, then stay quiet
            notifWatcher.Failed += msg => Post(() => { ShowInfo(Ink.IcoBell, Lang.T("Pesan tidak terbaca"), Lang.T("Coba lagi nanti"), true); Kick(); });
            media.Start();
            volume.Start();
            net.Start();
            privacy.Start();
            notifWatcher.Enabled = NotifOn;
            notifWatcher.Start();
            StartAgents();
            // Default works; settings.ini may override
            kulia = new KuliaClient(settings.Has("kulia.url") ? settings.Get("kulia.url", "") : KuliaClient.DefaultUrl,
                                    settings.Has("kulia.key") ? settings.Get("kulia.key", "") : KuliaClient.DefaultKey);
            if (kulia.SignedIn) RefreshTasks(false);
            StartSources();
            ApplyTidy();
            StartUpdateCheck();
        }

        // Daily release check; never downloads anything
        public const string AppVersion = "2.9.0";
        string newVersion, newUrl;
        bool newRebuild, checking, checkFailed;
        DateTime checkedUtc;
        // Settings › About listens here
        event Action UpdateChanged;

        void StartUpdateCheck()
        {
            if (!settings.GetBool("update.check", true) || Settings.TestMode) return;
            var t = new System.Windows.Forms.Timer { Interval = 60000 };
            t.Tick += delegate { t.Interval = 24 * 3600 * 1000; CheckUpdate(false); };
            t.Start();
        }

        // Newer tag, or same tag rebuilt
        void CheckUpdate(bool manual)
        {
            if (checking || updating) return;
            checking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string tag = null, url = null, asset = null, digest = null;
                bool ok = false, rebuild = false;
                try
                {
                    var r = Web.Send("GET", "https://api.github.com/repos/movlution/deskmate/releases/latest", null, null, 10000);
                    if (r.Ok)
                    {
                        var o = Json.Parse(r.Body);
                        ok = true;
                        tag = Json.Str(o, "tag_name").TrimStart('v');
                        url = Json.Str(o, "html_url");
                        DateTime uploaded;
                        ReadAsset(o, out asset, out digest, out uploaded);
                        Version latest, mine;
                        if (!Version.TryParse(tag, out latest) || !Version.TryParse(AppVersion, out mine) || latest < mine || !url.StartsWith("https://github.com/movlution/deskmate/")) tag = null;
                        else if (latest == mine)
                        {
                            rebuild = true;
                            // Local build newer than asset: keep
                            if (digest == null || digest == OwnDigest() || uploaded <= System.IO.File.GetLastWriteTimeUtc(Application.ExecutablePath)) tag = null;
                        }
                    }
                }
                catch (Exception ex) { Log.Write("update: " + ex.Message); }
                Post(() =>
                {
                    checking = false;
                    checkFailed = !ok;
                    if (ok) checkedUtc = DateTime.UtcNow;
                    if (tag != null)
                    {
                        bool first = newVersion != tag || newDigest != digest;
                        newVersion = tag;
                        newUrl = url;
                        newAsset = asset;
                        newDigest = digest;
                        newRebuild = rebuild;
                        if (first && !manual) ShowInfo(Ink.IcoRefresh, rebuild ? Lang.T("Pembaruan Deskmate tersedia") : Lang.F("Deskmate {0} tersedia", tag), Lang.T("tray › Perbarui"), true);
                    }
                    else if (ok) newVersion = newUrl = newAsset = newDigest = null;
                    if (UpdateChanged != null) UpdateChanged();
                });
            });
            if (UpdateChanged != null) UpdateChanged();
        }

        // Status line for Settings › About
        string UpdateText()
        {
            if (updating) return Lang.T("Mengunduh pembaruan");
            if (checking) return Lang.T("Memeriksa GitHub");
            if (newVersion != null) return newRebuild ? Lang.F("Build baru Deskmate {0} tersedia", newVersion) : Lang.F("Deskmate {0} tersedia", newVersion);
            if (checkFailed) return Lang.T("GitHub tidak terjangkau");
            if (checkedUtc != DateTime.MinValue) return Lang.F("Sudah terbaru · dicek {0}", Fmt.Hm(checkedUtc.ToLocalTime()));
            return Lang.T("Dicocokkan lewat SHA-256 dengan rilis di GitHub");
        }

        // Every second; true if redraw needed
        bool LifeTick(bool minute)
        {
            var now = DateTime.UtcNow;
            bool redraw = false;
            TickApps();
            PruneRuns();
            TickWeather();
            TickBluetooth(false);
            TickMirror();
            TickConvert();
            if (phone != null) phone.Sweep();
            if (TickMeeting()) { Kick(); redraw = true; }
            TickShare(now);
            if (AwakeTick(now, minute)) redraw = true;
            if (minute && ShelfExpireTick()) redraw = true;
            if (mediaInfo.Present) SyncLyrics();
            List<string> stale = null;
            foreach (var s in sessions.Values)
            {
                if (s.State == AgentState.Working && (now - s.LastUtc).TotalMinutes > 15) { s.State = AgentState.Ready; redraw = true; }
                // Codex and Antigravity never send SessionEnd
                bool idle = AgentKind(s) != "Claude" && (s.State == AgentState.Ready || s.State == AgentState.Done) && (now - s.LastUtc).TotalMinutes > 30;
                if (idle || (s.State == AgentState.Ended && (now - s.LastUtc).TotalMinutes > 3) || (now - s.LastUtc).TotalHours > 6)
                {
                    if (stale == null) stale = new List<string>();
                    stale.Add(s.Id);
                }
            }
            if (stale != null) foreach (var id in stale) { sessions.Remove(id); redraw = true; }
            if (kulia != null && kulia.SignedIn && now >= nextTasksUtc) RefreshTasks(false);
            SyncSourcesTick(now);
            CheckDeadlines(now);
            CheckClasses(now);
            // Countdowns read in minutes
            var soon = SoonTask();
            if (soon != null && (minute || (current == View.Task && Fmt.Left(soon.DeadlineUtc - now) != taskRoller.Value))) redraw = true;
            return redraw;
        }

        // Media + lyrics

        bool MediaVisible()
        {
            return mediaInfo.Present && mediaInfo.Title.Length > 0 && (mediaInfo.Playing || (DateTime.UtcNow - pausedSinceUtc).TotalSeconds < 90);
        }

        bool VisualizerVisible()
        {
            if (!settings.GetBool("visualizer", true) || !mediaInfo.Playing || fullscreen || dormant || hiddenByUser) return false;
            return current == View.Media || current == View.Lyric || current == View.Track || (current == View.Panel && page == PageMusic);
        }

        void OnMedia(MediaInfo m)
        {
            var prev = mediaInfo;
            if (prev.Playing && !m.Playing) pausedSinceUtc = DateTime.UtcNow;
            bool shortClip = m.DurationMs > 0 && m.DurationMs < 31000;
            if (m.Present && m.Key != prev.Key && mediaSeen && m.Title.Length > 0 && !shortClip) pendingTrack = m.Key;
            mediaInfo = m;
            // Free old artwork now, not at finalization
            if (prev.Cover != null && !ReferenceEquals(prev.Cover, m.Cover)) prev.Cover.Dispose();
            if (m.Cover != null && !ReferenceEquals(prev.Cover, m.Cover)) PrewarmArt();
            AlbumAccent();
            if (m.Present) mediaSeen = true;
            SyncLyrics();
            if (pendingTrack != null && pendingTrack == m.Key && m.Playing)
            {
                pendingTrack = null;
                if (!expanded) ShowToast(View.Track, 3.2, 1, false);
            }
            Kick();
        }

        void SyncLyrics()
        {
            if (!mediaInfo.Present) return;
            // Privacy switch: no lyric lookups
            if (!settings.GetBool("lyrics.online", true))
            {
                if (lyrics == null || lyrics.Key != mediaInfo.Key) lyrics = new Lyrics { Key = mediaInfo.Key, State = LyricState.Missing };
                return;
            }
            if (lyrics != null && lyrics.Key == mediaInfo.Key && lyrics.State != LyricState.Failed) return;
            var l = lyricsSvc.Get(mediaInfo);
            if (l == null) return;
            if (lyrics == null || lyrics.Key != l.Key) { lyricIndex = lyricPrev = -1; lyricScroll.Snap(0); }
            ApplyLyricOffset(l);
            lyrics = l;
        }

        void OnLyrics(Lyrics l)
        {
            if (l.Key != mediaInfo.Key) return;
            ApplyLyricOffset(l);
            lyrics = l;
            lyricIndex = lyricPrev = -1;
            Kick();
        }

        bool LyricsReady { get { return lyrics != null && lyrics.Key == mediaInfo.Key && lyrics.State == LyricState.Synced; } }

        bool LyricOnIsland()
        {
            if (!settings.GetBool("lyrics", true) || !LyricsReady || !mediaInfo.Playing) return false;
            double pos = mediaInfo.Position(DateTime.UtcNow);
            int i = lyrics.IndexAt(pos);
            if (i < 0 || lyrics.Lines[i].Text.Length == 0) return false;
            // Instrumental break: back to the cover
            return !(pos > lyrics.SungUntil(i) + 2500 && lyrics.NextAt(i) - pos > 3000);
        }
    }
}
