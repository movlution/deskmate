using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Desktop pet: one placeholder cat walking on the dock's top edge, or on the
    // bottom of the work area without a dock. Opt-in through pet.on, or DESKMATE_PET=1 for trial builds.
    partial class Island
    {
        PetWindow pet;

        bool PetOn { get { return settings.GetBool("pet.on", false) || Environment.GetEnvironmentVariable("DESKMATE_PET") == "1"; } }

        void ApplyPet()
        {
            if (!PetOn) { ClosePet(); return; }
            var dock = dockWin != null && !dockWin.IsDisposed ? dockWin : null;
            var screen = dock != null && dock.PetScreen != null ? dock.PetScreen : ChosenScreen();
            IntPtr mon = MonitorForScreen(screen);
            Rectangle work = screen.WorkingArea.IsEmpty ? screen.Bounds : screen.WorkingArea;
            float s = MonitorScale(mon);
            if (pet == null || pet.IsDisposed)
            {
                pet = new PetWindow(work, s, mon, () => dormant || hiddenByUser);
                pet.Show();
            }
            else pet.Retarget(work, s, mon);
            // Owned by the dock: always just above it, and the dock's later windows stay above both
            pet.Owner = dock;
            pet.Floor(dock != null ? dock.PetFloor() : Rectangle.Empty, dock != null);
        }

        // A closing owner would take the pet with it
        void DetachPet() { if (pet != null && !pet.IsDisposed) pet.Owner = null; }

        // Every dock frame: the pet rides the bar as it rises and drops
        void PetFollow(DockWindow d) { if (pet != null && !pet.IsDisposed && d == dockWin) pet.Floor(d.PetFloor(), true); }

        void ClosePet()
        {
            if (pet != null && !pet.IsDisposed) pet.Close();
            pet = null;
        }

        sealed partial class DockWindow
        {
            internal Screen PetScreen { get { return targetScreen; } }

            // Screen px: the flat part of the bar's top edge, held at the screen bottom while the bar
            // is down. Empty while the dock is hidden or not laid out yet.
            internal Rectangle PetFloor()
            {
                if (!shown || closing || g == null || scale <= 0) return Rectangle.Empty;
                var bar = Full ? new RectangleF(0, barTop, cw / scale, BarH) : centerPill;
                float r = Radius;
                if (bar.Width <= r * 2 + 1) return Rectangle.Empty;
                var top = ToScreen(new RectangleF(bar.X + r, bar.Y, bar.Width - r * 2, 1));
                return new Rectangle(top.X, Math.Min(top.Y, mon.Bottom), top.Width, 1);
            }
        }

        enum PetMode { Idle, Walk, Sit, Sleep }

        // Time-driven behaviour with no window, so tests can step it
        sealed class PetBrain
        {
            public PetMode Mode = PetMode.Idle;
            // Left edge of the sprite and its walkable range, px
            public double X, Left, Right;
            public int Dir = 1;
            // Logical px per second; scaled by Scale
            public double Speed = 36;
            public float Scale = 1;
            // Reduced motion: the pet stays where it is
            public bool NoWalk;
            double modeLeft = 2, animT, targetX, hopT = -1;
            public const double HopTime = 0.4, HopHeight = 12;
            readonly Random rnd;

            public PetBrain(int seed) { rnd = new Random(seed); }

            // Walking or mid-hop: frames run on vsync
            public bool Moving { get { return Mode == PetMode.Walk || hopT >= 0; } }

            // Screen px above the ground: one parabolic hop
            public double Lift
            {
                get
                {
                    if (hopT < 0) return 0;
                    double u = hopT / HopTime;
                    return 4 * u * (1 - u) * HopHeight * Scale;
                }
            }

            // Click: a sleeping or sitting pet wakes; reduced motion skips the hop itself
            public void Hop()
            {
                if (Mode == PetMode.Sleep || Mode == PetMode.Sit) Enter(PetMode.Idle);
                if (!NoWalk) hopT = 0;
            }

            // Pointer over the pet: a walk stops and it looks that way (dx > 0: pointer to the right).
            // True when the picture changes. A sleeping pet is not woken by hover.
            public bool Notice(int dx)
            {
                if (Mode == PetMode.Sleep) return false;
                int face = dx >= 0 ? 1 : -1;
                bool changed = face != Dir;
                Dir = face;
                if (Mode == PetMode.Walk) { Enter(PetMode.Idle); return true; }
                if (modeLeft < 2) modeLeft = 2;
                return changed;
            }

            public void Bounds(double left, double right, bool scatter)
            {
                Left = left;
                Right = Math.Max(left, right);
                X = scatter ? Left + rnd.NextDouble() * (Right - Left) : Clamp(X);
                targetX = Clamp(targetX);
            }

            double Clamp(double v) { return Math.Max(Left, Math.Min(Right, v)); }

            public void Step(double dt)
            {
                if (dt < 0) dt = 0;
                animT += dt;
                if (hopT >= 0)
                {
                    hopT += Math.Min(dt, 0.1);
                    if (hopT >= HopTime) hopT = -1;
                }
                if (Mode == PetMode.Walk)
                {
                    if (NoWalk) { Enter(PetMode.Sit); return; }
                    // A stalled frame slows the step instead of teleporting the pet
                    double v = Speed * Scale * Math.Min(dt, 0.1), gap = targetX - X;
                    if (Math.Abs(gap) <= v) { X = targetX; Enter(rnd.NextDouble() < 0.6 ? PetMode.Idle : PetMode.Sit); }
                    else X += Math.Sign(gap) * v;
                    return;
                }
                modeLeft -= dt;
                if (modeLeft <= 0) Next();
            }

            void Next()
            {
                double r = rnd.NextDouble();
                if (Mode == PetMode.Idle) { if (r < 0.65) StartWalk(); else Enter(PetMode.Sit); }
                else if (Mode == PetMode.Sit) { if (r < 0.3) Enter(PetMode.Sleep); else if (r < 0.65) StartWalk(); else Enter(PetMode.Idle); }
                else Enter(PetMode.Sit);
            }

            void StartWalk()
            {
                double min = 60 * Scale, reach = 420 * Scale;
                double lo = Math.Max(Left, X - reach), hi = Math.Min(Right, X + reach);
                if (NoWalk || hi - lo < min) { Enter(PetMode.Sit); return; }
                double t = X;
                for (int i = 0; i < 8 && Math.Abs(t - X) < min; i++) t = lo + rnd.NextDouble() * (hi - lo);
                if (Math.Abs(t - X) < min) { Enter(PetMode.Idle); return; }
                targetX = t;
                Dir = t > X ? 1 : -1;
                Enter(PetMode.Walk);
            }

            void Enter(PetMode m)
            {
                Mode = m;
                animT = 0;
                double r = rnd.NextDouble();
                modeLeft = m == PetMode.Idle ? 2 + r * 3 : m == PetMode.Sit ? 4 + r * 5 : m == PetMode.Sleep ? 20 + r * 25 : 0;
            }

            // Sprite frames per second; the walk cycle follows ground speed
            double Rate
            {
                get
                {
                    if (Mode == PetMode.Walk) return 8 * Speed / 36;
                    return Mode == PetMode.Idle ? 1.5 : Mode == PetMode.Sit ? 0.8 : 0.6;
                }
            }

            // Atlas column: idle 0-1, walk 2-5, sit 6-7, sleep 8-9
            public int Cell
            {
                get
                {
                    int n = (int)(animT * Rate);
                    if (Mode == PetMode.Walk) return 2 + n % 4;
                    if (Mode == PetMode.Sit) return 6 + n % 2;
                    if (Mode == PetMode.Sleep) return 8 + n % 2;
                    return n % 2;
                }
            }

            // Seconds until the picture can change while standing still
            public double NextChangeIn()
            {
                double r = Rate, frame = (Math.Floor(animT * r) + 1) / r - animT;
                return Math.Max(0.001, Math.Min(frame, modeLeft));
            }
        }

        // Placeholder pixel cat on a 16 px grid, outlined, mirrored for walking left
        static class PetArt
        {
            public const int Grid = 16, Cells = 10;
            const byte Fur = 1, Stripe = 2, Dark = 3, Nose = 4, Light = 5;
            static readonly int[] Palette = { 0, unchecked((int)0xFFF0A35E), unchecked((int)0xFFC9773A), unchecked((int)0xFF2A1E17), unchecked((int)0xFFE88A8A), unchecked((int)0xFFF5F1EA) };

            // Opaque or clear pixels only, so the buffer is already premultiplied.
            // Row 0 faces right, row 1 left; f is the screen px per art px.
            public static int[] Atlas(int f)
            {
                int cell = Grid * f, w = cell * Cells;
                var px = new int[w * cell * 2];
                for (int c = 0; c < Cells; c++)
                {
                    byte[] g = Outline(Frame(c));
                    for (int gy = 0; gy < Grid; gy++)
                        for (int gx = 0; gx < Grid; gx++)
                        {
                            int v = Palette[g[gy * Grid + gx]];
                            if (v == 0) continue;
                            for (int row = 0; row < 2; row++)
                            {
                                int ox = c * cell + (row == 0 ? gx : Grid - 1 - gx) * f, oy = row * cell + gy * f;
                                for (int y = 0; y < f; y++)
                                    for (int x = 0; x < f; x++) px[(oy + y) * w + ox + x] = v;
                            }
                        }
                }
                return px;
            }

            static byte[] Frame(int c)
            {
                var g = new byte[Grid * Grid];
                switch (c)
                {
                    case 0: Stand(g, new[] { 4, 2, 6, 2, 9, 2, 11, 2 }, 0); break;
                    case 1: Stand(g, new[] { 4, 2, 6, 2, 9, 2, 11, 2 }, 1); break;
                    case 2: Stand(g, new[] { 4, 2, 6, 2, 9, 2, 11, 2 }, 0); break;
                    case 3: Stand(g, new[] { 3, 2, 7, 1, 8, 2, 12, 1 }, 1); break;
                    case 4: Stand(g, new[] { 4, 2, 6, 2, 9, 2, 11, 2 }, 0); break;
                    case 5: Stand(g, new[] { 5, 1, 6, 2, 10, 1, 11, 2 }, 1); break;
                    case 6: Sit(g, 0); break;
                    case 7: Sit(g, 1); break;
                    case 8: Sleep(g, 0); break;
                    default: Sleep(g, 1); break;
                }
                return g;
            }

            // legs: x and height pairs; a height of 1 is a raised paw
            static void Stand(byte[] g, int[] legs, int tail)
            {
                Box(g, 3, 8, 9, 4, Fur);
                Box(g, 10, 4, 5, 4, Fur);
                Dot(g, 10, 3, Fur); Dot(g, 14, 3, Fur);
                Dot(g, 13, 5, Dark); Dot(g, 14, 6, Nose);
                Dot(g, 12, 4, Stripe); Dot(g, 5, 8, Stripe); Dot(g, 7, 8, Stripe); Dot(g, 9, 8, Stripe);
                for (int i = 0; i < legs.Length; i += 2) Box(g, legs[i], 12, 1, legs[i + 1], Fur);
                if (tail == 0) { Dot(g, 2, 8, Fur); Dot(g, 1, 7, Fur); Dot(g, 1, 6, Fur); Dot(g, 2, 5, Fur); }
                else { Dot(g, 2, 9, Fur); Dot(g, 1, 8, Fur); Dot(g, 1, 7, Fur); Dot(g, 1, 6, Fur); }
            }

            static void Sit(byte[] g, int tail)
            {
                Box(g, 5, 7, 6, 5, Fur);
                Box(g, 4, 10, 2, 4, Fur);
                Box(g, 6, 12, 3, 2, Fur);
                Box(g, 10, 12, 1, 2, Fur);
                Box(g, 9, 3, 5, 4, Fur);
                Dot(g, 9, 2, Fur); Dot(g, 13, 2, Fur);
                Dot(g, 12, 4, Dark); Dot(g, 13, 5, Nose);
                Dot(g, 11, 3, Stripe); Dot(g, 6, 7, Stripe); Dot(g, 8, 7, Stripe);
                Box(g, 1, 13, 3, 1, Fur);
                Dot(g, tail == 0 ? 1 : 2, 12, Fur);
            }

            static void Sleep(byte[] g, int breath)
            {
                int top = 10 + breath;
                Box(g, 3, top, 9, 14 - top, Fur);
                Box(g, 10, 9, 4, 4, Fur);
                Dot(g, 10, 8, Fur); Dot(g, 13, 8, Fur);
                Box(g, 11, 10, 2, 1, Dark);
                Dot(g, 5, top, Stripe); Dot(g, 7, top, Stripe);
                Box(g, 2, 12, 1, 2, Fur);
                if (breath == 1)
                {
                    Box(g, 12, 3, 3, 1, Light);
                    Dot(g, 13, 4, Light);
                    Box(g, 12, 5, 3, 1, Light);
                }
            }

            static void Box(byte[] g, int x, int y, int w, int h, byte c)
            {
                for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++) Dot(g, xx, yy, c);
            }

            static void Dot(byte[] g, int x, int y, byte c)
            {
                if (x >= 0 && y >= 0 && x < Grid && y < Grid) g[y * Grid + x] = c;
            }

            static byte[] Outline(byte[] g)
            {
                var o = (byte[])g.Clone();
                for (int y = 0; y < Grid; y++)
                    for (int x = 0; x < Grid; x++)
                    {
                        if (g[y * Grid + x] != 0) continue;
                        if ((x > 0 && g[y * Grid + x - 1] != 0) || (x < Grid - 1 && g[y * Grid + x + 1] != 0) ||
                            (y > 0 && g[(y - 1) * Grid + x] != 0) || (y < Grid - 1 && g[(y + 1) * Grid + x] != 0))
                            o[y * Grid + x] = Dark;
                    }
                return o;
            }
        }

        // Layered window: clear pixels let clicks through, the cat itself takes hover and
        // clicks. The atlas is uploaded once; a frame picks its cell through pptSrc, and a
        // plain move skips the bitmap entirely.
        sealed class PetWindow : Form
        {
            const uint SWP_NOZORDER = 0x4;
            static readonly int FrameMsg = Taskbar.RegisterWindowMessage("DeskmatePetFrame");
            readonly PetBrain brain = new PetBrain(Environment.TickCount);
            readonly Stopwatch clock = Stopwatch.StartNew();
            readonly System.Windows.Forms.Timer rest = new System.Windows.Forms.Timer(), watch = new System.Windows.Forms.Timer { Interval = 1000 };
            DockPacer pacer;
            // floor: dock top edge in screen px, or Empty; lane: walkable left edges (X..Right) on ground Y
            Rectangle work, floor, lane;
            readonly Func<bool> away;
            float scale;
            IntPtr monitor, memDC, dib, oldBmp;
            int f, cell;
            double lastT;
            bool hidden, closing, wasMoving, placed;
            int waitTicks;
            int drawnX = int.MinValue, drawnY = int.MinValue, drawnCell = -1, drawnRow = -1;
            // Trial counters: frames run, bitmap uploads, moves without a bitmap, worst walking gap
            public int Frames, Uploads, Moves;
            public double MaxGapMs;

            public PetWindow(Rectangle work, float scale, IntPtr monitor, Func<bool> away)
            {
                this.work = work;
                this.scale = scale;
                this.monitor = monitor;
                this.away = away;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Text = "Deskmate Pet";
                Bounds = new Rectangle(-32000, -32000, 1, 1);
                rest.Tick += delegate { rest.Stop(); Frame(); };
                watch.Tick += delegate { Watch(); };
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
                pacer = new DockPacer(Handle, FrameMsg);
                Fit();
                lastT = clock.Elapsed.TotalSeconds;
                watch.Start();
                Frame();
            }

            public void Retarget(Rectangle work, float scale, IntPtr monitor)
            {
                this.work = work;
                this.scale = scale;
                this.monitor = monitor;
                Fit();
                if (placed) Settle();
            }

            // Where the pet may stand: on the dock floor, else the work area bottom.
            // A floor narrower than the sprite pins it to the middle.
            public static Rectangle Lane(Rectangle work, Rectangle floor, int cell)
            {
                if (floor.IsEmpty) return new Rectangle(work.Left, work.Bottom, Math.Max(0, work.Width - cell), 0);
                int room = floor.Width - cell;
                if (room < 0) return new Rectangle(floor.Left + room / 2, floor.Top, 0, 0);
                return new Rectangle(floor.Left, floor.Top, room, 0);
            }

            // dockPresent with an Empty floor: the dock is hidden or has not drawn yet, so the pet
            // keeps its lane (or waits for the first frame; Watch falls back after 3 s)
            public void Floor(Rectangle f, bool dockPresent)
            {
                if (f.IsEmpty && dockPresent) return;
                if (f == floor && placed) return;
                floor = f;
                Settle();
            }

            void Settle()
            {
                lane = Lane(work, floor, cell);
                brain.Bounds(lane.Left, lane.Right, !placed);
                if (placed) { Draw(); return; }
                placed = true;
                lastT = clock.Elapsed.TotalSeconds;
                drawnCell = -1;
                Frame();
            }

            void Fit()
            {
                int nf = Math.Max(2, (int)Math.Round(3 * scale));
                if (nf != f || memDC == IntPtr.Zero) { f = nf; cell = PetArt.Grid * f; Build(); }
                brain.Scale = scale;
                drawnCell = -1;
            }

            void Build()
            {
                Free();
                int w = cell * PetArt.Cells, h = cell * 2;
                int[] px = PetArt.Atlas(f);
                var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                memDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                IntPtr bits;
                dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
                oldBmp = Native.SelectObject(memDC, dib);
                Marshal.Copy(px, 0, bits, px.Length);
            }

            void Free()
            {
                if (memDC == IntPtr.Zero) return;
                Native.SelectObject(memDC, oldBmp);
                Native.DeleteObject(dib);
                Native.DeleteDC(memDC);
                memDC = dib = oldBmp = IntPtr.Zero;
            }

            void Frame()
            {
                if (closing || !placed) return;
                double now = clock.Elapsed.TotalSeconds, dt = now - lastT;
                lastT = now;
                if (wasMoving && brain.Moving && dt * 1000 > MaxGapMs) MaxGapMs = dt * 1000;
                Frames++;
                brain.NoWalk = Anim.Reduced;
                brain.Step(dt);
                wasMoving = brain.Moving;
                Draw();
                Schedule();
            }

            // Walking runs on vsync; standing still sleeps until the picture changes
            void Schedule()
            {
                if (pacer == null) return;
                if (hidden || closing) { pacer.Done(false); rest.Stop(); return; }
                if (brain.Moving)
                {
                    rest.Stop();
                    pacer.Want();
                    pacer.Done(true);
                    return;
                }
                pacer.Done(false);
                rest.Interval = Math.Max(16, (int)Math.Ceiling(brain.NextChangeIn() * 1000));
                rest.Start();
            }

            void Draw()
            {
                if (memDC == IntPtr.Zero || hidden || !placed) return;
                int x = (int)Math.Round(brain.X), c = brain.Cell, row = brain.Dir > 0 ? 0 : 1;
                // The outline row under the paws touches the ground
                int y = lane.Y - (PetArt.Grid - 1) * f - (int)Math.Round(brain.Lift);
                bool art = c != drawnCell || row != drawnRow;
                if (!art && x == drawnX && y == drawnY) return;
                if (art)
                {
                    var dst = new Native.POINT(x, y);
                    var size = new Native.SIZE(cell, cell);
                    var src = new Native.POINT(c * cell, row * cell);
                    var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                    Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
                    Uploads++;
                }
                else
                {
                    Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Moves++;
                }
                drawnX = x;
                drawnY = y;
                drawnCell = c;
                drawnRow = row;
            }

            // Out of the way of fullscreen games, video and presentations, and gone with a hidden Deskmate
            void Watch()
            {
                if (!placed && ++waitTicks >= 3) { floor = Rectangle.Empty; Settle(); }
                bool hide = (away != null && away()) || Native.IsFullscreenOn(monitor);
                if (hide == hidden) return;
                hidden = hide;
                if (hide) { Native.ShowWindow(Handle, Native.SW_HIDE); Schedule(); return; }
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                lastT = clock.Elapsed.TotalSeconds;
                drawnCell = -1;
                Frame();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == FrameMsg) { Frame(); return; }
                if (m.Msg == 0x200 && placed)
                {
                    if (brain.Notice(Cursor.Position.X - (Left + cell / 2))) Frame();
                    return;
                }
                if (m.Msg == 0x201 && placed) { brain.Hop(); Frame(); return; }
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                closing = true;
                rest.Stop();
                watch.Stop();
                if (pacer != null) pacer.Stop();
                Free();
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { rest.Dispose(); watch.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
