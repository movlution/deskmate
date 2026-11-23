using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Desktop pet: a small animal walking on the dock's top edge, or on the bottom
    // of the work area without a dock. Opt-in through pet.on.
    partial class Island
    {
        PetWindow pet;
        PetSheet petSheet;
        // petError: why the chosen custom pet fell back to the built-in one, as a Lang id
        string petSheetKey, petError;

        bool PetOn { get { return settings.GetBool("pet.on", false); } }
        string PetKind { get { return settings.Get("pet.kind", "cat"); } }
        // Logical px tall
        int PetHeight { get { string s = settings.Get("pet.size", "m"); return s == "s" ? 32 : s == "l" ? 64 : 48; } }
        // Percent of the base walking speed
        int PetSpeed { get { return Math.Max(50, Math.Min(200, settings.GetInt("pet.speed", 100))); } }

        void SetPetChoice(string key, object value)
        {
            settings.Set(key, value);
            settings.Save();
            ApplyPet();
        }

        void ApplyPet()
        {
            if (!PetOn) { ClosePet(); return; }
            var sheet = LoadPetSheet();
            var dock = dockWin != null && !dockWin.IsDisposed ? dockWin : null;
            var screen = dock != null && dock.PetScreen != null ? dock.PetScreen : ChosenScreen();
            IntPtr mon = MonitorForScreen(screen);
            Rectangle work = screen.WorkingArea.IsEmpty ? screen.Bounds : screen.WorkingArea;
            float s = MonitorScale(mon);
            if (pet == null || pet.IsDisposed)
            {
                pet = new PetWindow(work, s, mon, () => dormant || hiddenByUser) { Isle = this };
                pet.Use(sheet, PetHeight, PetSpeed);
                pet.Show();
            }
            else
            {
                pet.Use(sheet, PetHeight, PetSpeed);
                pet.Retarget(work, s, mon);
            }
            // Owned by the dock: always just above it, and the dock's later windows stay above both
            pet.Owner = dock;
            pet.Floor(dock != null ? dock.PetFloor() : Rectangle.Empty, dock != null);
        }

        // Built-in art, or the chosen folder under pets\. Reloaded only when the choice
        // or the files change; a broken custom pet falls back to the cat and says why.
        PetSheet LoadPetSheet()
        {
            string kind = PetKind, color = settings.Get("pet.color", "orange"), key = kind + "|" + color, dir = null;
            if (kind.StartsWith("custom:", StringComparison.Ordinal))
            {
                string name = kind.Substring(7);
                if (name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                    dir = Path.Combine(PetSheet.Folder, name);
                try
                {
                    if (dir != null)
                        key += "|" + File.GetLastWriteTimeUtc(Path.Combine(dir, "pet.json")).Ticks + "|" + File.GetLastWriteTimeUtc(Path.Combine(dir, "sheet.png")).Ticks;
                }
                catch (Exception) { }
            }
            if (petSheet != null && key == petSheetKey) return petSheet;
            petError = null;
            PetSheet sheet = null;
            if (kind.StartsWith("custom:", StringComparison.Ordinal))
            {
                if (dir == null || !Directory.Exists(dir)) petError = "folder pet tidak ditemukan";
                else
                {
                    try { sheet = PetSheet.Load(dir); }
                    catch (InvalidDataException ex) { petError = ex.Message; }
                    catch (Exception ex) { petError = "sheet.png tidak terbaca"; Log.Write("pet: " + ex.Message); }
                }
                if (petError != null) Log.Write("pet: " + kind + ": " + petError);
            }
            if (sheet == null) sheet = PetSheet.BuiltIn(kind == "dog" ? "dog" : "cat", color);
            petSheet = sheet;
            petSheetKey = key;
            return sheet;
        }

        void OpenPetFolder()
        {
            try
            {
                Directory.CreateDirectory(PetSheet.Folder);
                Process.Start("explorer.exe", "\"" + PetSheet.Folder + "\"");
            }
            catch (Exception ex) { Log.Write("pet folder: " + ex.Message); }
        }

        // A closing owner would take the pet with it
        void DetachPet() { if (pet != null && !pet.IsDisposed) pet.Owner = null; }

        // Every dock frame: the pet rides the bar as it rises and drops
        void PetFollow(DockWindow d) { if (pet != null && !pet.IsDisposed && d == dockWin) pet.Floor(d.PetFloor(), true); }

        void ClosePet()
        {
            if (pet != null && !pet.IsDisposed) pet.Close();
            pet = null;
            // Switching off and on again reloads a custom pet
            petSheet = null;
            petSheetKey = null;
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

        // Happy, Surprised and Stretch are short reactions that return to Idle
        enum PetMode { Idle, Walk, Sit, Sleep, Happy, Surprised, Stretch }

        // Time-driven behaviour with no window, so tests can step it
        sealed class PetBrain
        {
            public PetMode Mode = PetMode.Idle;
            // Left edge of the sprite and its walkable range, px
            public double X, Left, Right;
            public int Dir = 1;
            // Logical px per second; scaled by Scale
            public double Speed = BaseSpeed;
            public const double BaseSpeed = 36;
            public float Scale = 1;
            // Reduced motion: the pet stays where it is
            public bool NoWalk;
            double modeLeft = 2, animT, targetX, hopT = -1, hopScale = 1;
            public const double HopTime = 0.4, HopHeight = 12, BobStep = 0.25;
            // Ambient cues, refreshed once a second: music playing, a focus session
            public bool Groove, Calm;
            bool away;
            readonly Random rnd;

            public PetBrain(int seed) { rnd = new Random(seed); }

            // Seconds into the current mode, for the sheet's frame timing
            public double AnimT { get { return animT; } }

            // Seconds until the mode changes on its own; a walk ends where it arrives
            public double ModeLeft { get { return Mode == PetMode.Walk ? double.MaxValue : Math.Max(0.001, modeLeft); } }

            // The walk cycle follows ground speed
            public double Pace { get { return Mode == PetMode.Walk ? Speed / BaseSpeed : 1; } }

            // Walking or mid-hop: frames run on vsync
            public bool Moving { get { return Mode == PetMode.Walk || hopT >= 0; } }

            public bool Reacting { get { return Mode == PetMode.Happy || Mode == PetMode.Surprised || Mode == PetMode.Stretch; } }

            // Asleep because nobody is at the keyboard
            public bool Away { get { return away; } }

            // Bobs to music while standing or sitting; never with reduced motion
            bool Bobbing { get { return Groove && !NoWalk && (Mode == PetMode.Idle || Mode == PetMode.Sit); } }

            // Seconds until the bob flips, or MaxValue when not bobbing
            public double NextBob { get { return Bobbing ? BobStep - animT % BobStep : double.MaxValue; } }

            // A reaction pose for a while, with a hop scaled by hop (0 for none). Nobody to react to while away.
            public bool React(PetMode m, double seconds, double hop)
            {
                if (away) return false;
                Enter(m);
                modeLeft = seconds;
                if (hop > 0 && !NoWalk) { hopT = 0; hopScale = hop; }
                return true;
            }

            // Nobody at the keyboard: sleep until they return, then stretch
            public void SetAway(bool on, double stretchSeconds)
            {
                if (on == away) return;
                away = on;
                Enter(on ? PetMode.Sleep : PetMode.Stretch);
                modeLeft = on ? 1e9 : stretchSeconds;
            }

            // Screen px above the ground: one parabolic hop
            public double Lift
            {
                get
                {
                    if (hopT >= 0)
                    {
                        double u = hopT / HopTime;
                        return 4 * u * (1 - u) * HopHeight * hopScale * Scale;
                    }
                    return Bobbing && (int)(animT / BobStep) % 2 == 1 ? 2 * Scale : 0;
                }
            }

            // Click: a sleeping or sitting pet wakes; reduced motion skips the hop itself
            public void Hop()
            {
                if (Mode == PetMode.Sleep || Mode == PetMode.Sit) Enter(PetMode.Idle);
                if (!NoWalk) { hopT = 0; hopScale = 1; }
            }

            // Pointer over the pet: a walk stops and it looks that way (dx > 0: pointer to the right).
            // True when the picture changes. A sleeping pet is not woken by hover.
            public bool Notice(int dx)
            {
                // A reaction keeps its pose; petting must not flip it side to side
                if (Mode == PetMode.Sleep || Reacting) return false;
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
                // A focus session keeps the pet off its feet
                if (Reacting) Enter(PetMode.Idle);
                else if (Mode == PetMode.Idle) { if (r < 0.65 && !Calm) StartWalk(); else Enter(PetMode.Sit); }
                else if (Mode == PetMode.Sit) { if (r < (Calm ? 0.5 : 0.3)) Enter(PetMode.Sleep); else if (r < 0.65 && !Calm) StartWalk(); else Enter(PetMode.Idle); }
                else Enter(PetMode.Sit);
            }

            void StartWalk()
            {
                double min = 60 * Scale, reach = 420 * Scale;
                double lo = Math.Max(Left, X - reach), hi = Math.Min(Right, X + reach);
                if (NoWalk || Calm || hi - lo < min) { Enter(PetMode.Sit); return; }
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
                modeLeft = m == PetMode.Idle ? 2 + r * 3 : m == PetMode.Sit ? 4 + r * 5 : m == PetMode.Sleep ? 20 + r * 25 : m == PetMode.Walk ? 0 : 1.2;
            }
        }

        // Layered window: clear pixels let clicks through, the pet itself takes hover and
        // clicks. The atlas is uploaded once; a frame picks its cell through pptSrc, and a
        // plain move skips the bitmap entirely.
        sealed partial class PetWindow : Form
        {
            const uint SWP_NOZORDER = 0x4;
            // Atlas memory ceiling; a large custom sheet is drawn smaller instead
            const long AtlasBytes = 32L << 20;
            static readonly int FrameMsg = Taskbar.RegisterWindowMessage("DeskmatePetFrame");
            readonly PetBrain brain = new PetBrain(Environment.TickCount);
            readonly Stopwatch clock = Stopwatch.StartNew();
            readonly System.Windows.Forms.Timer rest = new System.Windows.Forms.Timer(), watch = new System.Windows.Forms.Timer { Interval = 1000 };
            DockPacer pacer;
            // floor: dock top edge in screen px, or Empty; lane: walkable left edges (X..Right) on ground Y
            Rectangle work, floor, lane;
            readonly Func<bool> away;
            PetSheet sheet;
            int height = 48;
            float scale;
            IntPtr monitor, memDC, dib, oldBmp;
            // f: screen px per art px; cw, ch: one atlas cell
            int f, cw, ch;
            double lastT;
            bool hidden, closing, wasMoving, placed, rebuild;
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

            // Look and pace. A new sheet or size rebuilds the atlas; speed alone does not.
            public void Use(PetSheet sheet, int height, int speedPercent)
            {
                if (sheet != this.sheet || height != this.height) rebuild = true;
                this.sheet = sheet;
                this.height = height;
                brain.Speed = PetBrain.BaseSpeed * speedPercent / 100.0;
                if (!IsHandleCreated || !rebuild) return;
                Fit();
                if (placed) Settle();
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

            // Whole screen px per art px for a sheet drawn height logical px tall, kept under the atlas ceiling
            public static int Factor(PetSheet sheet, int height, float scale)
            {
                int n = Math.Max(1, (int)Math.Round(height * scale / sheet.H));
                while (n > 1 && (long)sheet.W * sheet.H * n * n * sheet.Count * 8 > AtlasBytes) n--;
                return n;
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
                if (sheet == null) return;
                lane = Lane(work, floor, cw);
                brain.Bounds(lane.Left, lane.Right, !placed);
                if (placed) { Draw(); return; }
                placed = true;
                lastT = clock.Elapsed.TotalSeconds;
                drawnCell = -1;
                Frame();
            }

            void Fit()
            {
                if (sheet == null) return;
                int nf = Factor(sheet, height, scale);
                if (nf != f || rebuild || memDC == IntPtr.Zero)
                {
                    f = nf;
                    cw = sheet.W * f;
                    ch = sheet.H * f;
                    rebuild = false;
                    Build();
                }
                brain.Scale = scale;
                drawnCell = -1;
            }

            void Build()
            {
                Free();
                int w = cw * sheet.Count, h = ch * 2;
                int[] px = sheet.Atlas(f);
                var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                memDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                IntPtr bits;
                dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) { Native.DeleteDC(memDC); memDC = IntPtr.Zero; Log.Write("pet: atlas " + w + "x" + h + " failed"); return; }
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
                if (emote != null) emote.Step(now);
                Schedule();
            }

            // Walking runs on vsync; standing still sleeps until the picture changes
            void Schedule()
            {
                if (pacer == null) return;
                if (hidden || closing) { pacer.Done(false); rest.Stop(); return; }
                if (brain.Moving || (emote != null && emote.Active))
                {
                    rest.Stop();
                    pacer.Want();
                    pacer.Done(true);
                    return;
                }
                pacer.Done(false);
                double wait = Math.Min(Math.Min(sheet.NextChange(brain.Mode, brain.AnimT, brain.Pace), brain.ModeLeft), brain.NextBob);
                rest.Interval = Math.Max(16, (int)Math.Min(int.MaxValue, Math.Ceiling(wait * 1000)));
                rest.Start();
            }

            void Draw()
            {
                if (memDC == IntPtr.Zero || hidden || !placed) return;
                int x = (int)Math.Round(brain.X), c = sheet.Frame(brain.Mode, brain.AnimT, brain.Pace), row = brain.Dir > 0 ? 0 : 1;
                // The lowest opaque row of the sheet stands on the ground
                int y = lane.Y - (sheet.Base + 1) * f - (int)Math.Round(brain.Lift);
                bool art = c != drawnCell || row != drawnRow;
                if (!art && x == drawnX && y == drawnY) return;
                if (art)
                {
                    var dst = new Native.POINT(x, y);
                    var size = new Native.SIZE(cw, ch);
                    var src = new Native.POINT(c * cw, row * ch);
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
                Cue();
                bool hide = (away != null && away()) || Native.IsFullscreenOn(monitor);
                if (hide == hidden) return;
                hidden = hide;
                if (hide) { if (emote != null) emote.Stop(); Native.ShowWindow(Handle, Native.SW_HIDE); Schedule(); return; }
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
                    bool turned = brain.Notice(Cursor.Position.X - (Left + cw / 2));
                    if (stroke.Feed(Cursor.Position.X, clock.Elapsed.TotalSeconds, Math.Max(2, f))) Petted();
                    else if (turned) Frame();
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
                if (emote != null && !emote.IsDisposed) { emote.Close(); emote.Dispose(); }
                emote = null;
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
