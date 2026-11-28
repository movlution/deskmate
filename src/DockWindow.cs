using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Glass dock along the bottom edge; can stand in for the taskbar
        sealed partial class DockWindow : Form
        {
            readonly Island isle;
            Screen targetScreen;
            readonly string deviceName;
            readonly Painter p = new Painter();
            readonly AppWatch fallbackWatch = new AppWatch();
            AppWatch watch { get { return isle != null ? isle.dockWatch : fallbackWatch; } }
            List<DockApp> apps = new List<DockApp>();
            readonly Dictionary<string, Bitmap> fallbackIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Bitmap> icons { get { return isle != null ? isle.dockIcons : fallbackIcons; } }
            float scale = 1;
            IntPtr memDC, dib, oldBmp;
            Bitmap canvas;
            Graphics g;
            int cw, ch;
            // This dock's monitor and strip, in px.
            Rectangle mon, area;
            IntPtr monitorHandle;
            bool globalOwner, registered, shown = true, closing;
            const int AppBarMsg = Native.WM_APP + 0x32;
            static readonly int ShellMsg = Taskbar.RegisterWindowMessage("SHELLHOOK"), FrameMsg = Taskbar.RegisterWindowMessage("DeskmateDockFrame");
            readonly Timer tick = new Timer { Interval = 1000 }, dwell = new Timer { Interval = 420 }, hideLater = new Timer { Interval = 700 }, revealLater = new Timer();
            readonly Stopwatch clock = Stopwatch.StartNew();
            double lastT;
            DockPacer pacer;
            DockFrost frost;
            DockPlate plate;
            TrayHost tray;
            List<TrayIcon> trayIcons = new List<TrayIcon>();
            int trayPosted;
            DockCard card;
            IntPtr front;
            string drawnSig = "";
            int tickN;
            // Dock slides in on open and for auto-hide
            readonly Spring reveal = new Spring(0, 1, 1, 0.001f), hover = new Spring(0, 1, 1, 0.002f), workDot = new Spring(0, 1, 1, 0.003f), suruhDot = new Spring(0, 1, 1, 0.003f);
            // One shape, one motion: the whole bar rises and leaves on the same
            // calm, critically damped spring. Pointer effects wait for arrival.
            const float RevealK = 520f, RevealDamping = 1f;
            bool wantShown = true, focusReveal;
            // Pointer effects belong to the arrived shape, not to the moving one.
            bool RevealReady { get { return reveal.Value > 0.985f; } }

            [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr h);
            [DllImport("user32.dll")] static extern bool DeregisterShellHookWindow(IntPtr h);
            [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
            [DllImport("kernel32.dll")] static extern IntPtr VirtualQuery(IntPtr address, out MemInfo info, IntPtr size);

            [StructLayout(LayoutKind.Sequential)]
            struct MemInfo { public IntPtr Base, AllocBase; public uint AllocProtect; public IntPtr Size; public uint State, Protect, Type; }

            Settings S { get { return isle != null ? isle.settings : null; } }

            // Pilot runs: no taskbar or tray changes, drawn off screen
            public static readonly bool Dry = Environment.GetEnvironmentVariable("DESKMATE_DOCK_DRY") == "1";

            // Stands in for the Windows taskbar; a debug pause leaves the dock running
            bool Replacing { get { return S != null && S.GetBool("dockbar.replace", false) && !Taskbar.Paused; } }
            bool Full { get { return S != null && S.Get("dockbar.style", "float") == "full"; } }

            string HideMode
            {
                get
                {
                    if (S == null) return "never";
                    string m = S.Get("dockbar.hide", "never");
                    return m == "auto" || m == "smart" ? m : "never";
                }
            }

            public DockWindow(Island isle, Screen screen, bool globalOwner)
            {
                this.isle = isle;
                targetScreen = screen;
                deviceName = screen != null ? screen.DeviceName : "";
                this.globalOwner = globalOwner;
                if (screen != null)
                {
                    mon = screen.Bounds;
                    int want = (int)Math.Ceiling((BarH + Edge) * scale);
                    int bottom = screen.WorkingArea.IsEmpty ? screen.Bounds.Bottom : screen.WorkingArea.Bottom;
                    area = new Rectangle(screen.Bounds.Left, bottom - want, screen.Bounds.Width, want);
                }
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                AllowDrop = true;
                Text = "Deskmate Dock";
                Bounds = new Rectangle(-32000, -32000, 1, 1);
                Tune(reveal, RevealK, RevealDamping);
                Tune(hover, 260, 0.9f);
                Tune(workDot, 340, 0.78f);
                Tune(suruhDot, 340, 0.78f);
                tick.Tick += delegate { Tick(); };
                dwell.Tick += delegate { dwell.Stop(); Dwell(); };
                hideLater.Tick += delegate { hideLater.Stop(); AutoHide(); };
                revealLater.Tick += delegate { revealLater.Stop(); RevealNow(); };
                trayPop.Tick += delegate { TrayPopup(); };
                Tune(labelShow, 520, 0.9f);
            }

            // Used by small dock unit tests that do not create a visible dock.
            public DockWindow(Island isle) : this(isle, Screen.PrimaryScreen, true) { }

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
                pacer = new DockPacer(Handle, FrameMsg);
                frost = new DockFrost(pl => { try { BeginInvoke(new Action(() => TakePlate(pl))); } catch (Exception) { pl.Dispose(); } });
                RegisterShellHookWindow(Handle);
                watch.RescanIfStale(400);
                Changed(globalOwner);
                tick.Start();
                WatchNativeMinimize();
                // Entrance: the dock rises, then icons arrive one after another
                reveal.Snap(0);
                reveal.Target = 1;
                float delay = 0.12f;
                foreach (var a in apps) { a.Show.Snap(0); a.Show.Target = 1; a.Delay = delay; delay += 0.035f; }
                Animate();
            }

            bool allowClose;

            // Only the island closes the dock; outside WM_CLOSE is ignored
            public void Shutdown()
            {
                allowClose = true;
                Close();
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (!allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
                base.OnFormClosing(e);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                closing = true;
                StopNativeMinimize();
                tick.Stop();
                dwell.Stop();
                hideLater.Stop();
                revealLater.Stop();
                DeregisterShellHookWindow(Handle);
                if (pacer != null) pacer.Stop();
                if (frost != null) frost.Stop();
                CloseCard();
                if (sheet != null) SheetDone(sheet);
                StopKeys();
                StopTray();
                Reserve(false);
                // Give this screen's Windows taskbar back; other docks keep theirs hidden
                if (Replacing && !Dry) Taskbar.Release(S, monitorHandle);
                FreeCanvas();
                foreach (var b in fallbackIcons.Values) if (b != null) b.Dispose();
                fallbackIcons.Clear();
                FreeCaches();
                if (plate != null) plate.Dispose();
                if (dockBackdrop != null) { dockBackdrop.Dispose(); dockBackdrop = null; }
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    closing = true;
                    StopNativeMinimize();
                    if (sheet != null)
                    {
                        var s = sheet;
                        sheet = null;
                        if (!s.IsDisposed) { s.Close(); s.Dispose(); }
                    }
                    CloseCard();
                }
                base.Dispose(disposing);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                if (m.Msg == FrameMsg) { OnFrame(); return; }
                if (m.Msg == ShellMsg && ShellMsg != 0) { OnShell(ref m); return; }
                // The Windows taskbar moved: claim the strip again
                if (m.Msg == AppBarMsg) { if (m.WParam.ToInt64() == 1) { Reserve(Reserving && !Dry); Place(); Render(); } return; }
                // Explorer restarted (not our own tray echo)
                if (m.Msg == Taskbar.Created && Taskbar.Created != 0 && !TrayHost.OwnEcho) BeginInvoke(new Action(ExplorerBack));
                base.WndProc(ref m);
            }

            void ExplorerBack()
            {
                if (closing) return;
                if (Replacing && !Dry) Taskbar.Hide(S, monitorHandle);
                if (globalOwner && tray != null) tray.Resync();
                Changed(globalOwner);
            }

            bool Reserving { get { return HideMode == "never" && (Replacing || S.GetBool("dockbar.reserve", true)); } }

            // Live glass reads the screen behind; wallpaper-only glass does not
            public bool Glass { get { return isle != null && isle.GlassOn && !S.GetBool("dockbar.capture", false); } }
            bool GpuGlass { get { return Glass && CompGlass.Available && !(isle != null && isle.WallGlassOn); } }
            CompGlass.CompBackdrop dockBackdrop;

            // Settings or global-owner changes. Peer docks hide the bar on their own
            // screen but keep off the tray and Win key, which have a single owner.
            public void Changed(bool owner)
            {
                SetGlobalOwner(owner);
                if (!IsHandleCreated || closing || !UpdateMonitor()) return;
                bool needFrost = Glass && !GpuGlass;
                bool liveFrost = needFrost && !(isle != null && isle.WallGlassOn);
                Native.SetWindowDisplayAffinity(Handle, liveFrost ? Native.WDA_EXCLUDEFROMCAPTURE : 0);
                if (!GpuGlass && dockBackdrop != null) dockBackdrop.HideBackdrop();
                // Every dock that stands in for the taskbar hides that screen's bar,
                // otherwise the secondary screen shows Explorer's bar and ours together
                if (Replacing && !Dry) Taskbar.Hide(S, monitorHandle);
                Reserve(Reserving && !Dry);
                if (globalOwner && Replacing && !Dry && S.GetBool("dockbar.tray", true)) StartTray();
                else StopTray();
                // Yields bare Win automatically if an external Win-key launcher is active.
                // Starting it spins up hook threads and a keeper window, which waits on the
                // caller: keep that off the first-paint path and let the dock draw first.
                if (globalOwner && Replacing && !Dry && S.GetBool("dockbar.winkey", true)) PostKeys();
                else StopKeys();
                if (Glass && !GpuGlass) frost.Start(); else { frost.Stop(); if (plate != null) { plate.Dispose(); plate = null; } }
                foreach (var a in apps) TuneApp(a);
                // Hiding turned off while retracted: the edge no longer reveals, so rise now
                if (HideMode == "never") { hideLater.Stop(); revealLater.Stop(); if (!wantShown) Reveal(true); }
                Refresh(false);
                Place();
                drawnSig = "";
                Render();
                if (card != null && !card.IsDisposed) card.GlassChanged();
                if (sheet != null && !sheet.IsDisposed) sheet.GlassChanged();
            }

            public void SetGlobalOwner(bool owner)
            {
                if (!owner && globalOwner) { globalOwner = false; StopTray(); StopKeys(); }
                else globalOwner = owner;
            }

            bool UpdateMonitor()
            {
                Screen target = null;
                foreach (var screen in Screen.AllScreens)
                    if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) { target = screen; break; }
                if (target == null) target = targetScreen;
                if (target == null) return false;
                targetScreen = target;
                monitorHandle = Native.MonitorFromPoint(new Native.POINT(target.Bounds.Left + target.Bounds.Width / 2, target.Bounds.Top + target.Bounds.Height / 2), 2);
                var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                if (monitorHandle != IntPtr.Zero && Native.GetMonitorInfo(monitorHandle, ref mi) &&
                    Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom) == target.Bounds)
                {
                    mon = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
                }
                else
                {
                    mon = target.Bounds;
                }
                scale = 1;
                try
                {
                    uint dx, dy;
                    if (monitorHandle != IntPtr.Zero && Native.GetDpiForMonitor(monitorHandle, 0, out dx, out dy) == 0) scale = dx / 96f;
                }
                catch (Exception ex) { Log.Write("dock dpi: " + ex.Message); }
                float forced;
                if (float.TryParse(Environment.GetEnvironmentVariable("DESKMATE_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forced) && forced > 0.5f) scale = forced;
                return true;
            }

            // Strip along the bottom; reserved, maximized windows stop above it
            void Reserve(bool on)
            {
                int want = (int)Math.Ceiling((BarH + Edge) * scale);
                var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)), hWnd = Handle, uCallbackMessage = AppBarMsg };
                if (!on)
                {
                    if (registered) { Native.SHAppBarMessage(1, ref abd); registered = false; }
                    var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                    var work = mon;
                    if (monitorHandle != IntPtr.Zero && Native.GetMonitorInfo(monitorHandle, ref mi) &&
                        Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom) == mon)
                    {
                        work = Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
                    }
                    else if (targetScreen != null && !targetScreen.WorkingArea.IsEmpty)
                    {
                        work = targetScreen.WorkingArea;
                    }
                    // Auto-hide sits on the monitor edge, over everything
                    int bottom = HideMode == "never" ? work.Bottom : mon.Bottom;
                    area = new Rectangle(mon.Left, bottom - want, mon.Width, want);
                    return;
                }
                if (!registered) { Native.SHAppBarMessage(0, ref abd); registered = true; }
                abd.uEdge = 3;
                abd.rc = new Native.RECT { Left = mon.Left, Right = mon.Right, Top = mon.Bottom - want, Bottom = mon.Bottom };
                // Windows moves the strip above its own taskbar
                Native.SHAppBarMessage(2, ref abd);
                abd.rc.Top = abd.rc.Bottom - want;
                Native.SHAppBarMessage(3, ref abd);
                area = Rectangle.FromLTRB(abd.rc.Left, abd.rc.Top, abd.rc.Right, abd.rc.Bottom);
            }

            DockWindow TrayOwner()
            {
                if (tray != null) return this;
                if (!Replacing || !S.GetBool("dockbar.tray", true)) return null;
                foreach (var dock in isle.dockWins.Values)
                    if (!dock.closing && dock.tray != null) return dock;
                return null;
            }

            void StartTray()
            {
                if (tray != null) return;
                tray = new TrayHost();
                tray.Changed += PostTrayChanged;
                // Taskbar settings are read off the UI thread; a changed read redraws the tray
                promoHandler = PostTrayChanged;
                TrayPromo.Updated += promoHandler;
                TrayPromo.Refresh();
                tray.Start(TrayArea());
            }

            Action promoHandler;

            // Any thread: one TrayChanged queued at a time
            void PostTrayChanged()
            {
                if (System.Threading.Interlocked.Exchange(ref trayPosted, 1) != 0) return;
                try { BeginInvoke(new Action(TrayChanged)); } catch (Exception) { trayPosted = 0; }
            }

            void StopTray()
            {
                if (card != null && card.Kind == DockCard.TrayKind) CloseCard();
                if (tray == null) return;
                var t = tray;
                tray = null;
                if (promoHandler != null) { TrayPromo.Updated -= promoHandler; promoHandler = null; }
                t.Stop();
                foreach (var dock in isle.dockWins.Values)
                    if (dock != this && !dock.closing && dock.TrayExpanded) dock.CloseCard();
                trayIcons = new List<TrayIcon>();
            }

            void TrayChanged()
            {
                trayPosted = 0;
                if (tray == null || closing) return;
                trayIcons = tray.Snapshot();
                foreach (var dock in isle.dockWins.Values)
                    if (dock != this && !dock.closing && dock.IsHandleCreated) dock.RefreshTrayCard();
                RefreshTrayCard();
            }

            void RefreshTrayCard()
            {
                if (card != null && card.Kind == DockCard.TrayKind) card.Refill();
                // Hidden icons change often (sync spinners); redraw only for what shows
                string sig = TraySig();
                if (sig == traySig) return;
                traySig = sig;
                Render();
            }

            string traySig = "";

            string TraySig()
            {
                var sb = new StringBuilder();
                foreach (var ic in InlineTray()) sb.Append(ic.Key).Append(ic.Stamp).Append(',');
                sb.Append(OverflowTray().Count > 0);
                return sb.ToString();
            }

            public List<TrayIcon> TrayOverflow() { return OverflowTray(); }

            // Windows came, went, flashed or took focus
            void OnShell(ref Message m)
            {
                int code = (int)(m.WParam.ToInt64() & 0xFFFF);
                IntPtr h = m.LParam;
                bool changed = false;
                switch (code)
                {
                    case 1: changed = watch.Track(h); break;
                    case 2: WindowWentAway(h); changed = watch.Drop(h); break;
                    case 4:
                    case 0x8004:
                        changed = watch.Track(h);
                        watch.Activated(h);
                        front = h;
                        changed = true;
                        if (HideMode == "auto" && IsStartOrSearch(h)) Reveal(true);
                        // An app took over from the desktop: hide again
                        else if (HideMode == "auto" && !inside && wantShown && sheet == null && card == null && !keyMode) { hideLater.Stop(); hideLater.Start(); }
                        break;
                    case 6:
                        watch.Track(h);
                        if (card != null && card.Kind == DockCard.WindowsKind) card.Refill();
                        break;
                    case 0x8006: changed = watch.Flashed(h); break;
                    case 5:
                        if (globalOwner && MinRect(m.LParam)) { m.Result = (IntPtr)1; return; }
                        break;
                    case 13: watch.Drop(h); changed = watch.Rescan() || true; break;
                }
                if (changed) Refresh(false);
                if (code == 4 || code == 0x8004) ShellWindowActivated(h);
                m.Result = IntPtr.Zero;
            }

            // Minimize flies into the app's icon
            bool MinRect(IntPtr info)
            {
                MemInfo mi;
                if (info == IntPtr.Zero || VirtualQuery(info, out mi, (IntPtr)Marshal.SizeOf(typeof(MemInfo))) == IntPtr.Zero) return false;
                // Must be ours and writable
                if (mi.State != 0x1000 || (mi.Protect & 0x04) == 0) return false;
                IntPtr h = Marshal.ReadIntPtr(info);
                Rectangle r = Rectangle.Empty;
                if (Visible && h != IntPtr.Zero && h == isle.WorkHwnd && S.GetBool("dockbar.work", true))
                    r = MotionIcon(workSlotApp);
                else if (Visible && h != IntPtr.Zero && h == isle.SuruhHwnd && S.GetBool("dockbar.suruh", true))
                    r = MotionIcon(suruhSlotApp);
                else
                {
                    var app = apps.Find(a => a.Wins.Exists(w => w.H == h));
                    if (app == null || !Visible) return false;
                    r = MotionIcon(app);
                    if (!r.IsEmpty && !WindowMotionEnabled) { app.GulpUtc = DateTime.UtcNow; Animate(); }
                }
                if (r.IsEmpty) return false;
                int off = IntPtr.Size;
                Marshal.WriteInt32(info, off, r.Left);
                Marshal.WriteInt32(info, off + 4, r.Top);
                Marshal.WriteInt32(info, off + 8, r.Right);
                Marshal.WriteInt32(info, off + 12, r.Bottom);
                return true;
            }

            static bool IsStartOrSearch(IntPtr h)
            {
                var sb = new StringBuilder(64);
                Native.GetClassName(h, sb, 64);
                return sb.ToString() == "Windows.UI.Core.CoreWindow";
            }

            // Pins and windows into the dock's list; leavers shrink away
            public void Refresh(bool first)
            {
                var keep = new Dictionary<string, DockApp>();
                foreach (var a in apps) if (!a.Leaving) keep[a.Key] = a;
                IntPtr skipWork = S.GetBool("dockbar.work", true) ? isle.WorkHwnd : IntPtr.Zero;
                IntPtr skipSuruh = S.GetBool("dockbar.suruh", true) ? isle.SuruhHwnd : IntPtr.Zero;
                HashSet<string> runningBefore = null;
                HashSet<IntPtr> windowsBefore = null;
                if (petIconBaseline && !first && PetIconReady)
                {
                    runningBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    windowsBefore = new HashSet<IntPtr>();
                    foreach (var a in keep.Values)
                    {
                        if (!a.Running) continue;
                        runningBefore.Add(a.Key);
                        foreach (var w in a.Wins) windowsBefore.Add(w.H);
                    }
                }
                var next = watch.Arrange(isle.DockPins(), keep, skipWork, skipSuruh);
                var result = new List<DockApp>(next);
                var inNext = new HashSet<DockApp>(next);
                for (int i = 0; i < apps.Count; i++)
                {
                    var a = apps[i];
                    if (inNext.Contains(a)) continue;
                    if (!a.Leaving) { a.Leaving = true; a.Show.Target = 0; CloseCardFor(a); }
                    if (a.Show.Value < 0.02f) continue;
                    int at = 0;
                    for (int k = i - 1; k >= 0; k--)
                    {
                        int idx = result.IndexOf(apps[k]);
                        if (idx >= 0) { at = idx + 1; break; }
                    }
                    result.Insert(at, a);
                }
                foreach (var a in next)
                {
                    if (!a.Placed)
                    {
                        a.Placed = true;
                        TuneApp(a);
                        a.Show.Snap(first ? 1 : 0);
                        a.Grow.Snap(1);
                        a.Dot.Snap(0);
                    }
                    a.Show.Target = 1;
                    LoadIcon(a);
                    // Wants attention: a few hops, then the accent stays
                    if (a.Attention && a.Hops == 0 && a.Hop < 0) { a.Hop = 0; a.Hops = 3; }
                }
                apps = result;
                ApplyBadges();
                if (runningBefore != null)
                    foreach (var a in next)
                        if (a.Running && !runningBefore.Contains(a.Key) && !a.Wins.Exists(w => windowsBefore.Contains(w.H)))
                            QueuePetIcon(a, false);
                petIconBaseline = true;
                // Focus moving between windows the dock does not show changes nothing here
                string look = LookSig();
                if (look != lookSig || Moving()) { lookSig = look; Animate(); }
            }

            string lookSig = "";

            public void SyncWork()
            {
                if (closing || !IsHandleCreated) return;
                IntPtr h = isle.WorkHwnd;
                if (h != IntPtr.Zero) watch.Track(h);
                UpdateVisibility();
                if (!isle.WorkOpen && card != null && card.Kind == DockCard.WindowsKind && card.App != null && card.App.Key == "sys:work") CloseCard();
                Refresh(false);
                workDot.Target = WorkDotTarget();
                Animate();
                Render();
            }

            public void SyncSuruh()
            {
                if (closing || !IsHandleCreated) return;
                IntPtr h = isle.SuruhHwnd;
                if (h != IntPtr.Zero) watch.Track(h);
                UpdateVisibility();
                if (!isle.SuruhOpen && card != null && card.Kind == DockCard.WindowsKind && card.App != null && card.App.Key == "sys:suruh") CloseCard();
                Refresh(false);
                suruhDot.Target = SuruhDotTarget();
                Animate();
                Render();
            }

            // What the centre shows: apps, their windows, focus, badges, calls for attention
            string LookSig()
            {
                var sb = new StringBuilder();
                IntPtr fg = GetForegroundWindow();
                sb.Append(S.GetBool("dockbar.work", true) && isle.WorkOpen ? (fg == isle.WorkHwnd ? 'W' : 'w') : '-').Append('|');
                sb.Append(S.GetBool("dockbar.suruh", true) && isle.SuruhOpen ? (fg == isle.SuruhHwnd ? 'S' : 's') : '-').Append(isle.suruhBusy ? '*' : '.').Append('|');
                foreach (var a in apps)
                    sb.Append(a.Key).Append(a.Pinned ? 'p' : 'r').Append(a.Wins.Count).Append(a.Attention ? '!' : '.').Append(a.Badge).Append(a.Leaving ? 'x' : 'o').Append(a.Wins.Exists(w => w.H == fg) ? 'f' : '-').Append('|');
                return sb.ToString();
            }

            public void PulseLaunchedApp(string target, string appId)
            {
                var app = apps.Find(a =>
                    (target != null && target.Length > 0 && string.Equals(a.Target, target, StringComparison.OrdinalIgnoreCase)) ||
                    (appId != null && appId.Length > 0 && string.Equals(a.AppId, appId, StringComparison.OrdinalIgnoreCase)));
                if (app == null) return;
                LaunchFeedback(app);
            }

            bool Moving()
            {
                var now = DateTime.UtcNow;
                if (!reveal.Settled || !hover.Settled || !workDot.Settled || !suruhDot.Settled || !shelfShow.Settled || shelfShow.Target != (ShelfWanted ? 1 : 0) || WidgetsMoving() || DockFlyersMoving(now)) return true;
                if (AppAnimMoving(workSlotApp, now) || AppAnimMoving(suruhSlotApp, now)) return true;
                foreach (var s in sysGrow.Values) if (!s.Settled) return true;
                foreach (var s in sysPress.Values) if (!s.Settled) return true;
                if (!Anim.Reduced && startPulseUtc > DateTime.MinValue && (now - startPulseUtc).TotalSeconds < 0.40) return true;
                foreach (var a in apps) if (!a.Show.Settled || !a.Grow.Settled || !a.Dot.Settled || a.Hop >= 0 || AppAnimMoving(a, now)) return true;
                return false;
            }

            // Key of the app whose window has focus
            string FocusKey()
            {
                IntPtr fg = GetForegroundWindow();
                if (S.GetBool("dockbar.work", true) && isle.WorkOpen && fg == isle.WorkHwnd) return "sys:work";
                if (S.GetBool("dockbar.suruh", true) && isle.SuruhOpen && fg == isle.SuruhHwnd) return "sys:suruh";
                var app = apps.Find(a => a.Wins.Exists(w => w.H == fg));
                return app != null ? app.Key : "";
            }

            void TuneApp(DockApp a)
            {
                Tune(a.Grow, 900, 0.95f);
                Tune(a.Show, 260, 0.8f);
                Tune(a.Dot, 340, 0.78f);
                Tune(a.X, 300, 0.82f);
            }

            void ApplyBadges()
            {
                var counts = S.GetBool("dockbar.badges", true) ? isle.DockBadgeCounts() : null;
                foreach (var a in apps)
                {
                    int n = 0;
                    if (counts != null && a.AppId.Length > 0) counts.TryGetValue(a.AppId, out n);
                    if (counts != null && n == 0 && a.Name.Length > 0) counts.TryGetValue("name:" + a.Name, out n);
                    int previous = a.Badge;
                    a.Badge = n;
                    if (petBadgeBaseline && n > previous && !a.Leaving && a.Show.Value > 0.5f) QueuePetIcon(a, true);
                }
                petBadgeBaseline = counts != null;
            }

            string IconCacheKey(string key) { return key + "@" + IconSourcePx(); }

            // Icon at the biggest size it will be drawn
            void LoadIcon(DockApp a)
            {
                string ck = IconCacheKey(a.Key);
                if (icons.ContainsKey(ck)) return;
                int px = IconSourcePx();
                Bitmap bmp = null;
                string item = a.Pinned ? a.Target : a.Packaged && a.AppId.Length > 0 ? ShellApp.AppsFolderOf(a.AppId) : a.Exe;
                if (item.Length > 0) bmp = ShellApp.Icon(item, px);
                if (bmp == null && a.Exe.Length > 0 && !a.Exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) bmp = ShellApp.Icon(a.Exe, px);
                if (bmp == null && a.Wins.Count > 0) bmp = WindowIcon(a.Wins[0].H);
                icons[ck] = bmp;
            }

            [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, int flags, int ms, out IntPtr result);
            [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr h, int index);

            static Bitmap WindowIcon(IntPtr h)
            {
                IntPtr r;
                SendMessageTimeout(h, 0x7F, (IntPtr)1, IntPtr.Zero, 2, 60, out r);
                if (r == IntPtr.Zero) r = GetClassLongPtr(h, -14);
                if (r == IntPtr.Zero) return null;
                try { using (var ic = Icon.FromHandle(r)) return ic.ToBitmap(); }
                catch (Exception) { return null; }
            }

            int IconSourcePx()
            {
                float px = IconRest * Math.Max(1, Mag) * scale;
                foreach (int s in new[] { 48, 64, 96, 128, 256 }) if (px <= s) return s;
                return 256;
            }

            void TakePlate(DockPlate pl)
            {
                if (closing || !Glass) { pl.Dispose(); return; }
                var old = plate;
                plate = pl;
                if (old != null) old.Dispose();
                if (card != null) card.Invalidate();
                if (pacer != null && IsHandleCreated && !Dry) Animate();
                else Render();
            }

            // Windows moved behind: the glass looks again
            public void Nudge()
            {
                if (frost != null) frost.Nudge();
                if (sheet != null && !sheet.IsDisposed) sheet.NudgeFrost();
                if (closing || !IsHandleCreated) return;
                UpdateVisibility();
                KeepOnTop();
                if (shown && HideMode == "smart") SmartHide();
                // Focus moved elsewhere: the tray grid has done its job
                if (TrayExpanded && (DateTime.UtcNow - trayOpenUtc).TotalMilliseconds > 250)
                {
                    IntPtr fg = GetForegroundWindow();
                    var cur = Cursor.Position;
                    if (fg != IntPtr.Zero && fg != trayFrontHwnd && !card.Bounds.Contains(cur) && !ChevContainsScreen(cur) && pressId != "chev")
                        CloseCard();
                }
            }

            void UpdateVisibility()
            {
                var work = isle.WorkOpen ? isle.WorkHwnd : IntPtr.Zero;
                var suruh = isle.SuruhOpen ? isle.SuruhHwnd : IntPtr.Zero;
                var foreground = GetForegroundWindow();
                bool workspaceActive = (work != IntPtr.Zero && foreground != IntPtr.Zero
                    && (foreground == work || GetAncestor(foreground, 3) == work)
                    && Native.MonitorFromWindow(work, 2) == monitorHandle)
                    || (suruh != IntPtr.Zero && foreground != IntPtr.Zero
                    && (foreground == suruh || GetAncestor(foreground, 3) == suruh)
                    && Native.MonitorFromWindow(suruh, 2) == monitorHandle);
                bool hold = workspaceActive || (isle.dockSwitchMonitor != IntPtr.Zero && isle.dockSwitchMonitor == monitorHandle);
                bool wasHeld = focusReveal;
                bool wasShown = shown;
                focusReveal = hold;
                bool want = (hold || !Native.IsFullscreenOn(monitorHandle)) && !(globalOwner && isle.ExceptHidden()) && !isle.dormant && !isle.hiddenByUser;
                if (want != shown)
                {
                    shown = want;
                    Native.ShowWindow(Handle, want ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
                    if (want) { drawnSig = ""; Render(); }
                    else { CloseCard(); if (dockBackdrop != null) dockBackdrop.HideBackdrop(); }
                }
                if (!shown) return;
                if (hold && (!wantShown || !wasHeld || !wasShown))
                {
                    if (!Dry && isle.dockSwitchMonitor == monitorHandle && monitorHandle != IntPtr.Zero)
                        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    Reveal(true);
                }
                else if (!hold && wasHeld && HideMode != "never")
                {
                    hideLater.Stop();
                    hideLater.Start();
                }
            }

            // Sunk under an app: back to the top band; the owned pet follows, the glass is restacked
            void KeepOnTop()
            {
                if (Dry || !shown || !Native.KeepTopmost(Handle)) return;
                if (dockBackdrop != null) dockBackdrop.SyncOrder(Handle);
            }

            internal void RevealAfterSwitch(IntPtr monitor)
            {
                if (closing || !IsHandleCreated || monitorHandle != monitor || HideMode == "never") return;
                UpdateVisibility();
                if (!shown || focusReveal) return;
                Reveal(true);
                hideLater.Start();
            }

            void Tick()
            {
                if (closing) return;
                tickN++;
                UpdateVisibility();
                if (!shown) return;
                KeepOnTop();
                if (HideMode == "auto" && StayOnDesktop && !wantShown && DesktopFront()) Reveal(true);
                // Any dock stands in for the bar on its screen; Rehide only touches owned ones
                if (Replacing && !Dry && Taskbar.Showing()) Taskbar.Rehide();
                if (tickN % 3 == 0) ReadSystem();
                // Shell hooks cover most changes; this catches the rest (shared across monitors)
                if (tickN % 10 == 0 && watch.RescanIfStale(1500))
                {
                    if (isle != null) isle.RefreshDockApps();
                    else Refresh(false);
                }
                if (tickN % 5 == 0) ApplyBadges();
                if (Signature() != drawnSig) Render();
            }

            public void SyncWeather()
            {
                if (closing || !IsHandleCreated) return;
                if (shown && Signature() != drawnSig) Render();
            }

            // Whatever a still frame shows, as text
            string Signature()
            {
                var sb = new StringBuilder();
                sb.Append(Fmt.Clock(DateTime.Now)).Append('|').Append(hoverId).Append('|').Append(pressId).Append('|').Append(FocusKey()).Append('|').Append(isle.WorkOpen).Append('|').Append(isle.SuruhOpen).Append('|').Append(isle.suruhBusy).Append('|').Append(TrayExpanded).Append('|').Append(isle.dockHeld.Count);
                foreach (var a in apps) sb.Append(a.Key.Length).Append(a.Wins.Count).Append(a.Badge).Append(a.Attention ? '!' : '.');
                var mi = isle.mediaInfo;
                if (mi.Present) sb.Append(mi.Title).Append(mi.Playing);
                if (isle.TimerOn) sb.Append(isle.TimerClock());
                sb.Append(isle.battery).Append(isle.onAc).Append(cpuPct).Append(ramPct).Append(isle.BusyCount()).Append(isle.quotaVersion);
                sb.Append(isle.vol).Append(isle.muted).Append(isle.unread).Append(Ink.Epoch).Append(Lang.English);
                if (isle.weather != null) sb.Append(isle.weather.Temp).Append(':').Append(isle.weather.Code);
                return sb.ToString();
            }

            // Frames run only while something moves
            void Animate()
            {
                if (closing || pacer == null) return;
                dockNeedsFrame = true;
                if (pacer.Want()) lastT = clock.Elapsed.TotalSeconds - 1 / 60.0;
            }

            void OnFrame()
            {
                if (closing) return;
                double t = clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(0.05, Math.Max(0.001, t - lastT));
                lastT = t;
                bool more = false;
                double f0 = clock.Elapsed.TotalMilliseconds, f1 = f0;
                // A failed frame must not leave the pacer waiting forever
                try
                {
                    bool dockMoving = Step(dt);
                    f1 = clock.Elapsed.TotalMilliseconds;
                    bool paintDock = dockMoving || dockWasMoving || dockNeedsFrame;
                    if (paintDock)
                    {
                        dockNeedsFrame = false;
                        Render();
                    }
                    dockWasMoving = dockMoving || WidgetsMoving();
                    more = dockWasMoving || dockNeedsFrame;
                }
                finally { pacer.Done(more); }
                if (fpsLog) CountDockFrame(f0, f1, clock.Elapsed.TotalMilliseconds, more);
            }

            // DESKMATE_FPS=1 also logs dock frames
            static readonly bool fpsLog = Environment.GetEnvironmentVariable("DESKMATE_FPS") == "1";
            bool dockNeedsFrame, dockWasMoving;
            int fpsN, fpsGc;
            double fpsFrom, fpsLast, fpsStep, fpsRender, fpsWorst, fpsGapWorst;

            void CountDockFrame(double f0, double f1, double f2, bool more)
            {
                double ms = f2 - f0;
                if (fpsLast > 0 && f0 - fpsLast < 250) fpsGapWorst = Math.Max(fpsGapWorst, f0 - fpsLast);
                else fpsFrom = f0;
                fpsLast = f2;
                fpsN++;
                fpsStep += f1 - f0;
                fpsRender += f2 - f1;
                fpsWorst = Math.Max(fpsWorst, ms);
                int gc = GC.CollectionCount(0);
                if (ms > 12) Log.Write("dock slow " + ms.ToString("0.0") + "ms step=" + (f1 - f0).ToString("0.0") + " render=" + (f2 - f1).ToString("0.0") + " " + cw + "x" + ch + (gc != fpsGc ? " [GC]" : ""));
                fpsGc = gc;
                if (more && f2 - fpsFrom < 2000) return;
                double span = Math.Max(1, f2 - fpsFrom);
                Log.Write("dock fps " + (fpsN * 1000 / span).ToString("0.0") + " step " + (fpsStep / fpsN).ToString("0.00") + "ms render " + (fpsRender / fpsN).ToString("0.00") +
                          "ms worst " + fpsWorst.ToString("0.0") + "ms gap " + fpsGapWorst.ToString("0.0") + "ms " + cw + "x" + ch);
                fpsN = 0; fpsStep = fpsRender = fpsWorst = fpsGapWorst = 0; fpsFrom = f2;
            }

            // Auto-hide: the edge brings it back, after an optional wait
            void Reveal(bool on)
            {
                if (on) hideLater.Stop();
                revealLater.Stop();
                wantShown = on;
                Tune(reveal, RevealK, RevealDamping);
                reveal.Target = on ? 1 : 0;
                Animate();
            }

            // Milliseconds the pointer must rest on the edge before the dock rises
            int RevealDelay { get { return Math.Max(0, Math.Min(2000, S.GetInt("dockbar.revealdelay", 0))); } }

            // Pointer reached a hidden dock: wait, unless the wait is off
            internal void HoverReveal()
            {
                if (wantShown || closing || !shown) return;
                if (!PointerOnEdge()) { CancelHoverReveal(); return; }
                if (revealLater.Enabled) return;
                int wait = RevealDelay;
                if (wait <= 0) { Reveal(true); return; }
                revealLater.Interval = wait;
                revealLater.Start();
            }

            // Pointer left, or something else already showed the dock
            internal void CancelHoverReveal() { revealLater.Stop(); }

            // The wait is over: only rise if the pointer is still resting there
            void RevealNow()
            {
                if (closing || wantShown || HideMode == "never") return;
                if (!shown || !PointerOnEdge()) return;
                Reveal(true);
            }

            void AutoHide()
            {
                if (focusReveal || HideMode == "never" || inside || card != null || sheet != null || keyMode || dragging || fileOver || shelfDragOut) return;
                // The edge belongs to the dock even where the floating bar leaves
                // a transparent gap. Do not retract under a pointer resting there.
                if (PointerOnEdge()) return;
                if (HideMode == "auto" && StayOnDesktop && DesktopFront()) return;
                if (HideMode == "smart" && !Covered()) return;
                Reveal(false);
            }

            void SmartHide()
            {
                if (focusReveal || inside || card != null) return;
                bool covered = Covered();
                if (covered && wantShown) { hideLater.Stop(); hideLater.Start(); }
                else if (!covered && !wantShown) Reveal(true);
            }

            // A window sits where the dock is
            bool Covered()
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == Handle || watch.Find(fg) == null) return false;
                Native.RECT r;
                if (!Native.GetWindowRect(fg, out r)) return false;
                var bar = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                return bar.IntersectsWith(new Rectangle(mon.Left, area.Bottom - (int)(BarH * scale), mon.Width, (int)(BarH * scale)));
            }

            // CPU from system times; RAM load
            [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
            [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);

            [StructLayout(LayoutKind.Sequential)]
            struct MemStatus { public uint Length, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }

            long idle0, kern0, user0;
            int cpuPct = -1, ramPct = -1;

            void ReadSystem()
            {
                if (!isle.DockWidget("system")) { cpuPct = -1; return; }
                long idle, kern, user;
                if (GetSystemTimes(out idle, out kern, out user))
                {
                    long di = idle - idle0, total = (kern - kern0) + (user - user0);
                    if (idle0 != 0 && total > 0) cpuPct = (int)Math.Round(100.0 * (total - di) / total);
                    idle0 = idle; kern0 = kern; user0 = user;
                    if (cpuPct < 0) cpuPct = 0;
                }
                var m = new MemStatus { Length = (uint)Marshal.SizeOf(typeof(MemStatus)) };
                if (GlobalMemoryStatusEx(ref m)) ramPct = (int)m.Load;
            }
        }
    }
}
