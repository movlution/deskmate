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
            // Empty desktop in front: nothing to make room for
            bool StayOnDesktop { get { return S.GetBool("dockbar.desktop", true); } }

            bool DesktopFront()
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                if (startKey != null && fg == startKey.KeeperHandle) return true;
                var c = new System.Text.StringBuilder(32);
                GetClassName(fg, c, 32);
                string cls = c.ToString();
                return cls == "Progman" || cls == "WorkerW";
            }

            public bool IsPinned(string target) { return isle.DockPins().Exists(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase)); }

            public void SetPinned(string target, bool on)
            {
                var pins = isle.DockPins();
                pins.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                if (on) pins.Add(target);
                isle.SetDockPins(pins);
            }

            // Right-click on Start: the places and power actions people reach for
            void QuickMenu() { ShowMenu(QuickItems()); }

            // Win+X: same menu, above Start
            void QuickMenuAtStart()
            {
                var m = QuickItems();
                CloseCard();
                if (sheet != null) SheetDone(sheet);
                MenuLook.Apply(m);
                m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
                var at = HitScreenRect("sys:start");
                MenuGuard.Watch(m);
                m.Show(new Point(at.X, at.Top - (int)(6 * scale)), ToolStripDropDownDirection.AboveRight);
            }

            ContextMenuStrip QuickItems()
            {
                var m = new ContextMenuStrip();
                m.Items.Add(Lang.T("Task Manager"), null, delegate { Open("taskmgr.exe"); });
                m.Items.Add(Lang.T("Pengaturan Windows"), null, delegate { Open("ms-settings:"); });
                m.Items.Add(Lang.T("File Explorer"), null, delegate { Open("explorer.exe"); });
                m.Items.Add(Lang.T("Terminal"), null, delegate { Open(File.Exists(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe")) ? "wt.exe" : "powershell.exe"); });
                m.Items.Add(Lang.T("Pengelola Perangkat"), null, delegate { Open("devmgmt.msc"); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Lang.T("Kunci"), null, delegate { SessionPower.Lock(); });
                m.Items.Add(Lang.T("Tidur"), null, delegate { SessionPower.Sleep(); });
                m.Items.Add(Lang.T("Keluar"), null, delegate { SessionPower.SignOut(); });
                m.Items.Add(Lang.T("Mulai ulang"), null, delegate { SessionPower.Restart(); });
                m.Items.Add(Lang.T("Matikan"), null, delegate { SessionPower.ShutDown(); });
                return m;
            }

            public Bitmap IconOf(DockApp a)
            {
                return a != null ? IconFor(a.Key) : null;
            }

            void CloseCard()
            {
                if (card == null) return;
                var c = card;
                card = null;
                MenuGuard.Unwatch(c);
                if (!c.IsDisposed) c.Dismiss();
                if (!closing)
                {
                    Render();
                    if (sheet == null && !keyMode && HideMode != "never" && !inside) { hideLater.Stop(); hideLater.Start(); }
                }
            }

            void CloseCardFor(DockApp a)
            {
                if (card != null && card.Covers(a)) CloseCard();
            }

            int BarTopScreen() { return WindowOrigin().Y + (int)Math.Round(barTop * scale); }

            // Where the bar sits when shown, even mid-slide
            int BarTopShownScreen() { return WindowOrigin().Y + (int)Math.Round((ch / scale - Edge - BarH) * scale); }

            Rectangle HitScreenRect(string id)
            {
                foreach (var kv in hits) if (kv.Value == id) return ToScreen(kv.Key);
                return ToScreen(rightPill);
            }

            // Drag to reorder; drag up and off the dock to unpin
            void BeginDrag(DockApp a, PointF pt)
            {
                var s = slots.Find(x => x.App == a);
                if (s == null) return;
                dragApp = a;
                dragGrab = new PointF(pt.X - (s.Icon.X + s.Icon.Width / 2), pt.Y - (s.Icon.Y + s.Icon.Height / 2));
                dragOut = false;
                int index = apps.FindIndex(x => x == a);
                dragTo = index;
                gaps.Clear();
                var gap = new Gap { At = index };
                Tune(gap.Show, 300, 0.85f);
                gap.Show.Snap(1);
                gaps.Add(gap);
                CloseCard();
                labelShow.Target = 0;
                labelShow.Snap(0);
            }

            void DragMove(PointF pt)
            {
                dragOut = pt.Y < barTop - 64;
                int to = -1;
                if (!dragOut)
                {
                    // Count the icons left of the pointer, the dragged one aside
                    to = 0;
                    foreach (var s in slots)
                        if (s.App != null && s.App != dragApp && !s.App.Leaving && s.Icon.X + s.Icon.Width / 2 < pt.X) to++;
                }
                if (to == dragTo) return;
                dragTo = to;
                foreach (var gp in gaps) gp.Show.Target = 0;
                if (to >= 0)
                {
                    var gap = new Gap { At = to };
                    Tune(gap.Show, 300, 0.85f);
                    gap.Show.Snap(0);
                    gap.Show.Target = 1;
                    gaps.Add(gap);
                }
            }

            void EndDrag()
            {
                var a = dragApp;
                dragApp = null;
                gaps.Clear();
                if (a == null) return;
                if (dragOut)
                {
                    float size = iconSize * 1.12f;
                    ShatterDockIcon(a, Glyph(new RectangleF(mouseX - dragGrab.X - size / 2, mouseY - dragGrab.Y - size / 2, size, size)));
                    if (a.Pinned) Unpin(a);
                    a.Show.Snap(0);
                    Refresh(false);
                    return;
                }
                var others = apps.FindAll(x => x != a && !x.Leaving);
                int pinned = others.FindAll(x => x.Pinned).Count;
                int to = Math.Max(0, Math.Min(dragTo, others.Count));
                if (to <= pinned) Pin(a, PinIndex(others, to));
                else if (a.Pinned) Pin(a, -1);
                // The icon lands where the gap was, then grows back in
                a.Show.Snap(0.6f);
                a.Show.Target = 1;
                Refresh(false);
            }

            // Position among pins for a slot among all apps
            static int PinIndex(List<DockApp> others, int to)
            {
                int n = 0;
                for (int i = 0; i < to && i < others.Count; i++) if (others[i].Pinned) n++;
                return n;
            }

            void InsertDragGap()
            {
                slots.RemoveAll(s => s.App == dragApp);
                int sys = slots.FindIndex(s => s.App != null);
                if (sys < 0) sys = slots.Count;
                // Gaps sorted from the end keep earlier indexes valid
                var order = new List<Gap>(gaps);
                order.Sort((x, y) => y.At.CompareTo(x.At));
                foreach (var gp in order)
                {
                    int appIndex = 0, at = sys;
                    for (int i = sys; i < slots.Count && appIndex < gp.At; i++) { if (slots[i].App != null) appIndex++; at = i + 1; }
                    slots.Insert(Math.Min(at, slots.Count), new Slot { Id = "gap", Show = Ease.Clamp01(gp.Show.Value) });
                }
            }

            bool StepDrag(float dt)
            {
                bool moving = false;
                for (int i = gaps.Count - 1; i >= 0; i--)
                {
                    gaps[i].Show.Step(dt);
                    moving |= !gaps[i].Show.Settled;
                    if (gaps[i].Show.Target == 0 && gaps[i].Show.Value < 0.01f) gaps.RemoveAt(i);
                }
                return moving || dragApp != null;
            }

            void DrawDragGhost()
            {
                float size = iconSize * 1.12f, alpha = dragOut && dragApp.Pinned ? 0.55f : 0.95f;
                var r = new RectangleF(mouseX - dragGrab.X - size / 2, mouseY - dragGrab.Y - size / 2, size, size);
                Tile(r, true, false, alpha);
                DrawIconImage(dragApp, Glyph(r), alpha);
                if (dragOut && dragApp.Pinned)
                {
                    // Removal mark: the pin goes when released here
                    var b = new RectangleF(r.Right - 12, r.Top - 4, 18, 18);
                    p.FillRound(b, 9, Ink.A(Ink.Base.Bg, 0.9f));
                    g.DrawLine(p.P(p.T.Text, 1.8f), b.X + 5, b.Y + 9, b.Right - 5, b.Y + 9);
                }
            }
        }
    }
}
