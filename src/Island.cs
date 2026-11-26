using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace Deskmate
{
    enum View { Idle, Media, Lyric, Track, Agent, Approve, Done, Notif, Volume, Power, Network, Privacy, Info, Task, Deadline, Timer, Prompter, Class, Download, Brightness, AudioDevice, ShelfDrop, Pray, Remind, Locks, Shot, Mic, Drive, Guard, Run, Meet, Mirror, Convert, Otp, Dictate, Record, Combo, Mini, Panel }

    class Toast
    {
        public View View;
        public int Priority;
        public bool Interactive;
        public DateTime ShownUtc;
        public string Title = "", Detail = "";
        public char Glyph;
        public bool Emblem;
        public object Data;
        public bool Flag;
        // Reminder holds the island until closed
        public bool Hold;
        public double Seconds;
        // Tab its history lives in; -1 picks by view
        public int Page = -1;
    }

    class Hit
    {
        public RectangleF Rect;
        public string Id;
        public Action Act;
        // Sliders: pointer x as 0..1
        public Action<float> Drag;
        // Right-click menu for this item
        public Action Menu;
        // Drag released
        public Action DragEnd;
    }

    partial class Island : Form
    {
        const float Gap = 9f, Pad = 34f, MaxH = 380f, BubbleR = 17f, BubbleGap = 10f, BaseHalf = 330f;
        // Wide teleprompter widens the canvas
        float prompterHalf = BaseHalf, canvasHalf = BaseHalf;
        float WantHalf { get { return PrompterOn ? Math.Max(BaseHalf, prompterHalf) : BaseHalf; } }
        const int AppBarMsg = Native.WM_APP + 0x31;
        const int MotionFrameMsg = Native.WM_APP + 0x33;
        DockPacer motionPacer;
        static readonly Guid PowerSourceGuid = new Guid("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
        static readonly Guid BatteryGuid = new Guid("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");

        internal readonly Settings settings = new Settings();
        readonly Painter p = new Painter();
        readonly Stopwatch clock = Stopwatch.StartNew();

        // Springs: shell size, radius, extras
        readonly Spring sw = new Spring(120, 300, 26, 0.04f), sh = new Spring(34, 240, 23, 0.04f), sr = new Spring(17, 300, 26, 0.04f);
        readonly Spring bubble = new Spring(0, 170, 15, 0.002f), opacity = new Spring(0, 140, 24, 0.002f);
        readonly Spring topGap = new Spring(Gap, 380, 24, 0.02f);
        float flipLinear, flip;
        View current = View.Idle, previous = View.Idle;
        DateTime switchedUtc = DateTime.UtcNow;
        double lastT;
        bool animating;

        // Window and canvas
        float scale = 1f;
        Rectangle monitorPx;
        IntPtr memDC, dib, oldBitmap;
        Bitmap canvas;
        Graphics g;
        int canvasW, canvasH;
        // Compact canvas until a tall view
        bool canvasTall;
        const float CompactH = 100;
        bool windowShown = true, fullscreen;
        IntPtr winHook, switchHook, powerSrc, powerPct;
        Native.WinEventProc winProc;
        RectangleF shell, bubbleRect;
        float originX, originY;
        bool hitsOn;

        // Interaction
        readonly List<Hit> hits = new List<Hit>();
        bool expanded, pointerInside, compactHoverExitPending;
        int page;
        PointF mouse = new PointF(-100, -100);
        string hoverId, pressedId;
        View pressedBubble;
        RectangleF pressedCompactBounds;
        readonly WinTimer clockTimer = new WinTimer { Interval = 1000 }, toastTimer = new WinTimer();
        readonly WinTimer hoverTimer = new WinTimer { Interval = 330 }, leaveTimer = new WinTimer { Interval = 420 }, fsTimer = new WinTimer { Interval = 350 };
        readonly WinTimer hoverWatch = new WinTimer { Interval = 50 };
        bool openedByHover;
        Point? messagesCardClick;
        Toast toast;
        NotifyIcon tray;
        ContextMenuStrip menu;
        ContextMenuStrip islandMenu;
        Icon trayIcon;
        readonly Roller clockRoller = new Roller();

        public Island()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Deskmate";
            Bounds = new Rectangle(-32000, -32000, 1, 1);

            Boot.Mark("form");
            Ink.Init();
            Boot.Mark("ink");
            Anim.Read(settings);
            Fmt.H24 = settings.GetBool("clock.24", true);
            Ink.Apply(ThemeLight());
            string savedAccent = settings.Get("accent", null), accentHex = savedAccent == null ? Ink.DefaultAccent : Ink.UpgradeAccent(savedAccent);
            if (savedAccent != null && accentHex != savedAccent) { settings.Set("accent", accentHex); settings.Save(); }
            Ink.SetAccent(accentHex);
            clockTimer.Tick += delegate { ClockTick(); };
            taskbarPeekTimer.Tick += delegate { EndTaskbarPeek(); };
            toastTimer.Tick += delegate
            {
                toastTimer.Stop();
                if (IsDisposed || Disposing) return;
                // Interactive hover buys one short grace, but cannot pin a toast indefinitely.
                if (toast != null && toast.Interactive && pointerInside && !ToastExpired())
                {
                    double remaining = Math.Max(0.05, toast.Seconds + 1.5 - (DateTime.UtcNow - toast.ShownUtc).TotalSeconds);
                    toastTimer.Interval = (int)(remaining * 1000);
                    toastTimer.Start();
                    return;
                }
                DismissToast();
            };
            hoverTimer.Tick += delegate
            {
                hoverTimer.Stop();
                if (!CanStartHover()) return;
                openedByHover = true;
                Expand(ToastPage(true));
            };
            hoverWatch.Tick += delegate { WatchHover(); };
            leaveTimer.Tick += delegate
            {
                leaveTimer.Stop();
                if (IsDisposed || Disposing) return;
                if (messagesCardClick.HasValue || islandMenu != null || WifiPassOpen()) return;
                if (!pointerInside && expanded)
                {
                    // The panel can grow underneath a stationary pointer after the bubble disappears.
                    var pt = CursorInIsland();
                    if (openedByHover && OverHoverTarget(pt)) return;
                    pointerInside = InShape(pt);
                    if (pointerInside) { Kick(); return; }
                    expanded = false;
                    openedByHover = false;
                    MiniLeft();
                    StopControlScans();
                    Kick();
                }
            };
            fsTimer.Tick += delegate { fsTimer.Stop(); if (IsDisposed || Disposing) return; CheckFullscreen(); CheckMiniAuto(); };
            ambientTimer.Tick += delegate { AmbientTick(); };
            spriteTimer.Tick += delegate { if (IsDisposed || Disposing) { spriteTimer.Stop(); return; } SpriteTick(); };
            trimTimer.Tick += delegate { trimTimer.Stop(); if (IsDisposed || Disposing) return; TrimAtRest(); };
            dockLeave.Tick += delegate { DockLeaveTick(); };
            dockSwitchPoll.Tick += delegate { PollDockSwitch(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = Native.WS_POPUP;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            motionPacer = new DockPacer(Handle, MotionFrameMsg, 0);
            if (animating) ScheduleMotionFrame();
            UpdateMonitor();
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)), hWnd = Handle, uCallbackMessage = AppBarMsg };
            Native.SHAppBarMessage(0, ref abd);
            winProc = delegate(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
            {
                if (evt == 3)
                {
                    // A topmost app can cover the island before the fullscreen debounce runs.
                    if (windowShown && !fullscreen && !dormant && !hiddenByUser && hwnd != Handle &&
                        hwnd == Native.GetForegroundWindow() &&
                        (Native.GetWindowLong(hwnd, -20) & Native.WS_EX_TOPMOST) != 0 &&
                        !Native.IsFullscreenWindow(hwnd, monitorHandle) && !ExceptHidden()) RaiseIsland();
                    RefreshComboPause();
                }
                if (evt == DockSwitchStart || evt == DockSwitchEnd) UpdateDockSwitch(evt == DockSwitchStart, hwnd);
                fsTimer.Stop(); fsTimer.Start(); NudgeFrost(); NudgeDock();
            };
            winHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, winProc, 0, 0, 0);
            switchHook = Native.SetWinEventHook(DockSwitchStart, DockSwitchEnd, IntPtr.Zero, winProc, 0, 0, 0);
            // Windows moving behind the glass
            moveHook = Native.SetWinEventHook(0x0B, 0x0B, IntPtr.Zero, winProc, 0, 0, 0);
            minHook = Native.SetWinEventHook(0x16, 0x17, IntPtr.Zero, winProc, 0, 0, 0);
            Guid a = PowerSourceGuid, b = BatteryGuid, d = DisplayGuid;
            powerSrc = Native.RegisterPowerSettingNotification(Handle, ref a, 0);
            powerPct = Native.RegisterPowerSettingNotification(Handle, ref b, 0);
            powerDisplay = Native.RegisterPowerSettingNotification(Handle, ref d, 0);
            SystemEvents.DisplaySettingsChanged += OnDisplaySettings;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        IntPtr moveHook, minHook, powerDisplay;
        static readonly Guid DisplayGuid = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");
        bool locked, screenOff;

        // Locked or screen off: everything rests
        void SetDormant(bool isLocked, bool isOff)
        {
            locked = isLocked;
            screenOff = isOff;
            bool now = locked || screenOff;
            if (now == dormant) return;
            dormant = now;
            if (now) ResetCombo();
            else NudgeFrost();
            Kick();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) Post(delegate { SetDormant(true, screenOff); });
            else if (e.Reason == SessionSwitchReason.SessionUnlock) Post(delegate { SetDormant(false, screenOff); });
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Boot.Mark("onload");
            // First run: the installer's language until setup asks
            if (settings.Fresh && !settings.Has("lang")) settings.Set("lang", SetupLanguage());
            Lang.English = settings.Get("lang", "id") == "en";
            LoadPack();
            LoadLayout();
            BuildTray();
            ApplyClips();
            ApplyDownloads();
            StartHotkeys();
            MiniInit();
            StartGlass();
            ApplyNotifLists();
            StartFeeds();
            StartLocks();
            StartCombo();
            StartMic();
            StartControls();
            StartDictate();
            LoadOneShots();
            ApplyReserve();
            ApplyDock();
            Boot.Mark("dock");
            StartWallSync();
            // Left hidden by a crash: the taskbar comes back.
            if (dockWins.Count == 0) Taskbar.Restore(settings);
            // New installs: tour, not notes
            if (settings.Fresh) settings.Set("whatsnew.seen", AppVersion);
            SetupOnFirstRun();
            InitShelfDrop();
            LoadShelf();
            Boot.Mark("shelf");
            EnsureDisc(64);
            // Warm-up split over two ticks: one 61 ms stall became two small ones
            warmTimer = new WinTimer { Interval = 2500 };
            warmTabsTimer = new WinTimer { Interval = 1200 };
            warmTimer.Tick += delegate
            {
                warmTimer.Stop(); warmTimer.Dispose(); warmTimer = null;
                if (IsDisposed || Disposing) return;
                WarmUp();
                if (warmTabsTimer == null) return;
                warmTabsTimer.Tick += delegate
                {
                    warmTabsTimer.Stop(); warmTabsTimer.Dispose(); warmTabsTimer = null;
                    if (IsDisposed || Disposing) return;
                    WarmTabs(); TrimSoon();
                };
                warmTabsTimer.Start();
            };
            warmTimer.Start();
            ReadPower();
            SyncIslandReplicas();
            CheckFullscreen();
            clockRoller.Set(Fmt.Clock(DateTime.Now));
            int hour = DateTime.Now.Hour;
            ShowInfo('\0', Greeting(hour < 11 ? Lang.T("Pagi") : hour < 15 ? Lang.T("Siang") : hour < 18 ? Lang.T("Sore") : Lang.T("Malam")), Fmt.Hm(DateTime.Now), true);
            if (toast != null) toast.Emblem = emblem != null;
            if (toast != null && toast.Emblem) { PlayEmblem(animSeconds); Ripple(); }
            PlayGreetingVoice();
            opacity.Target = 1;
            clockTimer.Start();
            hoverWatch.Start();
            Kick();
            Boot.Mark("kick");
            // Disk and shell COM: nothing here is needed for the first frame
            DeferStart();
            PilotStart();
        }

        readonly WinTimer deferTimer = new WinTimer { Interval = 400 };
        WinTimer warmTimer, warmTabsTimer;
        int deferTries;

        // After the first frame: leftovers, menu entry, old captures
        void DeferStart()
        {
            deferTimer.Tick += delegate
            {
                deferTimer.Stop();
                if (IsDisposed || Disposing) return;
                // Kick only made the frame dirty; wait for a real paint before adding work
                if (!Boot.Logged && ++deferTries < 25) { deferTimer.Start(); return; }
                deferTimer.Dispose();
                var sw = Stopwatch.StartNew();
                CleanUpdate();
                ShellApp.EnsureStartEntry();
                CleanShots(false);
                AgentSetup.Refresh(settings);
                CheckLinks();
                LogQuota("codex");
                LogQuota("copilot");
                // Separate line: this ran after the boot timeline had already closed
                Log.Write("defer " + sw.Elapsed.TotalMilliseconds.ToString("0") + "ms");
            };
            deferTimer.Start();
        }

        void StopTimers()
        {
            StopMotionPacer();
            foreach (var t in new[] { clockTimer, toastTimer, hoverTimer, hoverWatch, leaveTimer, fsTimer, ambientTimer, deferTimer, warmTimer, warmTabsTimer, trimTimer, dockLeave, pinSync, downloadTimer, clipRetry, holdTimer, dictCap, miniTimer, tabHover, spriteTimer, wallDebounce, locksTimer, screenTextWait, shotFind })
                if (t != null) { t.Stop(); t.Dispose(); }
            warmTimer = warmTabsTimer = null;
            if (googleWorkSyncTimer != null) { googleWorkSyncTimer.Dispose(); googleWorkSyncTimer = null; }
            if (googleWorkPeriodicTimer != null) { googleWorkPeriodicTimer.Dispose(); googleWorkPeriodicTimer = null; }
            if (tidyLater != null) { tidyLater.Dispose(); tidyLater = null; }
        }

        // Test builds attach a pilot here
        partial void PilotStart();
        partial void PilotFrame();

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (islandMenu != null) islandMenu.Close();
            DisposeIslandReplicas();
            StopTimers();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettings;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailability;
            StopWallSync();
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)), hWnd = Handle };
            Native.SHAppBarMessage(1, ref abd);
            foreach (var hook in new[] { winHook, switchHook, moveHook, minHook })
                if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
            foreach (var reg in new[] { powerSrc, powerPct, powerDisplay })
                if (reg != IntPtr.Zero) Native.UnregisterPowerSettingNotification(reg);
            ReleaseApprovals();
            ClosePet();
            CloseDocks();
            CleanShots(true);
            CloseMirror();
            CloseConvert();
            StopDictate();
            StopComboHook();
            StopComboAudio();
            FlushComboStats();
            if (RapatOn) AbortRapat();
            FlushFocusStats();
            if (islandBackdrop != null) { islandBackdrop.Dispose(); islandBackdrop = null; }
            DisposeControls();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeIslandReplicas();
                StopTimers();
                ClearClips(clips);
                ClearClips(imageClips);
                FreeCanvas();
                DisposeControls();
                if (p != null) { p.G = null; p.Dispose(); }
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case MotionFrameMsg:
                    MotionFrame();
                    return;
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)Native.MA_NOACTIVATE;
                    return;
                case Native.WM_POWERBROADCAST:
                    // Display: 0 off, 1 on, 2 dimmed
                    if (m.WParam.ToInt64() == 0x8013 && m.LParam != IntPtr.Zero)
                    {
                        var setting = (Guid)Marshal.PtrToStructure(m.LParam, typeof(Guid));
                        if (setting == DisplayGuid) { SetDormant(locked, Marshal.ReadInt32(m.LParam, 20) == 0); m.Result = (IntPtr)1; return; }
                    }
                    ReadPower();
                    m.Result = (IntPtr)1;
                    return;
                case AppBarMsg:
                    if (m.WParam.ToInt64() == 2) { fsTimer.Stop(); fsTimer.Start(); }
                    // ABN_POSCHANGED: claim the strip again
                    else if (m.WParam.ToInt64() == 1 && reservedPx > 0) { reservedPx = -1; ApplyReserve(); }
                    return;
                case 0x0219: // WM_DEVICECHANGE
                    OnDeviceChange(m.WParam, m.LParam);
                    break;
                case 0x031D: // WM_CLIPBOARDUPDATE
                    OnClipboardUpdate();
                    break;
                case 0x0312: // WM_HOTKEY
                    if (GiveBackAltGr(m.LParam)) return;
                    if (m.WParam.ToInt32() == HideHotkey) ToggleHidden();
                    else if (m.WParam.ToInt32() == PrompterHotkey) TogglePrompter();
                    else if (m.WParam.ToInt32() == PrompterFaster) NudgeSpeed(1);
                    else if (m.WParam.ToInt32() == PrompterSlower) NudgeSpeed(-1);
                    else if (m.WParam.ToInt32() == PrompterSetupKey) BeginInvoke(new Action(OpenPrompter));
                    else if (m.WParam.ToInt32() == PrompterRestart) RestartPrompter();
                    else if (m.WParam.ToInt32() == PrompterStop) StopPrompter();
                    else if (m.WParam.ToInt32() == PrompterBack) SeekPrompter(-5);
                    else if (m.WParam.ToInt32() == PrompterAhead) SeekPrompter(5);
                    else if (m.WParam.ToInt32() == KeysHotkey) EnterKeyboard();
                    else if (m.WParam.ToInt32() == MiniHotkey) ToggleMini();
                    else if (m.WParam.ToInt32() == MicHotkey) ToggleMic();
                    else if (m.WParam.ToInt32() == NoteHotkey) BeginInvoke(new Action(OpenNote));
                else if (m.WParam.ToInt32() == WorkHotkey) BeginInvoke(new Action(OpenWork));
                    else if (m.WParam.ToInt32() == MirrorHotkey) ToggleMirror();
                    else if (m.WParam.ToInt32() == PickerHotkey) BeginInvoke(new Action(OpenPicker));
                    else if (m.WParam.ToInt32() == ScreenTextHotkey) BeginInvoke(new Action(ReadScreenText));
                    else if (m.WParam.ToInt32() == DictPasteHotkey) BeginInvoke(new Action(PasteLastDictation));
                    else if (m.WParam.ToInt32() == RapatHotkey) ToggleRapat();
                    else if (m.WParam.ToInt32() == SuruhHotkey) BeginInvoke(new Action(OpenSuruh));
                    else if (m.WParam.ToInt32() == FixHotkey) BeginInvoke(new Action(delegate { RewriteSelection(false); }));
                    else if (m.WParam.ToInt32() == TranslateHotkey) BeginInvoke(new Action(delegate { RewriteSelection(true); }));
                    return;
                case 0x020E: // WM_MOUSEHWHEEL: sideways swipe changes tab
                    if (expanded) WheelTab((short)((m.WParam.ToInt64() >> 16) & 0xFFFF));
                    m.Result = IntPtr.Zero;
                    return;
                case 0x001A: // WM_SETTINGCHANGE: animation effects, light or dark
                    if (m.WParam.ToInt64() == 0x1043) { Anim.Read(settings); Kick(); }
                    else if (m.LParam != IntPtr.Zero && Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet") OnColorSetChanged();
                    break;
                case Native.WM_DPICHANGED:
                case Native.WM_DISPLAYCHANGE:
                    BeginInvoke(new Action(OnDisplayChanged));
                    if (m.Msg == Native.WM_DPICHANGED) return;
                    break;
            }
            base.WndProc(ref m);
        }

        void Post(Action action)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        void OnDisplaySettings(object sender, EventArgs e) { Post(OnDisplayChanged); }

        IntPtr monitorHandle;

        // A disconnected display falls back to the primary one, while its
        // canonical device name remains saved so it is picked again on reconnect.
        static bool IsDisplayDevice(string device)
        {
            const string prefix = @"\\.\DISPLAY";
            if (string.IsNullOrEmpty(device) || !device.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            for (int i = prefix.Length; i < device.Length; i++) if (device[i] < '0' || device[i] > '9') return false;
            return device.Length > prefix.Length;
        }

        Screen ChosenScreen()
        {
            Screen[] screens = Screen.AllScreens;
            Screen primary = screens.Length > 0 ? screens[0] : Screen.PrimaryScreen;
            string want = settings.Get("monitor", "").Trim();
            foreach (var s in screens)
            {
                if (s.Primary) primary = s;
                if (want.Length > 0 && string.Equals(s.DeviceName, want, StringComparison.OrdinalIgnoreCase)) return s;
            }
            if (want.Length > 0 && !IsDisplayDevice(want))
            {
                settings.Remove("monitor");
                settings.Save();
            }
            return primary;
        }

        static IntPtr MonitorForScreen(Screen screen)
        {
            if (screen != null)
                return Native.MonitorFromPoint(new Native.POINT(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2), 2);
            return Native.MonitorFromPoint(new Native.POINT(0, 0), 1);
        }

        void UseMonitor(string device)
        {
            string chosen = "";
            foreach (var s in Screen.AllScreens)
                if (string.Equals(s.DeviceName, device, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = s.Primary ? "" : s.DeviceName;
                    break;
                }
            activeIslandDisplay = null;
            settings.Set(AllIslandScreensKey, false);
            settings.Set("monitor", chosen);
            settings.Save();
            DisposeIslandReplicas();
            UpdateMonitor();
            ResetFrost();
            if (reservedPx > 0) ApplyReserve();
            ApplyDock();
            CheckFullscreen();
            Kick();
        }

        void OnDisplayChanged()
        {
            UpdateMonitor();
            SyncIslandReplicas();
            ResetFrost();
            if (reservedPx > 0) { reservedPx = -1; ApplyReserve(); }
            ApplyDock();
            if (prefs != null && !prefs.IsDisposed) prefs.RefreshDisplayPicker();
            CheckFullscreen();
            Kick();
        }

        void UpdateMonitor()
        {
            IntPtr mon = MonitorForScreen(ActiveIslandScreen());
            monitorHandle = mon;
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            Native.GetMonitorInfo(mon, ref mi);
            monitorPx = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            float s = MonitorScale(mon);
            if (canvas == null || Math.Abs(s - scale) > 0.001f)
            {
                scale = s;
                CreateCanvas();
            }
        }

        static float MonitorScale(IntPtr mon)
        {
            float s = 1f;
            try
            {
                uint dx, dy;
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) s = dx / 96f;
            }
            catch (Exception ex) { Log.Write("dpi: " + ex.Message); }
            float forced;
            if (float.TryParse(Environment.GetEnvironmentVariable("DESKMATE_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forced) && forced > 0.5f) s = forced;
            return s;
        }
    }
}
