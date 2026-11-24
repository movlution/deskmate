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
    partial class Island
    {
        // Toasts

        // Waiting behind a reminder, shown after
        readonly List<Toast> heldToasts = new List<Toast>();

        static bool IsReminder(View v) { return v == View.Remind || v == View.Pray; }

        // Passing feedback is dropped, news waits
        static bool Fleeting(View v) { return v == View.Volume || v == View.Brightness || v == View.Locks || v == View.Mic || v == View.Track || v == View.AudioDevice || v == View.Network || v == View.Privacy; }

        bool QuietToast(View v, int priority)
        {
            return (Quiet || MeetingOn || ExceptQuieted() || FocusHold) && priority < 3 &&
                (v == View.Notif || v == View.Track || v == View.Network || v == View.Privacy || v == View.Remind ||
                    v == View.Download || v == View.Info || v == View.Done || v == View.Guard);
        }

        Toast ShowToast(View v, double seconds, int priority, bool interactive, bool force = false)
        {
            if (toast != null && toast.Hold && !IsReminder(v))
            {
                if (Fleeting(v)) return null;
                var w = new Toast { View = v, Priority = priority, Interactive = interactive, Seconds = seconds };
                heldToasts.RemoveAll(h => h.View == v);
                heldToasts.Add(w);
                return w;
            }
            if (toast != null && toast.Priority > priority) return null;
            // Senyap: only what can't wait
            // Calls hold the same quiet news
            if (QuietToast(v, priority)) return null;
            // Retracted: quiet news stays in the sliver
            if (MiniOn() && !MiniLets(v, priority)) return null;
            if (expanded && !force)
            {
                if (priority < 3) return null;
                expanded = false;
            }
            bool same = toast != null && toast.View == v;
            var t = same ? toast : new Toast();
            t.View = v;
            t.Priority = priority;
            t.Interactive = interactive;
            t.Seconds = seconds;
            // Callers set it again after
            t.Page = -1;
            if (!same) t.Hold = false;
            if (!same)
            {
                t.ShownUtc = DateTime.UtcNow;
                if (!Anim.Reduced) topGap.Velocity += 70; // arrival dip
            }
            toast = t;
            if (!same) EyesToast(v, t);
            toastTimer.Stop();
            toastTimer.Interval = Math.Max(1, (int)(seconds * 1000));
            toastTimer.Start();
            Kick();
            // Text is filled after: size again
            Post(Kick);
            return t;
        }

        // Notices that pass Senyap, like timers
        void ShowNotice(char glyph, string title, string detail)
        {
            var t = ShowToast(View.Info, 3.2, 3, false, true);
            if (t == null) return;
            EyesReact(EyeMood.Happy, 0);
            t.Glyph = glyph;
            t.Title = title;
            t.Detail = detail;
        }

        void ShowInfo(char glyph, string title, string detail, bool force = false)
        {
            var t = ShowToast(View.Info, 2.8, 1, false, force);
            if (t == null) return;
            t.Glyph = glyph;
            t.Title = title;
            t.Detail = detail;
        }

        // The arrival time is absolute: repeated hover ticks cannot renew the lifetime.
        bool ToastExpired()
        {
            return toast != null && (DateTime.UtcNow - toast.ShownUtc).TotalSeconds >= toast.Seconds + 1.5;
        }

        void DismissToast()
        {
            toastTimer.Stop();
            if (IsDisposed || Disposing) return;
            // A closed reminder drops its sound whether it was pinned or simply aged out
            bool wasReminder = toast != null && IsReminder(toast.View);
            bool held = toast != null && toast.Hold;
            toast = null;
            if (held || wasReminder) StopRemind();
            ReleaseHeld();
            Kick();
        }

        // Next waiting toast, in arrival order
        void ReleaseHeld()
        {
            if (heldToasts.Count == 0) return;
            var t = heldToasts[0];
            heldToasts.RemoveAt(0);
            t.ShownUtc = DateTime.UtcNow;
            toast = t;
            EyesToast(t.View, t);
            toastTimer.Interval = Math.Max(1, (int)(t.Seconds * 1000));
            toastTimer.Start();
            Post(Kick);
        }

        // System

        void RaiseIsland()
        {
            // A foreground change can trigger this while a context menu is open.
            // Raising the layered Island then puts its content over the menu.
            if (islandMenu != null) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void CheckFullscreen()
        {
            RefreshIslandReplicas(true);
            bool fs = Native.IsFullscreenForDisplay(monitorHandle, AllIslandScreens) || ExceptHidden();
            if (!fs && windowShown) RaiseIsland();
            if (fs == fullscreen) return;
            fullscreen = fs;
            if (fs) expanded = false;
            Kick();
        }

        void ClockTick()
        {
            if (IsDisposed || Disposing) { clockTimer.Stop(); return; }
            RefreshComboPause();
            var nowUtc = DateTime.UtcNow;
            string hm = Fmt.Clock(DateTime.Now);
            bool minute = hm != clockRoller.Value;
            clockRoller.Set(hm);
            // The clock rolls only where it shows
            bool redraw = minute && current == View.Idle && (!IdleEyes || SlotsTimed());
            redraw |= LifeTick(minute);
            // Running timers tick in their live areas
            TimerTick(nowUtc);
            TickRapat();
            IngatTick();
            TickOneShots();
            GoogleWorkTick(nowUtc);
            redraw |= EdgeTick();
            redraw |= RemindTick(nowUtc);
            RemindSoundTick();
            ThemeTick(nowUtc);
            MemoryWatch();
            DropSpareGlass();
            ProfTick();
            MiniClock();
            // Permission cards carry a live countdown
            if (approvals.Count > 0) redraw = true;
            // Panel texts change by the minute
            if (redraw || (expanded && minute) || DesiredView() != current) Kick();
        }

        bool unplugWarned;

        void ReadPower()
        {
            bool hb, ac; int level;
            Native.ReadPower(out hb, out ac, out level);
            if (!powerKnown)
            {
                powerKnown = true;
                hasBattery = hb; onAc = ac; battery = level;
                return;
            }
            bool acChanged = ac != onAc;
            int prev = battery;
            hasBattery = hb; onAc = ac; battery = level;
            if (!hb) return;
            if (!ac) unplugWarned = false;
            // Battery care: unplug at the chosen level
            int unplugAt = settings.GetInt("battery.unplug", 0);
            bool enough = ac && !acChanged && unplugAt > 0 && level >= unplugAt && prev < unplugAt && !unplugWarned;
            Toast t = null;
            if (acChanged) t = ShowToast(View.Power, 3.2, 2, false);
            else if (enough) { unplugWarned = true; t = ShowToast(View.Power, 6, 2, false); }
            else if (ac && level >= 100 && prev < 100) t = ShowToast(View.Power, 4, 2, false);
            else if (!ac && ((prev > 20 && level <= 20) || (prev > 10 && level <= 10) || (prev > 5 && level <= 5))) t = ShowToast(View.Power, 5, 2, false);
            if (t != null)
            {
                t.Flag = ac;
                t.Data = acChanged ? (ac ? "plug" : "unplug") : enough ? "enough" : ac ? "full" : "low";
                batteryFill.Snap(0);
                batteryFill.Target = battery / 100f;
            }
            Kick();
        }

        // Tray

        void BuildTray()
        {
            // Rebuilt when the language changes
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (menu != null) menu.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
            menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
            MenuLook.Apply(menu);
            var title = new ToolStripMenuItem("Deskmate") { Enabled = false };
            var kuliaItem = new ToolStripMenuItem("Kulia");
            var hookItem = new ToolStripMenuItem(Lang.T("Hubungkan agen"));
            var dataItem = new ToolStripMenuItem(Lang.T("Buka folder data"), null, delegate
            {
                try { Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); } catch (Exception ex) { Log.Write("open dir: " + ex.Message); }
            });
            var updateItem = new ToolStripMenuItem(Lang.T("Perbarui"), null, delegate { BeginInvoke(new Action(() => InstallUpdate(this))); }) { Visible = false };
            var hideItem = new ToolStripMenuItem(Lang.T("Sembunyikan island"), null, delegate { ToggleHidden(); }) { ShortcutKeyDisplayString = HotkeyText(HideHotkey) };
            var taskbarItem = TaskbarPeekMenuItem();
            var taskbarHideItem = new ToolStripMenuItem(Lang.T("Sembunyikan taskbar Windows"), null, delegate { EndTaskbarPeek(); });
            var keysItem = new ToolStripMenuItem(Lang.T("Navigasi keyboard"), null, delegate { EnterKeyboard(); }) { ShortcutKeyDisplayString = HotkeyText(KeysHotkey) };
            trayComboItem = new ToolStripMenuItem(Lang.T("Combo Ketikan"), null, delegate { ToggleCombo(); }) { Checked = ComboOn };
            var miniItem = new ToolStripMenuItem(Lang.T("Mini"), null, delegate { ToggleMini(); }) { ShortcutKeyDisplayString = HotkeyText(MiniHotkey) };
            var prompterItem = new ToolStripMenuItem(Lang.T("Teleprompter"), null, delegate { if (!PrompterOn) OpenPrompter(); });
            var focusItem = new ToolStripMenuItem(Lang.T("Timer"));
            var workItem = new ToolStripMenuItem(Lang.T("Tugas dan catatan"), null, delegate { OpenWork(); }) { ShortcutKeyDisplayString = HotkeyText(WorkHotkey) };
            var noteItem = new ToolStripMenuItem(Lang.T("Catatan kilat"), null, delegate { OpenNote(); }) { ShortcutKeyDisplayString = HotkeyText(NoteHotkey) };
            var rapatItem = new ToolStripMenuItem(Lang.T("Rekam rapat"), null, delegate { ToggleRapat(); });
            var suruhItem = new ToolStripMenuItem(Lang.T("Suruh Deskmate…"), null, delegate { BeginInvoke(new Action(OpenSuruh)); });
            var mirrorItem = new ToolStripMenuItem(Lang.T("Cermin kamera"), null, delegate { ToggleMirror(); }) { ShortcutKeyDisplayString = HotkeyText(MirrorHotkey) };
            var pickerItem = new ToolStripMenuItem(Lang.T("Ambil warna"), null, delegate { BeginInvoke(new Action(OpenPicker)); });
            var screenTextItem = new ToolStripMenuItem(Lang.T("Salin teks dari layar"), null, delegate { BeginInvoke(new Action(ReadScreenText)); });
            var exit = new ToolStripMenuItem(Lang.T("Keluar"), null, delegate { Close(); });
            var prefsItem = new ToolStripMenuItem(Lang.T("Pengaturan…"), null, delegate { OpenPrefs(); });
            menu.Items.AddRange(new ToolStripItem[] { title, new ToolStripSeparator(), updateItem, miniItem, hideItem, taskbarItem, taskbarHideItem, keysItem, trayComboItem, focusItem, workItem, noteItem, suruhItem, rapatItem, mirrorItem, pickerItem, screenTextItem, prompterItem, hookItem, kuliaItem, new ToolStripSeparator(), prefsItem, dataItem, exit });
            menu.Opening += delegate
            {
                // Style submenus after they're rebuilt
                BeginInvoke(new Action(delegate { MenuLook.Apply(menu); }));
                // Shortcuts may have been remapped
                hideItem.ShortcutKeyDisplayString = HotkeyText(HideHotkey);
                keysItem.ShortcutKeyDisplayString = HotkeyText(KeysHotkey);
                miniItem.ShortcutKeyDisplayString = HotkeyText(MiniHotkey);
                noteItem.ShortcutKeyDisplayString = HotkeyText(NoteHotkey);
                workItem.ShortcutKeyDisplayString = HotkeyText(WorkHotkey);
                rapatItem.Text = RapatOn ? Lang.T("Hentikan rekaman rapat") : Lang.T("Rekam rapat");
                rapatItem.Enabled = !rapatWriting;
                rapatItem.ShortcutKeyDisplayString = HotkeyText(RapatHotkey);
                suruhItem.ShortcutKeyDisplayString = HotkeyText(SuruhHotkey);
                mirrorItem.ShortcutKeyDisplayString = HotkeyText(MirrorHotkey);
                pickerItem.ShortcutKeyDisplayString = HotkeyText(PickerHotkey);
                screenTextItem.ShortcutKeyDisplayString = HotkeyText(ScreenTextHotkey);
                hideItem.Checked = hiddenByUser;
                miniItem.Checked = MiniWanted();
                taskbarItem.Visible = DockBarOn && settings.GetBool("dockbar.replace", false);
                taskbarHideItem.Visible = taskbarItem.Visible && Taskbar.Paused;
                trayComboItem.Checked = ComboOn;
                prompterItem.Checked = PrompterOn;
                // Running script: controls live here
                prompterItem.DropDownItems.Clear();
                if (PrompterOn)
                    prompterItem.DropDownItems.AddRange(new ToolStripItem[]
                    {
                        new ToolStripMenuItem(Lang.T(prompterPaused ? "Lanjutkan" : "Jeda"), null, delegate { TogglePrompter(); }) { ShortcutKeyDisplayString = HotkeyText(PrompterHotkey) },
                        new ToolStripMenuItem(Lang.T("Ulang dari awal"), null, delegate { RestartPrompter(); }) { ShortcutKeyDisplayString = HotkeyText(PrompterRestart) },
                        new ToolStripMenuItem(Lang.T("Mundur 5 detik"), null, delegate { SeekPrompter(-5); }) { ShortcutKeyDisplayString = HotkeyText(PrompterBack) },
                        new ToolStripMenuItem(Lang.T("Maju 5 detik"), null, delegate { SeekPrompter(5); }) { ShortcutKeyDisplayString = HotkeyText(PrompterAhead) },
                        new ToolStripMenuItem(Lang.T("Atur…"), null, delegate { BeginInvoke(new Action(OpenPrompter)); }) { ShortcutKeyDisplayString = HotkeyText(PrompterSetupKey) },
                        new ToolStripMenuItem(Lang.T("Lebih cepat"), null, delegate { NudgeSpeed(1); }) { ShortcutKeyDisplayString = HotkeyText(PrompterFaster) },
                        new ToolStripMenuItem(Lang.T("Lebih lambat"), null, delegate { NudgeSpeed(-1); }) { ShortcutKeyDisplayString = HotkeyText(PrompterSlower) },
                        new ToolStripSeparator(),
                        new ToolStripMenuItem(Lang.T("Hentikan"), null, delegate { StopPrompter(); }) { ShortcutKeyDisplayString = HotkeyText(PrompterStop) },
                    });
                FillFocusMenu(focusItem);
                updateItem.Visible = newVersion != null;
                if (newVersion != null) updateItem.Text = newRebuild && newAsset != null ? Lang.T("Perbarui Deskmate") : Lang.F(newAsset != null ? Lang.T("Perbarui ke Deskmate {0}") : Lang.T("Unduh Deskmate {0}"), newVersion);
                CheckLinks();
                hookItem.DropDownItems.Clear();
                hookItem.DropDownItems.Add(new ToolStripMenuItem("Claude Code" + (hookProfiles > 0 ? "" : Lang.T(" · tidak ditemukan")), null, delegate { ToggleHooks(); })
                {
                    Checked = hooksOn,
                    Enabled = hookProfiles > 0
                });
                foreach (var agentId in AgentSetup.Ids)
                {
                    string id = agentId;
                    bool present = AgentSetup.Present(id);
                    hookItem.DropDownItems.Add(new ToolStripMenuItem(AgentSetup.Label(id) + (present ? "" : Lang.T(" · tidak ditemukan")), null, delegate { ToggleAgent(id); })
                    {
                        Checked = present && linked.Contains(AgentSetup.Label(id)),
                        Enabled = present
                    });
                }
                kuliaItem.DropDownItems.Clear();
                if (kulia != null && kulia.SignedIn)
                {
                    kuliaItem.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Masuk sebagai ") + kulia.Email) { Enabled = false });
                    kuliaItem.DropDownItems.Add(Lang.T("Perbarui tugas"), null, delegate { RefreshTasks(true); });
                    kuliaItem.DropDownItems.Add(Lang.T("Keluar dari Kulia"), null, delegate { SignOutKulia(); });
                }
                else kuliaItem.DropDownItems.Add(Lang.T("Masuk ke Kulia…"), null, delegate { OpenLogin(); });
            };
            trayIcon = KitWindow.LoadAppIcon(SystemInformation.SmallIconSize);
            tray = new NotifyIcon { Icon = trayIcon, Text = "Deskmate", ContextMenuStrip = menu, Visible = false };
            tray.MouseClick += (s, e) => { if (e.Button != MouseButtons.Left) return; OpenPrefs(); };
            tray.Visible = true;
        }



        // Rebuilt each time the menu opens
        void FillFocusMenu(ToolStripMenuItem focusItem)
        {
            focusItem.DropDownItems.Clear();
            focusItem.Checked = TimerOn;
            foreach (var m in new[] { 25, 50 })
            {
                int minutes = m;
                focusItem.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Fokus ") + minutes + Lang.T(" menit"), null, delegate { StartTimer(TimerKind.Focus, minutes); }));
            }
            focusItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var m in new[] { 5, 10, 15 })
            {
                int minutes = m;
                focusItem.DropDownItems.Add(new ToolStripMenuItem(Lang.F("Timer {0} menit", minutes), null, delegate { StartTimer(TimerKind.Countdown, minutes); }));
            }
            focusItem.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Stopwatch"), null, delegate { StartTimer(TimerKind.Stopwatch, 0); }));
            focusItem.DropDownItems.Add(new ToolStripSeparator());
            focusItem.DropDownItems.Add(new ToolStripMenuItem(timerPaused ? Lang.T("Lanjutkan") : Lang.T("Jeda"), null, delegate { ToggleTimerPause(); }) { Enabled = TimerOn });
            focusItem.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Hentikan"), null, delegate { StopTimer(); }) { Enabled = TimerOn });
        }
    }
}
