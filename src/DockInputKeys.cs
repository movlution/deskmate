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
            // Pointer on a card keeps an auto-hidden dock up
            public void HoldOpen(bool on)
            {
                if (on) hideLater.Stop();
                else if (HideMode != "never" && !inside) { hideLater.Stop(); hideLater.Start(); }
            }

            public Bitmap TrayPicture(TrayIcon ic)
            {
                var owner = TrayOwner();
                return owner == null ? null : owner.tray.Picture(ic);
            }

            GlassSheet sheet;

            // One card; its button toggles it
            void ToggleSheet(string kind)
            {
                if (sheet != null)
                {
                    bool same = sheet.Kind == kind;
                    SheetDone(sheet);
                    if (same) return;
                }
                CloseCard();
                labelShow.Target = 0;
                // Auto-hidden: the dock comes up under its card
                Reveal(true);
                if (kind == "start" && !Anim.Reduced)
                {
                    SysSpring("start").Velocity += 1.8f;
                    startPulseUtc = DateTime.UtcNow;
                    Animate();
                }
                double o0 = clock.Elapsed.TotalMilliseconds;
                sheet = MakeSheet(kind);
                double o1 = clock.Elapsed.TotalMilliseconds;
                sheet.OpenAt(HitScreenRect(SheetAnchor(kind)), Full ? Rectangle.Empty : ToScreen(centerPill), mon, BarTopShownScreen(), scale);
                if (fpsLog) Log.Write("dock open " + kind + " " + (clock.Elapsed.TotalMilliseconds - o0).ToString("0.0") + "ms make=" + (o1 - o0).ToString("0.0") + " open=" + (clock.Elapsed.TotalMilliseconds - o1).ToString("0.0"));
            }

            GlassSheet MakeSheet(string kind)
            {
                switch (kind)
                {
                    case "quick": return new QuickSheet(isle, this);
                    case "cal": return new CalendarSheet(isle, this);
                    case "weather": return new WeatherSheet(isle, this);
                    default: return new StartCard(isle, this);
                }
            }

            static string SheetAnchor(string kind)
            {
                switch (kind)
                {
                    case "quick": return "status";
                    case "cal": return "clock";
                    case "weather": return "w_weather";
                    default: return "sys:start";
                }
            }

            public void SheetDone(GlassSheet s)
            {
                bool wasStart = s != null && s.Kind == "start";
                if (s == sheet) sheet = null;
                MenuGuard.Unwatch(s);
                if (!s.IsDisposed) s.Dismiss();
                if (wasStart && !closing) Render();
                if (sheet == null && !closing && HideMode != "never" && !inside) { hideLater.Stop(); hideLater.Start(); }
            }

            StartKey startKey;

            void StartKeys()
            {
                if (startKey != null) return;
                startKey = new StartKey();
                startKey.Pressed += what =>
                {
                    try { BeginInvoke(new Action(delegate { KeyAction(what); })); } catch (Exception) { }
                };
                startKey.Start();
            }

            // StartKey.Start waits on its helper thread. Measured from a free caller that
            // costs ~80 ms, but when the UI thread is inside the first paint the helper
            // cannot finish and the wait runs to its full 2 s timeout. So start it after
            // the frame is out: the dock draws first, the Win key follows a beat later.
            bool keysPosted;

            void PostKeys()
            {
                if (keysPosted || startKey != null || closing) return;
                keysPosted = true;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        keysPosted = false;
                        if (closing) return;
                        if (globalOwner && Replacing && !Dry && S.GetBool("dockbar.winkey", true)) StartKeys();
                        // First Start open waited ~0.9s for the catalog and ~0.9s for icons; warm both in the background
                        if (globalOwner && !Dry) StartCard.Prewarm(this, isle.settings.Get("start.pins", null), Math.Max(32, (int)Math.Ceiling(40 * scale)));
                    }));
                }
                catch (InvalidOperationException) { keysPosted = false; }
            }

            void StopKeys()
            {
                if (startKey == null) return;
                startKey.Stop();
                startKey = null;
            }

            // Key from the hook to a card
            void KeyAction(string what)
            {
                if (closing) return;
                if (what == "menu") { QuickMenuAtStart(); return; }
                if (what == "dockkeys") { KeyNav(); return; }
                if (what == "search")
                {
                    if (sheet is StartCard) { ShellApp.Activate(sheet.Handle); return; }
                    if (sheet != null) SheetDone(sheet);
                    what = "start";
                }
                ToggleSheet(what);
            }

            // Win+T: the dock by keyboard, like the taskbar
            bool keyMode;
            string keySel;
            IntPtr keyPrev;

            [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);

            void KeyNav()
            {
                if (keyMode) { ExitKeys(true); return; }
                var ids = NavIds();
                if (ids.Count == 0) return;
                CloseCard();
                if (sheet != null) SheetDone(sheet);
                Reveal(true);
                keyPrev = GetForegroundWindow();
                // Starts on the first app, as the taskbar does
                keySel = ids.Find(x => !x.StartsWith("sys:", StringComparison.Ordinal)) ?? ids[0];
                keyMode = true;
                SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) & ~Native.WS_EX_NOACTIVATE);
                ShellApp.Activate(Handle);
                labelShow.Target = 1;
                Animate();
            }

            void ExitKeys(bool restore)
            {
                if (!keyMode) return;
                keyMode = false;
                keySel = null;
                labelShow.Target = 0;
                SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) | Native.WS_EX_NOACTIVATE);
                if (restore && keyPrev != IntPtr.Zero) ShellApp.Activate(keyPrev);
                if (HideMode != "never" && !inside) { hideLater.Stop(); hideLater.Start(); }
                Animate();
            }

            List<string> NavIds()
            {
                var ids = new List<string>();
                foreach (var s in slots)
                {
                    if (s.Sep || s.Id == "gap" || s.Show < 0.5f) continue;
                    ids.Add(s.App == null ? "sys:" + s.Id : s.Id);
                }
                return ids;
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys key)
            {
                if (!keyMode) return base.ProcessCmdKey(ref msg, key);
                var ids = NavIds();
                int at = Math.Max(0, ids.IndexOf(keySel));
                Action a;
                switch (key)
                {
                    case Keys.Left:
                    case Keys.Up:
                    case Keys.Shift | Keys.Tab:
                        at = Math.Max(0, at - 1);
                        break;
                    case Keys.Right:
                    case Keys.Down:
                    case Keys.Tab:
                        at = Math.Min(ids.Count - 1, at + 1);
                        break;
                    case Keys.Home: at = 0; break;
                    case Keys.End: at = ids.Count - 1; break;
                    case Keys.Enter:
                    case Keys.Space:
                        string id = keySel;
                        ExitKeys(false);
                        if (id != null && acts.TryGetValue(id, out a)) Run(a, id);
                        return true;
                    case Keys.Apps:
                    case Keys.Shift | Keys.F10:
                        if (keySel != null && menus.TryGetValue(keySel, out a)) Run(a, keySel);
                        return true;
                    case Keys.Escape:
                        ExitKeys(true);
                        return true;
                    default:
                        return true;
                }
                if (ids.Count > 0) keySel = ids[at];
                labelShow.Target = 1;
                Animate();
                return true;
            }

            protected override void OnDeactivate(EventArgs e)
            {
                base.OnDeactivate(e);
                if (keyMode) ExitKeys(false);
            }
        }
    }
}
