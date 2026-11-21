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
        void AddHit(RectangleF local, string id, Action act) { AddHit(local, id, act, null, null); }

        void AddHit(RectangleF local, string id, Action act, Action<float> drag, Action menu) { AddHit(local, id, act, drag, menu, null); }

        void AddHit(RectangleF local, string id, Action act, Action<float> drag, Action menu, Action dragEnd)
        {
            if (inSlot && fpsLog) Log.Write("live: hit " + id + " inside a live area is lost");
            // No pointer, no hits to allocate
            if (!hitsOn || !(pointerInside || keyboardMode || dragId != null)) return;
            local.Offset(originX, originY + hitDy);
            Hit h;
            if (hitPool.Count > 0) { h = hitPool[hitPool.Count - 1]; hitPool.RemoveAt(hitPool.Count - 1); }
            else h = new Hit();
            h.Rect = local;
            h.Id = id;
            h.Act = act;
            h.Drag = drag;
            h.Menu = menu;
            h.DragEnd = dragEnd;
            hits.Add(h);
        }

        // Hits are reused frame to frame
        readonly List<Hit> hitPool = new List<Hit>();

        void ClearHits()
        {
            hitPool.AddRange(hits);
            hits.Clear();
        }

        Hit HitById(string id)
        {
            if (id == null) return null;
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Id == id) return hits[i];
            return null;
        }

        static float Fraction(Hit h, PointF pt) { return Ease.Clamp01((pt.X - h.Rect.X) / Math.Max(1, h.Rect.Width)); }

        // Dragged slider; survives hit rebuilds
        string dragId;

        bool Hover(string id) { return hoverId == id; }

        // Held down under the pointer
        bool Pressed(string id) { return pressedId == id && hoverId == id; }

        // Input

        bool InShape(PointF pt)
        {
            var s = shell;
            s.Inflate(2, 2);
            return s.Contains(pt) || (!bubbleRect.IsEmpty && bubbleRect.Contains(pt));
        }

        bool OverHoverTarget(PointF pt)
        {
            if (shell.IsEmpty || bubbleRect.Contains(pt)) return false;
            var target = shell;
            target.Inflate(2, 2);
            // Include the strip between a top-mounted island and the monitor edge.
            if (!OnSide && frameTop == monitorPx.Top)
                target = RectangleF.FromLTRB(target.Left, 0, target.Right, target.Bottom);
            return target.Contains(pt);
        }

        bool CanStartHover()
        {
            return !IsDisposed && !Disposing && windowShown && !fullscreen && opacity.Value > 0.5f &&
                !expanded && islandMenu == null && !keyboardMode && !Capture && MouseButtons == MouseButtons.None &&
                pressedCompactBounds.IsEmpty && pressedId == null && dragId == null && !draggingOut &&
                settings.Get("panel.open", "hover") != "click" && CanExpand() && OverHoverTarget(CursorInIsland());
        }

        void WatchHover()
        {
            if (HoverIslandReplica()) return;
            if (compactHoverExitPending && !HoldCompactHoverAtEdge(CursorInIsland())) PointerLeave();
            if (messagesCardClick.HasValue)
            {
                if (!expanded || page != PageMessages)
                    messagesCardClick = null;
                else
                {
                    if (!sw.Settled || !sh.Settled ||
                        (!Anim.Reduced && (DateTime.UtcNow - pageSwitchUtc).TotalSeconds < PageTransitionSeconds)) return;
                    var pt = CursorInIsland();
                    bool inside = InShape(pt) || (openedByHover && OverHoverTarget(pt));
                    // A resize can move the panel away from a stationary cursor.
                    if (!inside && Cursor.Position == messagesCardClick.Value) return;
                    messagesCardClick = null;
                    if (inside)
                    {
                        pointerInside = true;
                        leaveTimer.Stop();
                    }
                    else PointerLeave();
                }
            }
            if (expanded)
            {
                if (!openedByHover || islandMenu != null || WifiPassOpen() || dragId != null || draggingOut || prompterSizing != 0) return;
                var pt = CursorInIsland();
                if (InShape(pt) || OverHoverTarget(pt)) return;
                if (pointerInside) PointerLeave();
                else if (!leaveTimer.Enabled) leaveTimer.Start();
                return;
            }
            if (!CanStartHover())
            {
                hoverTimer.Stop();
                if (!OverHoverTarget(CursorInIsland())) openedByHover = false;
                return;
            }
            if (openedByHover || hoverTimer.Enabled) return;
            hoverTimer.Interval = Math.Max(150, Math.Min(800, settings.GetInt("panel.delay", 330)));
            hoverTimer.Start();
            PrefetchFrost();
        }

        PointF CursorInIsland()
        {
            var at = Cursor.Position;
            return new PointF((at.X - frameLeft) / scale, (at.Y - frameTop) / scale);
        }

        RectangleF ToScreenBounds(RectangleF local)
        {
            return new RectangleF(frameLeft + local.X * scale, frameTop + local.Y * scale,
                local.Width * scale, local.Height * scale);
        }

        bool CanExpand() { return !hiddenByUser && !PrompterOn && !ConvertOn && !sprintActive && approvals.Count == 0 && (toast == null || !toast.Interactive); }

        // A toast opens where its story continues
        int ToastPage(bool hover)
        {
            if (toast == null) return DefaultPage(hover);
            if (toast.Page >= 0) return toast.Page;
            switch (toast.View)
            {
                case View.Track: return PageMusic;
                case View.Done: return PageAgents;
                case View.Notif: return PageMessages;
                case View.Deadline: return PageClass;
                case View.Volume:
                case View.Brightness:
                case View.AudioDevice:
                case View.Power:
                case View.Network:
                case View.Mic: return PageControl;
                case View.Pray:
                case View.Remind: return PageRemind;
                case View.Shot:
                case View.ShelfDrop: return PageShelf;
                case View.Otp: return PagePhone;
            }
            return DefaultPage(hover);
        }

        void Expand(int pg)
        {
            if (!CanExpand()) return;
            messagesCardClick = null;
            toast = null;
            toastTimer.Stop();
            // A detached compact bubble would otherwise bridge into the growing panel.
            bubble.Snap(0);
            expanded = true;
            homeScroll.Snap(0);
            PickDecor();
            StartBubbles(12);
            SetPage(pg, true);
            Kick();
        }

        string HitAt(PointF pt)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Rect.Contains(pt)) return hits[i].Id;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool wasOnBubble = bubbleRect.Contains(mouse);
            mouse = new PointF(e.X / scale, e.Y / scale);
            if (prompterSizing != 0) { SizePrompterTo(Control.MousePosition); return; }
            if (dragId != null)
            {
                var dh = HitById(dragId);
                if (dh != null && dh.Drag != null) dh.Drag(Fraction(dh, mouse));
                Kick();
                return;
            }
            if (ShelfDragCheck()) return;
            bool inside = InShape(mouse);
            if (current == View.Prompter) PrompterHover(mouse, inside);
            bool onBubble = bubbleRect.Contains(mouse);
            if (onBubble) hoverTimer.Stop();
            if (inside && (!pointerInside || (wasOnBubble && !onBubble)))
            {
                pointerInside = true;
                Kick();
                leaveTimer.Stop();
                if (!expanded && !onBubble && settings.Get("panel.open", "hover") == "click") PrefetchFrost();
            }
            else if (!inside && pointerInside)
            {
                if (!HoldCompactHoverAtEdge(mouse)) { PointerLeave(); return; }
                compactHoverExitPending = true;
            }
            if (inside) compactHoverExitPending = false;
            string id = inside ? HitAt(mouse) : null;
            // Redraw only when the hovered control changes
            if (id != hoverId) { hoverId = id; Kick(); if (expanded) TabHoverCheck(id); }
        }

        void PointerLeave()
        {
            compactHoverExitPending = false;
            // Dragging slider or file: stay open
            if (dragId != null || draggingOut) return;
            if (prompterSizing != 0) return;
            pointerInside = false;
            shellDown = false;
            leaveTimer.Stop();
            if (!messagesCardClick.HasValue && islandMenu == null) leaveTimer.Start();
            hoverId = null;
            Kick();
            PrompterHover(mouse, false);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!pointerInside) return;
            if (HoldCompactHoverAtEdge(CursorInIsland())) compactHoverExitPending = true;
            else PointerLeave();
        }

        bool HoldCompactHoverAtEdge(PointF pt)
        {
            if (!windowShown || fullscreen || dormant || !Swells() || bubbleRect.Contains(pt)) return false;
            var outer = RectangleF.Inflate(shell, 6, 6);
            var inner = RectangleF.Inflate(shell, -6, -6);
            return outer.Contains(pt) && !inner.Contains(pt);
        }

        // Capture lost mid-resize: keep what's there
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (prompterSizing != 0 && !Capture) EndPrompterSize();
            if (!Capture)
            {
                pressedBubble = View.Idle;
                pressedCompactBounds = RectangleF.Empty;
                pressedId = null;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                var pt = new PointF(e.X / scale, e.Y / scale);
                if (current == View.Prompter && PrompterGrab(pt, Control.MousePosition)) return;
                pressedCompactBounds = RectangleF.Empty;
                bool onBubble = bubbleRect.Contains(pt);
                pressedBubble = !expanded && onBubble ? BubbleKind() : View.Idle;
                if (pressedBubble != View.Idle)
                {
                    pointerInside = true;
                    shellDown = false;
                    hoverTimer.Stop();
                    leaveTimer.Stop();
                    pressedId = null;
                    pressedCompactBounds = ToScreenBounds(bubbleRect);
                    return;
                }
                if (InShape(pt))
                {
                    if (!pointerInside) pointerInside = true;
                    if (!expanded) { hoverTimer.Stop(); leaveTimer.Stop(); }
                    if (hits.Count == 0 && g != null && !dormant)
                        try { Render(DateTime.UtcNow); } catch (Exception) { }
                }
                pressedId = HitAt(pt);
                var h = HitById(pressedId);
                if (h != null && h.Drag != null) { dragId = pressedId; Capture = true; h.Drag(Fraction(h, pt)); }
                if (!expanded && !onBubble && h == null && shell.Contains(pt) && CanExpand())
                    pressedCompactBounds = ToScreenBounds(shell);
                // Bare pill or a pill-wide hit
                shellDown = InShape(pt) && (h == null || h.Rect.Width >= shell.Width * 0.9f);
                Kick();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            View bubbleClick = pressedBubble;
            RectangleF compactBounds = pressedCompactBounds;
            if (e.Button == MouseButtons.Left)
            {
                pressedBubble = View.Idle;
                pressedCompactBounds = RectangleF.Empty;
            }
            base.OnMouseUp(e);
            shellDown = false;
            var pt = new PointF(e.X / scale, e.Y / scale);
            if (prompterSizing != 0)
            {
                EndPrompterSize();
                Capture = false;
                if (!InShape(pt)) PointerLeave();
                return;
            }
            if (dragId != null)
            {
                var dh = HitById(dragId);
                if (dh != null && dh.DragEnd != null) dh.DragEnd();
                dragId = null;
                Capture = false;
                pressedId = null;
                Kick();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                var mh = HitById(HitAt(pt));
                if (mh != null && mh.Menu != null) { mh.Menu(); return; }
                ShowIslandMenu(menu);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            bool releasedOnPress = compactBounds.Contains(frameLeft + e.X, frameTop + e.Y);
            if (bubbleClick != View.Idle)
            {
                if (!expanded && CanExpand() &&
                    (releasedOnPress || bubbleRect.Contains(pt)))
                {
                    if (bubbleClick == View.Task) ShowTasks();
                    else if (bubbleClick == View.Timer) Expand(PageControl);
                    else Expand(bubbleClick == View.Agent ? PageAgents : ToastPage(false));
                }
                return;
            }
            string id = HitAt(pt);
            if (id != null && id == pressedId)
            {
                foreach (var h in hits)
                {
                    if (h.Id != id) continue;
                    try { h.Act(); } catch (Exception ex) { Log.Write("action " + id + ": " + ex.Message); }
                    break;
                }
            }
            else if (id == null && current == View.Prompter && InShape(pt)) TogglePrompter();
            else if (releasedOnPress && !expanded && CanExpand())
            {
                hoverTimer.Stop();
                Expand(ToastPage(false));
            }
            pressedId = null;
            Kick();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (current == View.Prompter) { ScrollPrompter(e.Delta); return; }
            if (current == View.Approve && ScrollApproval(e.Delta)) return;
            // Open panel: wheel changes tabs, lists scroll
            if (expanded)
            {
                bool overTabs = mouse.Y - shell.Y < PanelTop - 2;
                if (overTabs || !ScrollPanel(e.Delta)) WheelTab(-e.Delta);
                return;
            }
            if (current == View.Approve) return;
            vol = Ease.Clamp01(vol + e.Delta / 120f * 0.04f);
            volBar.Target = vol;
            volume.Set(vol);
        }

        bool Docked { get { return settings.GetBool("dock", true); } }

        // Hide on demand; permission cards still show
        const int HideHotkey = 1;
        bool hiddenByUser;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        bool hideTipShown;

        void ToggleHidden()
        {
            hiddenByUser = !hiddenByUser;
            if (hiddenByUser) expanded = false;
            // Hidden island can't say how to return, so the tray does
            if (hiddenByUser && !hideTipShown && tray != null)
            {
                hideTipShown = true;
                tray.ShowBalloonTip(5000, Lang.T("Island disembunyikan"), HotkeyText(HideHotkey).Length > 0 ? Lang.F("Klik ikon Deskmate di tray atau tekan {0} untuk memunculkannya lagi.", HotkeyText(HideHotkey)) : Lang.T("Klik ikon Deskmate di tray untuk memunculkannya lagi."), ToolTipIcon.None);
            }
            Kick();
        }
    }
}
