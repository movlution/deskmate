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
            // Tray: hover, press and release reach the owning app
            void TrayMove(string id)
            {
                var ic = TrayById(id);
                if (ic != trayHot)
                {
                    TrayLeave();
                    trayHot = ic;
                    if (ic != null) { trayPop.Stop(); trayPop.Start(); }
                }
                if (ic != null && tray != null && !tray.Gone(ic)) TrayHost.Send(ic, 0x200, Cursor.Position);
            }

            void TrayPopup()
            {
                trayPop.Stop();
                if (trayHot == null || trayHot.Version <= 3) return;
                trayPopup = true;
                TrayHost.Send(trayHot, 0x406, Cursor.Position);
            }

            void TrayLeave()
            {
                trayPop.Stop();
                if (trayHot != null && trayPopup) TrayHost.Send(trayHot, 0x407, Cursor.Position);
                trayPopup = false;
                trayHot = null;
            }

            public void TrayDown(TrayIcon ic, MouseButtons button)
            {
                var owner = TrayOwner();
                if (ic == null || owner == null || owner.tray.Gone(ic)) return;
                bool dbl = button == MouseButtons.Left && lastTrayKey == ic.Key && (DateTime.UtcNow - lastTrayUtc).TotalMilliseconds < SystemInformation.DoubleClickTime;
                uint msg = button == MouseButtons.Right ? 0x204u : button == MouseButtons.Middle ? 0x207u : dbl ? 0x203u : 0x201u;
                TrayHost.Down(ic, msg, Cursor.Position);
            }

            public void TrayUp(TrayIcon ic, MouseButtons button)
            {
                var owner = TrayOwner();
                if (ic == null || owner == null || owner.tray.Gone(ic)) return;
                var at = Cursor.Position;
                if (button == MouseButtons.Left)
                {
                    bool reached = TrayHost.Send(ic, 0x202, at);
                    if (ic.Version >= 3) TrayHost.Send(ic, 0x400, at);
                    lastTrayKey = ic.Key;
                    lastTrayUtc = DateTime.UtcNow;
                    // Elevated owner: the click never arrives, so start the app again to raise its window
                    if (!reached) TrayHost.Restore(ic);
                }
                else if (button == MouseButtons.Right)
                {
                    TrayHost.Send(ic, 0x205, at);
                    if (ic.Version >= 3) TrayHost.Send(ic, 0x7B, at);
                }
                else if (button == MouseButtons.Middle) TrayHost.Send(ic, 0x208, at);
            }

            void TrayDown(string id, MouseButtons button) { TrayDown(TrayById(id), button); }
            void TrayUp(string id, MouseButtons button) { TrayUp(TrayById(id), button); }

            // Files over an app: its window comes up; dropped files open with it.
            // Anything else dropped on the dock waits in its tray, to be dragged on later.
            protected override void OnDragOver(DragEventArgs e)
            {
                base.OnDragOver(e);
                var c = PointToClient(new Point(e.X, e.Y));
                mouseX = c.X / scale;
                mouseY = c.Y / scale;
                inside = true;
                hover.Target = 1;
                bool holdable = ShelfSlotOn && Holdable(e.Data);
                if (holdable && !fileOver) { fileOver = true; Reveal(true); CloseCard(); }
                string id = HitAt(new PointF(mouseX, mouseY));
                var app = AppById(id);
                var files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
                e.Effect = DragDropEffects.None;
                if (app != null && files != null && OpensFiles(app)) e.Effect = DragDropEffects.Copy;
                // The tray's own files only go to apps
                else if (shelfDragOut) { }
                else if (id == "sys:shelf" && holdable) e.Effect = DragDropEffects.Copy;
                else if (app == null && files != null && Array.TrueForAll(files, Pinnable)) e.Effect = DragDropEffects.Link;
                else if (app == null && holdable) e.Effect = DragDropEffects.Copy;
                if (id != hoverId && (fileOver || shelfDragOut))
                {
                    hoverId = id;
                    labelShow.Target = id == "sys:shelf" ? 1 : 0;
                }
                if (id != dropHover) { dropHover = id; dropSinceUtc = DateTime.UtcNow; }
                else if (app != null && app.Running && (DateTime.UtcNow - dropSinceUtc).TotalMilliseconds > 700)
                {
                    var w = app.Recent();
                    if (w != null && GetForegroundWindow() != w.H) ShellApp.Activate(w.H);
                }
                Animate();
            }

            protected override void OnDragEnter(DragEventArgs e) { OnDragOver(e); }

            protected override void OnDragLeave(EventArgs e)
            {
                base.OnDragLeave(e);
                DragGone();
                if (HideMode != "never") { hideLater.Stop(); hideLater.Start(); }
                Animate();
            }

            void DragGone()
            {
                dropHover = null;
                inside = false;
                fileOver = false;
                hover.Target = 0;
                if (hoverId != null) { hoverId = null; labelShow.Target = 0; }
            }

            protected override void OnDragDrop(DragEventArgs e)
            {
                base.OnDragDrop(e);
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                string at = dropHover;
                var app = AppById(at);
                bool tray = ShelfSlotOn && !shelfDragOut && app == null;
                DragGone();
                Animate();
                // On the tray, or anything the dock cannot pin: hold it
                if (tray && (at == "sys:shelf" || files == null || files.Length == 0 || !Array.TrueForAll(files, Pinnable)))
                {
                    var held = DropPaths(e.Data);
                    if (held.Count > 0) isle.HoldInDock(held);
                    return;
                }
                if (files == null || files.Length == 0) return;
                if (app != null && OpensFiles(app))
                {
                    var args = new System.Text.StringBuilder();
                    foreach (var f in files) args.Append('"').Append(f).Append("\" ");
                    try { Process.Start(new ProcessStartInfo(app.Exe, args.ToString().Trim()) { UseShellExecute = true }); }
                    catch (Exception ex) { Log.Write("dock drop: " + ex.Message); }
                    if (BounceOnce(app)) Animate();
                    return;
                }
                if (app == null)
                {
                    var pins = isle.DockPins();
                    foreach (var f in files) if (Pinnable(f) && !pins.Exists(x => string.Equals(x, f, StringComparison.OrdinalIgnoreCase))) pins.Add(f);
                    isle.SetDockPins(pins);
                }
            }

            static bool BounceOnce(DockApp app)
            {
                if (app.Hop >= 0) return false;
                app.Hop = 0;
                app.Hops = 1;
                return true;
            }

            static bool OpensFiles(DockApp a) { return a.Exe.Length > 0 && File.Exists(a.Exe) && !a.Packaged; }

            static bool Pinnable(string f)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                return (ext == ".exe" || ext == ".lnk") && File.Exists(f);
            }
        }
    }
}
