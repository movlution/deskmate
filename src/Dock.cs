using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Deskmate
{
    // Dock: opt-in bar of apps and live widgets
    partial class Island
    {
        // The selected island display owns taskbar/tray/Win-key integration;
        // every detected display gets an independent visual dock.
        DockWindow dockWin;
        readonly Dictionary<string, DockWindow> dockWins = new Dictionary<string, DockWindow>(StringComparer.OrdinalIgnoreCase);
        internal readonly AppWatch dockWatch = new AppWatch();
        internal readonly Dictionary<string, Bitmap> dockIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly System.Windows.Forms.Timer dockLeave = new System.Windows.Forms.Timer { Interval = 3500 };
        readonly System.Windows.Forms.Timer dockSwitchPoll = new System.Windows.Forms.Timer { Interval = 60 };
        readonly System.Windows.Forms.Timer taskbarPeekTimer = new System.Windows.Forms.Timer();
        FileSystemWatcher pinWatch;
        readonly System.Windows.Forms.Timer pinSync = new System.Windows.Forms.Timer { Interval = 600 };

        // Keys apart from "dock", which glues the island to its edge
        bool DockBarOn { get { return settings.GetBool("dockbar.on", false); } }

        static readonly string[] DockWidgetIds = { "clock", "media", "ai", "timer", "weather", "battery", "system" };
        static readonly string[] DockWidgetNames = { "Jam", "Musik", "Kuota AI dan agen", "Timer", "Cuaca", "Baterai", "Sistem" };

        bool DockWidget(string id) { return settings.GetBool("dockbar.w." + id, id != "system"); }

        sealed class DockPlan
        {
            public readonly List<string> Live = new List<string>();
            public readonly List<string> Close = new List<string>();
            public string Owner = "";
        }

        // Kept pure so hot-plug reconciliation is deterministic before windows change.
        static DockPlan PlanDocks(IEnumerable<string> screens, IEnumerable<string> existing, string selected)
        {
            var plan = new DockPlan();
            foreach (var device in screens)
                if (!string.IsNullOrEmpty(device) && !plan.Live.Exists(x => string.Equals(x, device, StringComparison.OrdinalIgnoreCase))) plan.Live.Add(device);
            foreach (var device in existing)
                if (!plan.Live.Exists(x => string.Equals(x, device, StringComparison.OrdinalIgnoreCase))) plan.Close.Add(device);
            foreach (var device in plan.Live)
                if (string.Equals(device, selected, StringComparison.OrdinalIgnoreCase)) { plan.Owner = device; break; }
            if (plan.Owner.Length == 0 && plan.Live.Count > 0) plan.Owner = plan.Live[0];
            return plan;
        }

        // The pet stands on the owner dock, so it is detached before docks close and re-seated after
        void ApplyDock()
        {
            DetachPet();
            ApplyDockBars();
            ApplyPet();
        }

        void ApplyDockBars()
        {
            if (!DockBarOn) { CloseDocks(); return; }
            if (dockWins.Count == 0) { ImportTaskbarPins(false); WatchPins(true); }

            var screens = System.Windows.Forms.Screen.AllScreens;
            var devices = new List<string>();
            foreach (var screen in screens) devices.Add(screen.DeviceName);
            var selected = ChosenScreen();
            var plan = PlanDocks(devices, dockWins.Keys, selected != null ? selected.DeviceName : "");

            // Stop shared owner services on docks that lose them, before the new owner
            // takes over. A dock that stays owner keeps its tray: rebuilding it empties
            // the icon list, and apps that ignore TaskbarCreated never come back.
            foreach (var kv in dockWins)
                if (!string.Equals(kv.Key, plan.Owner, StringComparison.OrdinalIgnoreCase)) kv.Value.SetGlobalOwner(false);
            foreach (var device in plan.Close)
            {
                var dock = dockWins[device];
                dock.Shutdown();
                dockWins.Remove(device);
            }
            if (!settings.GetBool("dockbar.replace", false))
            {
                taskbarPeekTimer.Stop();
                Taskbar.Paused = false;
                if (!DockWindow.Dry) Taskbar.Restore(settings);
            }
            if (plan.Live.Count > 0) dockWatch.RescanIfStale(400);

            foreach (var device in plan.Live)
            {
                System.Windows.Forms.Screen target = null;
                foreach (var screen in screens)
                    if (string.Equals(screen.DeviceName, device, StringComparison.OrdinalIgnoreCase)) { target = screen; break; }
                if (target == null) continue;
                DockWindow dock;
                bool global = string.Equals(device, plan.Owner, StringComparison.OrdinalIgnoreCase);
                if (!dockWins.TryGetValue(device, out dock))
                {
                    dock = new DockWindow(this, target, global);
                    dockWins.Add(device, dock);
                    dock.Show();
                }
                else dock.Changed(global);
            }
            dockWins.TryGetValue(plan.Owner, out dockWin);
            dockSwitchPoll.Enabled = dockWins.Count != 0 && !DockWindow.Dry;
        }

        void CloseDocks()
        {
            taskbarPeekTimer.Stop();
            Taskbar.Paused = false;
            if (dockSwitchPoll != null) dockSwitchPoll.Stop();
            dockSwitchMonitor = IntPtr.Zero;
            WatchPins(false);
            if (dockWins.Count == 0)
            {
                dockWin = null;
                if (!DockWindow.Dry) Taskbar.Restore(settings);
                return;
            }
            foreach (var dock in dockWins.Values) dock.SetGlobalOwner(false);
            foreach (var dock in dockWins.Values) dock.Shutdown();
            dockWins.Clear();
            dockWin = null;
            foreach (var b in dockIcons.Values) if (b != null) b.Dispose();
            dockIcons.Clear();
            if (!DockWindow.Dry) Taskbar.Restore(settings);
        }

        // Debug: hand the real taskbar back for a chosen number of seconds. The
        // saved "replace" choice and the dock itself stay untouched.
        void PeekTaskbar(int seconds)
        {
            if (!DockBarOn || !settings.GetBool("dockbar.replace", false) || dockWins.Count == 0) return;
            bool first = !Taskbar.Paused;
            Taskbar.Paused = true;
            if (first)
            {
                foreach (var dock in dockWins.Values) dock.Changed(dock == dockWin);
                if (!DockWindow.Dry) Taskbar.Restore(settings);
            }
            taskbarPeekTimer.Stop();
            taskbarPeekTimer.Interval = Math.Max(1, seconds) * 1000;
            taskbarPeekTimer.Start();
        }

        // Timer elapsed or "Sembunyikan taskbar Windows" picked
        void EndTaskbarPeek()
        {
            taskbarPeekTimer.Stop();
            if (!Taskbar.Paused) return;
            Taskbar.Paused = false;
            foreach (var dock in dockWins.Values) dock.Changed(dock == dockWin);
        }

        ToolStripMenuItem TaskbarPeekMenuItem()
        {
            var item = new ToolStripMenuItem(Lang.T("Tampilkan taskbar Windows"));
            foreach (int seconds in new[] { 5, 10, 30, 60 })
            {
                int value = seconds;
                item.DropDownItems.Add(Lang.F("{0} detik", value), null, delegate { PeekTaskbar(value); });
            }
            return item;
        }

        void RefreshDocks()
        {
            foreach (var dock in dockWins.Values) dock.Changed(dock == dockWin);
        }

        void RefreshDockApps()
        {
            foreach (var dock in dockWins.Values) dock.Refresh(false);
        }

        // Something on screen moved: dock glass and apps look again
        const uint DockSwitchStart = 0x0014, DockSwitchEnd = 0x0015;
        IntPtr dockSwitchMonitor;

        // Some switcher sessions don't deliver SWITCHSTART while the chord is held.
        void PollDockSwitch()
        {
            const int alt = 0x12, tab = 0x09, control = 0x11, down = 0x8000;
            bool altDown = (GetAsyncKeyState(alt) & down) != 0;
            if (dockSwitchMonitor != IntPtr.Zero)
            {
                if (altDown) return;
                UpdateDockSwitch(false, Native.GetForegroundWindow());
            }
            else
            {
                if (!altDown || (GetAsyncKeyState(tab) & down) == 0 || (GetAsyncKeyState(control) & down) != 0) return;
                UpdateDockSwitch(true, Native.GetForegroundWindow());
            }
            NudgeDock();
        }

        void UpdateDockSwitch(bool switching, IntPtr hwnd)
        {
            if (switching)
            {
                if (dockSwitchMonitor != IntPtr.Zero) return;
                IntPtr foreground = Native.GetForegroundWindow();
                dockSwitchMonitor = Native.MonitorFromWindow(foreground != IntPtr.Zero ? foreground : hwnd, 1);
                return;
            }
            dockSwitchMonitor = IntPtr.Zero;
            if (hwnd == IntPtr.Zero) hwnd = Native.GetForegroundWindow();
            IntPtr monitor = Native.MonitorFromWindow(hwnd, 1);
            foreach (var dock in dockWins.Values) dock.RevealAfterSwitch(monitor);
        }

        void NudgeDock() { foreach (var dock in dockWins.Values) dock.Nudge(); }

        // Dock click opens the island on a tab; closes if never visited
        void OpenFromDock(int pg)
        {
            if (!CanExpand()) return;
            if (expanded) SetPage(pg, false);
            else Expand(pg);
            dockLeave.Stop();
            dockLeave.Start();
        }

        void DockLeaveTick()
        {
            dockLeave.Stop();
            if (expanded && !pointerInside) { expanded = false; Kick(); }
        }

        List<string> DockPins()
        {
            var list = new List<string>();
            foreach (var part in settings.Get("dockbar.pins", "").Split('|'))
                if (part.Trim().Length > 0 && !list.Exists(x => string.Equals(x, part.Trim(), StringComparison.OrdinalIgnoreCase))) list.Add(part.Trim());
            return list;
        }

        void SetDockPins(List<string> pins)
        {
            settings.Set("dockbar.pins", string.Join("|", pins.ToArray()));
            settings.Save();
            RefreshDockApps();
        }

        // First run takes the Windows taskbar's pins, in its order
        void ImportTaskbarPins(bool again)
        {
            if (!again && settings.GetBool("dockbar.imported", false)) return;
            var pins = DockPins();
            foreach (var t in AppWatch.TaskbarPins())
                if (!pins.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) pins.Add(t);
            settings.Set("dockbar.imported", true);
            SetDockPins(pins);
        }

        // "Pin to taskbar" from Start and Explorer lands on the dock too
        void WatchPins(bool on)
        {
            if (!on)
            {
                if (pinWatch != null) { pinWatch.EnableRaisingEvents = false; pinWatch.Dispose(); pinWatch = null; }
                pinSync.Stop();
                return;
            }
            if (pinWatch != null || !Directory.Exists(AppWatch.PinnedFolder)) return;
            if (!pinSyncWired) { pinSync.Tick += delegate { SyncPins(); }; pinSyncWired = true; }
            try
            {
                lastPinFiles = PinFiles();
                pinWatch = new FileSystemWatcher(AppWatch.PinnedFolder, "*.lnk") { NotifyFilter = NotifyFilters.FileName };
                FileSystemEventHandler h = delegate { Post(delegate { pinSync.Stop(); pinSync.Start(); }); };
                pinWatch.Created += h;
                pinWatch.Deleted += h;
                pinWatch.Renamed += delegate { Post(delegate { pinSync.Stop(); pinSync.Start(); }); };
                pinWatch.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("dock pin watch: " + ex.Message); }
        }

        HashSet<string> lastPinFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool pinSyncWired;

        static HashSet<string> PinFiles()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var f in Directory.GetFiles(AppWatch.PinnedFolder, "*.lnk")) set.Add(f); }
            catch (Exception) { }
            return set;
        }

        // Settled folder: new shortcuts join, removed ones leave
        void SyncPins()
        {
            pinSync.Stop();
            var now = PinFiles();
            var pins = DockPins();
            bool changed = false;
            foreach (var f in now)
                if (!lastPinFiles.Contains(f) && !pins.Exists(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase))) { pins.Add(f); changed = true; }
            foreach (var f in lastPinFiles)
                if (!now.Contains(f) && pins.RemoveAll(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase)) > 0) changed = true;
            lastPinFiles = now;
            if (changed) SetDockPins(pins);
        }

        // Notification centre count per app id, and per app name as a fallback
        Dictionary<string, int> DockBadgeCounts()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!NotifOn) return map;
            foreach (var n in notifs)
            {
                if (n.AppId.StartsWith("phone:", StringComparison.Ordinal)) continue;
                int c;
                if (n.AppId.Length > 0) { map.TryGetValue(n.AppId, out c); map[n.AppId] = c + 1; }
                if (n.AppName.Length > 0) { string k = "name:" + n.AppName; map.TryGetValue(k, out c); map[k] = c + 1; }
            }
            return map;
        }
    }
}
