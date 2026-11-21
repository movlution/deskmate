using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        const string AllIslandScreensKey = "monitor.all";
        bool AllIslandScreens { get { return settings.GetBool(AllIslandScreensKey, false); } }
        string activeIslandDisplay, hoveredReplica;
        long replicaHoverStart;
        readonly Dictionary<string, IslandReplica> replicas = new Dictionary<string, IslandReplica>(StringComparer.OrdinalIgnoreCase);

        Screen ActiveIslandScreen()
        {
            if (AllIslandScreens && activeIslandDisplay != null)
                foreach (var screen in Screen.AllScreens)
                    if (string.Equals(screen.DeviceName, activeIslandDisplay, StringComparison.OrdinalIgnoreCase)) return screen;
            activeIslandDisplay = null;
            return ChosenScreen();
        }

        void UseAllIslandScreens()
        {
            settings.Set(AllIslandScreensKey, true);
            settings.Save();
            SyncIslandReplicas();
            CheckFullscreen();
            Kick();
        }

        void SyncIslandReplicas()
        {
            if (!AllIslandScreens) { DisposeIslandReplicas(); return; }
            var gone = new HashSet<string>(replicas.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.Bounds.IsEmpty) continue;
                gone.Remove(screen.DeviceName);
                IslandReplica replica;
                if (!replicas.TryGetValue(screen.DeviceName, out replica))
                {
                    replica = new IslandReplica(this, screen.DeviceName);
                    replicas.Add(screen.DeviceName, replica);
                }
                replica.Display = screen.Bounds;
                replica.Monitor = MonitorForScreen(screen);
                replica.DpiScale = MonitorScale(replica.Monitor);
            }
            foreach (string device in gone)
            {
                replicas[device].Dispose();
                replicas.Remove(device);
            }
        }

        void DisposeIslandReplicas()
        {
            foreach (var replica in replicas.Values) replica.Dispose();
            replicas.Clear();
        }

        // Refresh only on state changes; replicas have no independent animation or timers.
        void RefreshIslandReplicas(bool checkFullscreen = false)
        {
            if (replicas.Count == 0) return;
            bool hide = hiddenByUser || dormant || Sharing || Disposing || IsDisposed;
            var rule = checkFullscreen ? FrontRule() : null;
            IntPtr exceptMonitor = rule != null && rule.Mode == ExceptHide
                ? Native.MonitorFromWindow(Native.GetForegroundWindow(), 2) : IntPtr.Zero;
            foreach (var replica in replicas.Values)
            {
                if (checkFullscreen)
                    replica.Blocked = replica.Monitor == IntPtr.Zero || replica.Monitor == exceptMonitor || Native.IsFullscreenForDisplay(replica.Monitor, true);
                if (hide || replica.Blocked || replica.Monitor == monitorHandle) replica.HideReplica();
                else replica.Present(placeMode, Docked, placeInset, placeY, shiftPct);
            }
        }

        bool CanMoveIsland()
        {
            return !dormant && CanExpand() && !MirrorOn && dictState == DictState.Off &&
                islandMenu == null && !keyboardMode && !Capture && dragId == null && !draggingOut &&
                !dropActive && !WifiPassOpen();
        }

        bool HoverIslandReplica()
        {
            if (replicas.Count >= 2 && MouseButtons == MouseButtons.None &&
                settings.Get("panel.open", "hover") != "click" && CanMoveIsland())
            {
                Point cursor = Cursor.Position;
                foreach (var replica in replicas.Values)
                {
                    if (!replica.Presented || !replica.Area.Contains(cursor)) continue;
                    long now = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (hoveredReplica != replica.Device)
                    {
                        hoveredReplica = replica.Device;
                        replicaHoverStart = now;
                    }
                    int delay = Math.Max(150, Math.Min(800, settings.GetInt("panel.delay", 330)));
                    if ((now - replicaHoverStart) * 1000L >= delay * System.Diagnostics.Stopwatch.Frequency)
                        return MoveIslandToReplica(replica, true);
                    return false;
                }
            }
            hoveredReplica = null;
            return false;
        }

        bool MoveIslandToReplica(IslandReplica replica, bool byHover)
        {
            if (!replica.Presented || replica.Monitor == monitorHandle || Sharing || !CanMoveIsland() ||
                Native.IsFullscreenForDisplay(replica.Monitor, true)) return false;
            var screen = Screen.FromPoint(Cursor.Position);
            if (!string.Equals(screen.DeviceName, replica.Device, StringComparison.OrdinalIgnoreCase)) return false;
            int pageToOpen = expanded ? page : ToastPage(byHover);
            hoveredReplica = null;
            activeIslandDisplay = replica.Device;
            hoverTimer.Stop();
            leaveTimer.Stop();
            compactHoverExitPending = pointerInside = openedByHover = false;
            hoverId = pressedId = null;
            shellDown = false;
            pressedBubble = View.Idle;
            pressedCompactBounds = RectangleF.Empty;
            messagesCardClick = null;
            mouse = new PointF(-100, -100);
            placeGlide.Snap(0);
            UpdateMonitor();
            ResetFrost();
            CheckFullscreen();
            if (!fullscreen)
            {
                openedByHover = byHover;
                Expand(pageToOpen);
            }
            else Kick();
            return true;
        }

        // Secondary windows show a plain Mini launcher, not copies of private content or active panels.
        static Rectangle IslandReplicaBounds(Rectangle display, float scale, int place, bool docked, float inset, float sideY, int shift)
        {
            bool side = place == PlaceLeft || place == PlaceRight;
            int w = Math.Min(display.Width, Math.Max(1, (int)Math.Round((side ? MiniH : MiniW) * scale)));
            int h = Math.Min(display.Height, Math.Max(1, (int)Math.Round((side ? MiniW : MiniH) * scale)));
            int gap = docked ? 0 : (int)Math.Round((side ? SideFloat : Gap) * scale);
            int n = (int)Math.Round(inset * scale);
            int x, y = display.Top + gap;
            switch (place)
            {
                case PlaceTopLeft: x = display.Left + n; break;
                case PlaceTopRight: x = display.Right - w - n; break;
                case PlaceLeft: x = display.Left + gap; break;
                case PlaceRight: x = display.Right - w - gap; break;
                default:
                    float free = Math.Max(0, display.Width / 2f - (BaseHalf + Pad) * scale);
                    x = display.Left + display.Width / 2 - w / 2 + (int)Math.Round(shift / 100f * free);
                    break;
            }
            if (side)
            {
                int m = (int)Math.Round(8 * scale);
                y = Math.Max(display.Top + m, Math.Min(display.Bottom - m - h, display.Top + (int)Math.Round(sideY * display.Height)));
            }
            return new Rectangle(Math.Max(display.Left, Math.Min(display.Right - w, x)),
                Math.Max(display.Top, Math.Min(display.Bottom - h, y)), w, h);
        }

        sealed class IslandReplica : Form
        {
            readonly Island island;
            public readonly string Device;
            public Rectangle Display;
            public Rectangle Area { get; private set; }
            public IntPtr Monitor;
            public float DpiScale;
            public bool Blocked, Presented;
            Color lastBg, lastFg;
            int lastPlace = -1;
            bool lastDocked;

            public IslandReplica(Island island, string device)
            {
                this.island = island;
                Device = device;
                Text = "Deskmate Island";
                AccessibleName = "Deskmate Island";
                FormBorderStyle = FormBorderStyle.None;
                AutoScaleMode = AutoScaleMode.None;
                ShowInTaskbar = false;
                Bounds = new Rectangle(-32000, -32000, 1, 1);
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                // Present owns physical bounds and DPI; don't let Form rescale the bitmap window.
                if (m.Msg == Native.WM_DPICHANGED) return;
                base.WndProc(ref m);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) island.MoveIslandToReplica(this, false);
            }

            public void HideReplica()
            {
                if (!Presented) return;
                Native.ShowWindow(Handle, Native.SW_HIDE);
                Presented = false;
            }

            public void Present(int place, bool docked, float inset, float sideY, int shift)
            {
                var at = IslandReplicaBounds(Display, DpiScale, place, docked, inset, sideY, shift);
                var bg = Color.FromArgb(255, Ink.Base.Bg);
                var fg = Color.FromArgb(255, Ink.Base.Text);
                if (at.Width < 2 || at.Height < 2) { HideReplica(); return; }
                if (at != Area || place != lastPlace || docked != lastDocked || bg != lastBg || fg != lastFg)
                {
                    if (!DrawReplica(at, bg, fg, place, docked)) { HideReplica(); return; }
                    Area = at; lastBg = bg; lastFg = fg; lastPlace = place; lastDocked = docked;
                }
                if (Presented) return;
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                Presented = true;
            }

            bool DrawReplica(Rectangle at, Color bg, Color fg, int place, bool docked)
            {
                var dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                IntPtr dib = IntPtr.Zero, old = IntPtr.Zero;
                try
                {
                    var bmi = new Native.BITMAPINFOHEADER
                    {
                        biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                        biWidth = at.Width, biHeight = -at.Height, biPlanes = 1, biBitCount = 32
                    };
                    IntPtr bits;
                    dib = Native.CreateDIBSection(dc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                    if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
                    old = Native.SelectObject(dc, dib);
                    if (old == IntPtr.Zero || old == new IntPtr(-1)) return false;
                    using (var bmp = new Bitmap(at.Width, at.Height, at.Width * 4, PixelFormat.Format32bppPArgb, bits))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.ScaleTransform(DpiScale, DpiScale);
                        var r = new RectangleF(0, 0, at.Width / DpiScale, at.Height / DpiScale);
                        float radius = Math.Min(r.Width, r.Height) / 2;
                        bool side = place == PlaceLeft || place == PlaceRight;
                        using (var shape = !docked ? Ink.Round(r, radius) :
                            side ? Ink.SideNotch(r, radius, radius, place == PlaceRight) : Ink.Notch(r, radius, radius))
                        using (var fill = new SolidBrush(bg))
                        using (var grip = new SolidBrush(Ink.Mix(bg, fg, 0.55f)))
                        {
                            g.FillPath(fill, shape);
                            float gw = Math.Min(r.Width / 2, side ? 3 : 18), gh = Math.Min(r.Height / 2, side ? 16 : 3);
                            using (var mark = Ink.Round(new RectangleF((r.Width - gw) / 2, (r.Height - gh) / 2, gw, gh), 1.5f))
                                g.FillPath(grip, mark);
                        }
                        g.Flush(FlushIntention.Sync);
                        var dst = new Native.POINT(at.X, at.Y);
                        var size = new Native.SIZE(at.Width, at.Height);
                        var src = new Native.POINT(0, 0);
                        var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                        return Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, Native.ULW_ALPHA);
                    }
                }
                finally
                {
                    if (old != IntPtr.Zero && old != new IntPtr(-1)) Native.SelectObject(dc, old);
                    if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                    Native.DeleteDC(dc);
                }
            }
        }
    }
}
