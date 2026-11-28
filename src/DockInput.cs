using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class DockWindow
        {
            string hoverId, pressId;
            Point downAt;
            // Reorder: the dragged app, its grab point, and the gaps it opens
            DockApp dragApp;
            PointF dragGrab;
            bool dragOut;
            int dragTo = -1;
            sealed class Gap { public int At; public readonly Spring Show = new Spring(0, 1, 1, 0.002f); }
            readonly List<Gap> gaps = new List<Gap>();
            // Tray pointer state
            TrayIcon trayHot;
            bool trayPopup;
            string lastTrayKey = "";
            DateTime lastTrayUtc = DateTime.MinValue;
            DateTime trayOpenUtc = DateTime.MinValue;
            IntPtr trayFrontHwnd = IntPtr.Zero;
            readonly Timer trayPop = new Timer { Interval = 500 };
            // File drag over an app brings its window up
            string dropHover;
            DateTime dropSinceUtc;

            bool dragging { get { return dragApp != null; } }
            bool PreviewsOn { get { return S.GetBool("dockbar.previews", true); } }
            bool TrayExpanded { get { return card != null && card.Kind == DockCard.TrayKind && !card.Leaving; } }

            bool ChevContainsScreen(Point pt)
            {
                if (scale <= 0 || cw <= 0 || ch <= 0) return false;
                var o = WindowOrigin();
                if (pt.X < o.X || pt.Y < o.Y || pt.X >= o.X + cw || pt.Y >= o.Y + ch) return false;
                if (HitAt(new PointF((pt.X - o.X) / scale, (pt.Y - o.Y) / scale)) == "chev") return true;
                foreach (var kv in hits)
                    if (kv.Value == "chev")
                    {
                        var r = ToScreen(kv.Key);
                        r.Inflate(2, 2);
                        return r.Contains(pt);
                    }
                return false;
            }

            string HitAt(PointF pt)
            {
                for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Key.Contains(pt)) return hits[i].Value;
                return null;
            }

            readonly DockApp workSlotApp = new DockApp { Key = "sys:work", Pinned = true };
            readonly DockApp suruhSlotApp = new DockApp { Key = "sys:suruh", Pinned = true };

            DockApp AppById(string id)
            {
                if (id == null || !id.StartsWith("app:", StringComparison.Ordinal)) return null;
                string key = id.Substring(4);
                return apps.Find(a => a.Key == key && !a.Leaving);
            }

            DockApp PreviewAppById(string id)
            {
                if (id == "sys:work")
                {
                    if (S == null || !S.GetBool("dockbar.work", true) || !isle.WorkOpen || isle.WorkHwnd == IntPtr.Zero) return null;
                    workSlotApp.Name = Lang.T("Tugas dan catatan");
                    workSlotApp.Wins.Clear();
                    var w = watch.Find(isle.WorkHwnd) ?? new DockWin { H = isle.WorkHwnd, Title = workSlotApp.Name };
                    workSlotApp.Wins.Add(w);
                    return workSlotApp;
                }
                if (id == "sys:suruh")
                {
                    if (S == null || !S.GetBool("dockbar.suruh", true) || !isle.SuruhOpen || isle.SuruhHwnd == IntPtr.Zero) return null;
                    suruhSlotApp.Name = Lang.T("Suruh Deskmate");
                    suruhSlotApp.Wins.Clear();
                    var w = watch.Find(isle.SuruhHwnd) ?? new DockWin { H = isle.SuruhHwnd, Title = suruhSlotApp.Name };
                    suruhSlotApp.Wins.Add(w);
                    return suruhSlotApp;
                }
                return AppById(id);
            }

            TrayIcon TrayById(string id)
            {
                if (id == null || !id.StartsWith("tray:", StringComparison.Ordinal)) return null;
                return trayIcons.Find(x => "tray:" + x.Key == id);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var pt = new PointF(e.X / scale, e.Y / scale);
                mouseX = pt.X;
                mouseY = pt.Y;
                if (!inside)
                {
                    inside = true;
                    hover.Target = 1;
                    hideLater.Stop();
                }
                HoverReveal();
                if (pressId != null && e.Button == MouseButtons.Left && dragApp == null && AppById(pressId) != null && Far(e.Location, downAt)) BeginDrag(AppById(pressId), pt);
                if (dragApp != null) { DragMove(pt); Animate(); return; }
                if (pressId == "sys:shelf" && e.Button == MouseButtons.Left && Held.Count > 0 && Far(e.Location, downAt)) { ShelfDragOut(); return; }
                string id = HitAt(pt);
                if (card != null && card.Kind == DockCard.WindowsKind && PreviewAppById(id) == card.App) card.Keep();
                if (id != hoverId) SetHover(id);
                TrayMove(id);
                Animate();
            }

            bool Far(Point a, Point b) { return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 7 * scale; }

            void SetHover(string id)
            {
                hoverId = id;
                bool label = id != null && (id.StartsWith("app:", StringComparison.Ordinal) || id.StartsWith("sys:", StringComparison.Ordinal) || id.StartsWith("tray:", StringComparison.Ordinal));
                var app = PreviewAppById(id);
                bool previewing = card != null && card.Kind == DockCard.WindowsKind && app != null && card.App == app;
                labelShow.Target = label && !previewing ? 1 : 0;
                dwell.Stop();
                if (app != null && app.Running && PreviewsOn && sheet == null && !TrayExpanded)
                {
                    // Once one preview is up, the next app shows at once
                    if (card != null && card.Kind == DockCard.WindowsKind) ShowPreview(app);
                    else dwell.Start();
                }
                else if (card != null && card.Kind == DockCard.WindowsKind) card.CloseSoon();
            }

            void Dwell()
            {
                if (sheet != null || TrayExpanded) return;
                var app = PreviewAppById(hoverId);
                if (app != null && app.Running && inside && dragApp == null) ShowPreview(app);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                CancelHoverReveal();
                if (dragApp != null) return;
                inside = false;
                hover.Target = 0;
                pressId = null;
                dwell.Stop();
                SetHover(null);
                TrayLeave();
                if (card != null && card.Kind == DockCard.WindowsKind) card.CloseSoon();
                if (HideMode != "never") { hideLater.Stop(); hideLater.Start(); }
                Animate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                string id = HitAt(new PointF(e.X / scale, e.Y / scale));
                if (TrayExpanded && id != "chev") CloseCard();
                string wasPress = pressId;
                pressId = id;
                downAt = e.Location;
                dwell.Stop();
                // A deliberate click skips the wait
                CancelHoverReveal();
                Reveal(true);
                if (id != null && id.StartsWith("sys:", StringComparison.Ordinal))
                {
                    var sp = SysPress(id.Substring(4));
                    if (Anim.Reduced) sp.Snap(1f);
                    else { Tune(sp, 520, 0.85f); sp.Target = 1f; }
                }
                if (id != null && id.StartsWith("tray:", StringComparison.Ordinal)) TrayDown(id, e.Button);
                if (pressId != wasPress || pressId != null) Animate();
                Render();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                var pt = new PointF(e.X / scale, e.Y / scale);
                if (dragApp != null) { EndDrag(); pressId = null; Animate(); return; }
                string id = HitAt(pt);
                Action a;
                if (id != null && id.StartsWith("tray:", StringComparison.Ordinal)) TrayUp(id, e.Button);
                else if (e.Button == MouseButtons.Right && id != null && menus.TryGetValue(id, out a)) Run(a, id);
                else if (e.Button == MouseButtons.Right) DockMenu();
                else if (e.Button == MouseButtons.Left && id != null && id == pressId && acts.TryGetValue(id, out a)) Run(a, id);
                else if (e.Button == MouseButtons.Middle && id != null && id == pressId)
                {
                    var app = AppById(id);
                    if (app != null) Launch(app, false);
                }
                if (pressId != null) { pressId = null; Animate(); }
                else pressId = null;
                Render();
            }

            static void Run(Action a, string id)
            {
                try { a(); } catch (Exception ex) { Log.Write("dock " + id + ": " + ex.Message); }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (hoverId != "status") return;
                float step = e.Delta > 0 ? 0.02f : -0.02f;
                isle.volume.Set(Ease.Clamp01(isle.vol + step));
            }

            internal void FocusWindow(DockApp app, IntPtr window)
            {
                if (WindowMotionEnabled && (GetForegroundWindow() != window || ShellApp.IsMinimized(window)))
                {
                    WindowFeedback(app, true, DateTime.UtcNow);
                    QueueOpeningOutline(window);
                }
                ShellApp.FocusDockApp(window);
            }

            internal void MinimizeWindow(IntPtr window)
            {
                if (WindowMotionEnabled) DockWindowFrame.Update(watch.Find(window));
                ShellApp.MinimizeFromDock(window);
            }

            internal bool CloseWindow(IntPtr window)
            {
                if (WindowMotionEnabled) DockWindowFrame.Update(watch.Find(window));
                if (!ShellApp.Close(window)) return false;
                closingFromDock = window;
                closeRequestedUtc = DateTime.UtcNow;
                return true;
            }

            // The Dock never takes focus. Clicking the front app minimizes it; menus only focus or open.
            void OpenApp(DockApp a, bool fromCard, bool minimizeFront)
            {
                bool shift = (ModifierKeys & Keys.Shift) != 0, ctrl = (ModifierKeys & Keys.Control) != 0;
                if (!shift && !a.Running && (DateTime.UtcNow - a.LaunchUtc).TotalMilliseconds < 650) return;
                if (!a.Running || shift) { Launch(a, shift && ctrl); return; }
                IntPtr fg = GetForegroundWindow();
                var live = a.Wins.FindAll(w => ShellApp.WindowExists(w.H));
                if (live.Count == 0) { Launch(a, false); return; }
                int at = live.FindIndex(w => w.H == fg);
                if (minimizeFront && at >= 0 && !ShellApp.IsMinimized(fg))
                {
                    MinimizeWindow(fg);
                    if (!fromCard) CloseCard();
                    return;
                }
                var recent = a.Recent();
                var next = at >= 0 ? live[(at + 1) % live.Count] :
                    recent != null && live.Contains(recent) ? recent : live[0];
                if (next != null)
                {
                    if (!WindowMotionEnabled)
                    {
                        a.PulseUtc = DateTime.UtcNow;
                        a.Grow.Velocity += 2.2f;
                        Animate();
                    }
                    FocusWindow(a, next.H);
                }
                if (!fromCard) CloseCard();
            }

            void Launch(DockApp a, bool admin)
            {
                string target = a.Target.Length > 0 ? a.Target : a.Exe;
                if (target.Length == 0) return;
                ShellApp.Launch(target, admin);
                LaunchFeedback(a);
            }

            void WorkMenu()
            {
                var m = new ContextMenuStrip();
                m.Items.Add(new ToolStripMenuItem(Lang.T("Tugas dan catatan")) { Enabled = false, Tag = new MenuItemMeta { Header = true } });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem(Lang.T("Tugas"), null, delegate { isle.OpenWork(0); }) { Tag = new MenuItemMeta { Glyph = '\uE762' } });
                m.Items.Add(new ToolStripMenuItem(Lang.T("Catatan"), null, delegate { isle.OpenWork(1); }) { Tag = new MenuItemMeta { Glyph = '\uE70B' } });
                m.Items.Add(new ToolStripMenuItem(Lang.T("Lepas dari Dock"), null, delegate { isle.SetFlag("dockbar.work", false); if (isle.prefs != null && !isle.prefs.IsDisposed) isle.prefs.Relayout(); }) { Tag = new MenuItemMeta { Glyph = '\uE77A' } });
                if (isle.WorkOpen && isle.WorkHwnd != IntPtr.Zero)
                {
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(new ToolStripMenuItem(Lang.T("Tutup jendela"), null, delegate { CloseWindow(isle.WorkHwnd); }) { Tag = new MenuItemMeta { Glyph = '\uE8BB' } });
                }
                ShowMenu(m);
            }

            void OpenSuruhFromDock()
            {
                CloseCard();
                if (isle.SuruhOpen && isle.SuruhHwnd != IntPtr.Zero && GetForegroundWindow() == isle.SuruhHwnd && !ShellApp.IsMinimized(isle.SuruhHwnd))
                {
                    MinimizeWindow(isle.SuruhHwnd);
                    return;
                }
                isle.OpenSuruh();
            }

            void SuruhMenu()
            {
                var m = new ContextMenuStrip();
                m.Items.Add(new ToolStripMenuItem(Lang.T("Suruh Deskmate")) { Enabled = false, Tag = new MenuItemMeta { Header = true } });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(new ToolStripMenuItem(Lang.T("Buka obrolan"), null, delegate { isle.OpenSuruh(); }) { Tag = new MenuItemMeta { Glyph = '\uE8F2' } });
                m.Items.Add(new ToolStripMenuItem(Lang.T("Obrolan baru"), null, delegate { isle.StartFreshSuruh(); }) { Enabled = !isle.suruhBusy, Tag = new MenuItemMeta { Glyph = '\uE710' } });
                m.Items.Add(new ToolStripMenuItem(Lang.T("Pengaturan AI"), null, delegate { isle.OpenPrefs("ai"); }) { Tag = new MenuItemMeta { Glyph = '\uE713' } });
                m.Items.Add(new ToolStripMenuItem(Lang.T("Lepas dari Dock"), null, delegate { isle.SetFlag("dockbar.suruh", false); if (isle.prefs != null && !isle.prefs.IsDisposed) isle.prefs.Relayout(); }) { Tag = new MenuItemMeta { Glyph = '\uE77A' } });
                if (isle.SuruhOpen && isle.SuruhHwnd != IntPtr.Zero)
                {
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add(new ToolStripMenuItem(Lang.T("Tutup jendela"), null, delegate { CloseWindow(isle.SuruhHwnd); }) { Tag = new MenuItemMeta { Glyph = '\uE8BB' } });
                }
                ShowMenu(m);
            }

            internal void AppMenu(DockApp a)
            {
                if (a == null || closing) return;
                CloseCard();
                if (sheet is AppSheet && ((AppSheet)sheet).App == a)
                {
                    SheetDone(sheet);
                    return;
                }
                if (sheet != null) SheetDone(sheet);
                labelShow.Target = 0;
                Reveal(true);
                var m = new AppSheet(isle, this, a);
                sheet = m;
                Rectangle useMon = mon.IsEmpty ? Screen.PrimaryScreen.Bounds : mon;
                float sc = scale > 0 ? scale : 1f;
                int barTop = (!mon.IsEmpty && !area.IsEmpty) ? BarTopShownScreen() : useMon.Bottom - (int)Math.Round((BarH + Edge) * sc);
                Rectangle anchor = IconScreenRect(a);
                if (anchor.IsEmpty) anchor = new Rectangle(useMon.Left + useMon.Width / 2, barTop, 1, 1);
                m.OpenAt(anchor, Rectangle.Empty, useMon, barTop, sc);
                // Clicking the same foreground app may not deactivate the sheet.
                MenuGuard.Watch(m, pt => m.Bounds.Contains(pt), () => SheetDone(m), Dry);
            }

            internal AppSheet CurrentAppSheet { get { return sheet as AppSheet; } }
            internal void OpenAppMenu(DockApp a) { AppMenu(a); }
            public void LaunchApp(DockApp a, bool admin) { Launch(a, admin); }
            internal void FocusOrOpenApp(DockApp a) { OpenApp(a, false, false); }
            internal void PinApp(DockApp a) { Pin(a, -1); }
            internal void UnpinApp(DockApp a) { Unpin(a); }
            internal void EndApp(DockApp a) { EndDockApp(a); }
            internal void RevealFile(string file) { ShowInFolder(file); }
            internal string WindowTitle(IntPtr h) { return watch.Title(h); }
            internal void RescanApps() { watch.Rescan(); Refresh(false); Render(); }
            internal void ShowCopiedToast(string file) { if (isle != null) isle.ShowInfo('\uE8C8', Lang.T("Jalur disalin"), Path.GetFileName(file), true); }

            public Bitmap IconFor(string key)
            {
                Bitmap b;
                return !string.IsNullOrEmpty(key) && icons.TryGetValue(IconCacheKey(key), out b) ? b : null;
            }

            void EndDockApp(DockApp a)
            {
                var wins = new List<DockWin>(a.Wins);
                var status = ShellApp.EndTask(wins);
                if (status == ShellApp.EndTaskStatus.PartialDenied)
                {
                    if (isle != null) isle.ShowInfo(Ink.IcoLock, Lang.T("Tidak dapat mengakhiri proses"), Lang.T("Butuh izin administrator"), true);
                }
                else
                {
                    foreach (var w in wins) watch.Drop(w.H);
                    Refresh(false);
                    Render();
                }
            }

            static void ShowInFolder(string file)
            {
                try { Process.Start("explorer.exe", "/select,\"" + file + "\""); }
                catch (Exception ex) { Log.Write("dock reveal: " + ex.Message); }
            }

            void ClockMenu()
            {
                var m = new ContextMenuStrip();
                m.Items.Add(Lang.T("Notifikasi dan kalender"), null, delegate { ToggleSheet("cal"); });
                m.Items.Add(Lang.T("Atur tanggal dan waktu"), null, delegate { Open("ms-settings:dateandtime"); });
                ShowMenu(m);
            }

            void DockMenu()
            {
                var m = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };
                m.Items.Add(Lang.T("Pengaturan Dock"), null, delegate { isle.OpenPrefs("dock"); });
                var combo = new ToolStripMenuItem(Lang.T("Combo Ketikan"), null, delegate { isle.ToggleCombo(); }) { Checked = isle.ComboOn };
                m.Items.Add(combo);
                m.Items.Add(Lang.T("Task Manager"), null, delegate { Open("taskmgr.exe"); });
                if (S != null && S.GetBool("dockbar.replace", false))
                {
                    m.Items.Add(Lang.T("Ikon di pojok taskbar"), null, delegate { Open("ms-settings:taskbar"); });
                    m.Items.Add(isle.TaskbarPeekMenuItem());
                    if (Taskbar.Paused)
                        m.Items.Add(Lang.T("Sembunyikan taskbar Windows"), null, delegate { isle.EndTaskbarPeek(); });
                }
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Lang.T("Tampilkan desktop"), null, delegate { ShowDesktop(); });
                ShowMenu(m);
            }

            static void Open(string what)
            {
                try { Process.Start(new ProcessStartInfo(what) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write("dock open " + what + ": " + ex.Message); }
            }

            void ShowMenu(ContextMenuStrip m)
            {
                CloseCard();
                labelShow.Target = 0;
                MenuLook.Apply(m);
                m.Closed += delegate
                {
                    foreach (ToolStripItem item in m.Items)
                    {
                        if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
                    }
                    BeginInvoke(new Action(m.Dispose));
                };
                MenuGuard.Watch(m);
                m.Show(Cursor.Position);
            }

            void Pin(DockApp a, int at)
            {
                if (isle == null) return;
                string target = a.Target.Length > 0 ? a.Target : a.Exe;
                if (target.Length == 0) return;
                var pins = isle.DockPins();
                pins.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                if (at < 0 || at > pins.Count) pins.Add(target);
                else pins.Insert(at, target);
                isle.SetDockPins(pins);
            }

            void Unpin(DockApp a)
            {
                if (!a.Running)
                {
                    var s = slots.Find(x => x.App == a);
                    if (s != null) ShatterDockIcon(a, Glyph(s.Icon));
                    Animate();
                }
                if (isle == null) return;
                var pins = isle.DockPins();
                pins.RemoveAll(x => string.Equals(x, a.Target, StringComparison.OrdinalIgnoreCase));
                isle.SetDockPins(pins);
            }

            void ShowPreview(DockApp app)
            {
                if (closing || !shown || dragApp != null || sheet != null || TrayExpanded) return;
                var at = IconScreenRect(app);
                if (at.IsEmpty) return;
                if (card != null && card.Kind != DockCard.WindowsKind) CloseCard();
                if (card == null) card = new DockCard(isle, this, DockCard.WindowsKind);
                labelShow.Target = 0;
                card.ShowApp(app, at, mon, BarTopScreen(), scale);
            }

            void ToggleTrayCard()
            {
                bool wasTray = TrayExpanded;
                if (sheet != null) SheetDone(sheet);
                CloseCard();
                if (wasTray) return;
                labelShow.Target = 0;
                Reveal(true);
                TrayPromo.Invalidate();
                double o0 = clock.Elapsed.TotalMilliseconds;
                var list = OverflowTray();
                if (list.Count == 0) { Render(); return; }
                trayOpenUtc = DateTime.UtcNow;
                trayFrontHwnd = GetForegroundWindow();
                double o1 = clock.Elapsed.TotalMilliseconds;
                var c = new DockCard(isle, this, DockCard.TrayKind);
                card = c;
                double o2 = clock.Elapsed.TotalMilliseconds;
                c.ShowTray(list, HitScreenRect("chev"), mon, BarTopShownScreen(), scale);
                if (fpsLog) Log.Write("dock open tray " + (clock.Elapsed.TotalMilliseconds - o0).ToString("0.0") + "ms list=" + (o1 - o0).ToString("0.0") + " make=" + (o2 - o1).ToString("0.0") + " show=" + (clock.Elapsed.TotalMilliseconds - o2).ToString("0.0") + " icons=" + list.Count);
                MenuGuard.Watch(c, pt => (!c.IsDisposed && c.Bounds.Contains(pt)) || ChevContainsScreen(pt), delegate
                {
                    if (card == c) CloseCard();
                }, Dry);
                Render();
            }

            public void CardDone(DockCard c)
            {
                if (c == card) CloseCard();
                else if (!c.IsDisposed) c.Dismiss();
            }
        }
    }
}
