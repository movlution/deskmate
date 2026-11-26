using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Pet reactions: Deskmate moments (notifications, finished work), music, focus sessions, the user
    // stepping away, and petting. Ambient cues are read on the pet's one-second watch; nothing polls faster.
    partial class Island
    {
        DateTime petReactUtc = DateTime.MinValue;

        bool PetReactOn { get { return settings.GetBool("pet.react", true); } }

        // Every island mood passes here before the eyes decide whether to show it
        void PetReact(EyeMood m)
        {
            if (pet == null || pet.IsDisposed || !PetOn || !PetReactOn) return;
            if (m != EyeMood.Happy && (m != EyeMood.Surprised || Quiet)) return;
            // Typing combos can fire moods in bursts; the pet answers one at a time
            var now = DateTime.UtcNow;
            if ((now - petReactUtc).TotalSeconds < 3) return;
            if (pet.React(m == EyeMood.Happy)) petReactUtc = now;
        }

        enum PetEmote { Heart, Bang, Note, Spark, Scratch }

        // Back-and-forth strokes over the pet: three turns, each within 0.6 s of the last, is petting
        sealed class PetStroke
        {
            int lastX = int.MinValue, dir, turns;
            double lastTurn = -10;

            public bool Feed(int x, double t, int minStep)
            {
                if (lastX == int.MinValue) { lastX = x; return false; }
                int dx = x - lastX;
                if (Math.Abs(dx) < minStep) return false;
                lastX = x;
                int d = Math.Sign(dx);
                if (dir != 0 && d != dir)
                {
                    turns = t - lastTurn <= 0.6 ? turns + 1 : 1;
                    lastTurn = t;
                }
                dir = d;
                if (turns < 3) return false;
                turns = 0;
                return true;
            }
        }

        sealed partial class PetWindow
        {
            // Seconds without keyboard or mouse input before the pet naps
            const double AwayAfter = 180;
            internal Island Isle;
            readonly PetStroke stroke = new PetStroke();
            PetEmoteWindow emote;
            bool grooveWas;
            bool petPressed;
            Point pressAt;
            int pressButton;
            double pressX;
            // Screen Y of the paws when the press began; a drag keeps the paws this far from the pointer
            int pressFeet;
            bool holdVelValid;
            double holdLastX, holdLastLift, holdLastT, holdVx, holdVy;
            bool autoChase, autoFullscreen;
            double nextAutoSample, nextAutoFullscreen, nextDockFullscreen;

            bool FocusActive { get { return Isle != null && Isle.timerKind == TimerKind.Focus && !Isle.timerPaused; } }
            bool PlayBlocked { get { return hidden || closing || Anim.Reduced || FocusActive; } }
            double StretchSeconds { get { return Math.Max(1.2, sheet.Length(PetMode.Stretch)); } }

            public void DockTrick(double iconX, bool scratch)
            {
                if (!placed || floor.IsEmpty || sheet == null || PlayBlocked || petPressed) return;
                double now = clock.Elapsed.TotalSeconds;
                nextDockFullscreen = 0;
                if (!DockTrickVisible(now)) return;
                if (!brain.TryDockTrick(iconX, cw / 2.0, scratch, scratch ? sheet.Length(PetMode.Scratch) : 0.75, now)) return;
                if (scratch)
                {
                    if (emote == null || emote.IsDisposed) emote = new PetEmoteWindow { Owner = this };
                    emote.Pop(PetEmote.Scratch, (int)Math.Round(iconX), StandY - 3, Math.Max(1, f), now, false);
                }
                lastT = now;
                Frame();
            }

            bool DockTrickVisible(double now)
            {
                var dock = Owner as DockWindow;
                if (dock == null || !dock.PetIconReady || (away != null && away())) return false;
                if (now < nextDockFullscreen) return true;
                nextDockFullscreen = now + 0.12;
                return !Native.IsFullscreenOn(monitor);
            }

            void BeginPetPress()
            {
                if (hidden || closing || petPressed || brain.Manipulating) return;
                brain.NoWalk = Anim.Reduced;
                brain.Calm = FocusActive;
                if (PlayBlocked || brain.Away)
                {
                    // Nothing to drag here, so the press is the click
                    brain.Click(StretchSeconds);
                    ToggleActions();
                }
                else
                {
                    if (Native.IsFullscreenOn(monitor)) return;
                    brain.StopPlay();
                    petPressed = true;
                    pressAt = Cursor.Position;
                    pressButton = SystemInformation.MouseButtonsSwapped ? 0x02 : 0x01;
                    pressX = brain.X;
                    pressFeet = drawnY != int.MinValue ? drawnY + BasePx : StandY;
                    ClearHoldSamples();
                    if (emote != null) emote.Stop();
                }
                lastT = clock.Elapsed.TotalSeconds;
                Frame();
            }

            void ClearHoldSamples()
            {
                holdVelValid = false;
                holdVx = 0;
                holdVy = 0;
            }

            void RecordHoldSample(double x, double lift, double now)
            {
                if (!holdVelValid)
                {
                    holdVelValid = true;
                    holdLastX = x;
                    holdLastLift = lift;
                    holdLastT = now;
                    holdVx = 0;
                    holdVy = 0;
                    return;
                }
                double dt = now - holdLastT;
                if (dt < 0.002) return;
                if (dt > 0.12)
                {
                    holdVx = 0;
                    holdVy = 0;
                }
                else if (scale > 0)
                {
                    double instVx = (x - holdLastX) / (dt * scale);
                    double instVy = (lift - holdLastLift) / (dt * scale);
                    // Exponential smoothing (~45ms) filters out mouse polling jitter
                    double alpha = 1.0 - Math.Exp(-dt / 0.045);
                    holdVx += (instVx - holdVx) * alpha;
                    holdVy += (instVy - holdVy) * alpha;
                }
                holdLastX = x;
                holdLastLift = lift;
                holdLastT = now;
            }

            void ComputeThrowVelocity(double now, out double vx, out double vy)
            {
                vx = 0;
                vy = 0;
                if (!holdVelValid) return;
                double stale = now - holdLastT;
                // Pausing before release drops straight down instead of flinging
                if (stale > 0.08) return;
                double decay = stale > 0 ? Math.Exp(-stale / 0.045) : 1.0;
                vx = holdVx * decay;
                vy = holdVy * decay;
            }

            // The pet never takes focus. A background window cannot rely on mouse capture,
            // so the existing frame pacer observes the button only after a press on the pet.
            void UpdatePetPress()
            {
                if (PlayBlocked || (away != null && away())) { CancelPetPress(); return; }
                Point at = Cursor.Position;
                if (KeyState(pressButton) >= 0) { EndPetPress(at); return; }
                double now = clock.Elapsed.TotalSeconds;
                if (!brain.Holding && PetBrain.DragThreshold(pressAt, at, scale))
                {
                    brain.BeginHold();
                    RecordHoldSample(brain.X, brain.Lift, now);
                }
                if (brain.Holding)
                {
                    CarryTo(at);
                    RecordHoldSample(brain.X, brain.Lift, now);
                }
            }

            // Anywhere on the pet's screen: the paws follow the pointer, lifted a little on pickup, never
            // below the ground under them and never above the top of the work area
            void CarryTo(Point at)
            {
                double left = work.Left, right = Math.Max(work.Left, work.Right - cw);
                double x = Math.Max(left, Math.Min(right, pressX + at.X - pressAt.X));
                bool onDock;
                int ground = GroundAt(x + cw / 2.0, out onDock);
                double feet = pressFeet - 8 * scale + (at.Y - pressAt.Y);
                double maxLift = ground - work.Top - (BasePx - TopPx);
                brain.MoveHeldFree(x, ground - feet, left, right, maxLift);
            }

            void EndPetPress(Point at)
            {
                petPressed = false;
                if (PlayBlocked) { ClearHoldSamples(); brain.CancelHold(); return; }
                if (brain.Holding)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    CarryTo(at);
                    RecordHoldSample(brain.X, brain.Lift, now);
                    double vx, vy;
                    ComputeThrowVelocity(now, out vx, out vy);
                    ClearHoldSamples();
                    bool onDock;
                    int ground = GroundAt(brain.X + cw / 2.0, out onDock);
                    double maxLift = ground - work.Top - (BasePx - TopPx);
                    brain.EndHold(vx, vy, work.Left, Math.Max(work.Left, work.Right - cw), maxLift);
                }
                else if (!PetBrain.DragThreshold(pressAt, at, scale))
                {
                    ClearHoldSamples();
                    // A click hops, and opens (or closes) the action bar; a drag only carries the pet
                    brain.Click(StretchSeconds);
                    ToggleActions();
                }
                else ClearHoldSamples();
                lastT = clock.Elapsed.TotalSeconds;
            }

            void CancelPetPress()
            {
                petPressed = false;
                ClearHoldSamples();
                brain.CancelHold();
            }

            public void SetAutoChase(bool on)
            {
                if (autoChase == on) return;
                autoChase = on;
                if (!on) brain.StopPlay();
                if (placed) Frame();
            }

            bool CanProbeAutoChase(double now)
            {
                return autoChase && (!floor.IsEmpty || screenArea) && !PlayBlocked && brain.CanProbeAuto(now);
            }

            // Logical px around the lane's top edge: the bar below it, a strip above it
            const double ChaseBelow = 96, AutoAbove = 24, AutoNear = 160;

            // Screen px. The chase follows the cursor along the dock itself, not over the windows above it;
            // margin widens the band on every side.
            public static bool InChaseBand(Rectangle lane, int cell, float scale, Point at, double above, double margin)
            {
                double m = margin * scale;
                return at.X >= lane.Left - m && at.X < lane.Right + cell + m &&
                    at.Y >= lane.Y - (above + margin) * scale && at.Y <= lane.Y + (ChaseBelow + margin) * scale;
            }

            bool InAutoChaseBand(Point at) { return (!floor.IsEmpty || screenArea) && InChaseBand(StandLane, cw, scale, at, AutoAbove, 0); }

            // Near enough that a sweep could begin: sample quickly; otherwise a slow glance is enough
            bool AutoChaseNear(Point at) { return (!floor.IsEmpty || screenArea) && InChaseBand(StandLane, cw, scale, at, AutoAbove, AutoNear); }

            void ProbeAutoChase(double now)
            {
                if (!CanProbeAutoChase(now) || now < nextAutoSample) return;
                nextAutoSample = now + 0.055;
                Point at = Cursor.Position;
                if (!brain.ProbeAuto(at, now, cw / 2.0, InAutoChaseBand(at) && MouseButtons == MouseButtons.None)) return;
                nextAutoFullscreen = 0;
                if (!AutoChaseAllowed(at, now)) brain.StopPlay();
            }

            bool AutoChaseAllowed(Point at, double now)
            {
                if (!autoChase || !InAutoChaseBand(at) || MouseButtons != MouseButtons.None ||
                    (away != null && away())) return false;
                if (now >= nextAutoFullscreen)
                {
                    autoFullscreen = Native.IsFullscreenOn(monitor);
                    nextAutoFullscreen = now + 0.12;
                }
                return !autoFullscreen;
            }

            // A Deskmate moment: surprise turns toward the island with a small hop, joy hops and sparkles
            public bool React(bool happy)
            {
                if (!placed || hidden || closing || sheet == null || FocusActive || petPressed || brain.Manipulating || brain.DockTrickActive || brain.Playing) return false;
                brain.NoWalk = Anim.Reduced;
                brain.Calm = false;
                var mode = happy ? PetMode.Happy : PetMode.Surprised;
                if (!brain.React(mode, Math.Max(happy ? 1.4 : 1.0, sheet.Length(mode)), happy ? 1 : 0.5)) return false;
                if (!happy) brain.Dir = work.Left + work.Width / 2 >= drawnX + cw / 2 ? 1 : -1;
                Emote(happy ? PetEmote.Spark : PetEmote.Bang);
                Frame();
                return true;
            }

            // A sleeping pet keeps sleeping, contented
            void Petted()
            {
                if (brain.Mode != PetMode.Sleep) brain.React(PetMode.Happy, Math.Max(1.4, sheet.Length(PetMode.Happy)), 0);
                Emote(PetEmote.Heart);
                Frame();
            }

            // Above the head, which leads in the facing direction; the caller runs Frame
            void Emote(PetEmote kind)
            {
                if (hidden || !placed || sheet == null || drawnX == int.MinValue) return;
                if (emote == null || emote.IsDisposed) emote = new PetEmoteWindow { Owner = this };
                int factor = Math.Max(2, f);
                int dir = IsRemote ? remoteDir : brain.Dir;
                int cx = drawnX + cw / 2 + dir * cw / 4;
                emote.Pop(kind, cx, drawnY + TopPx - factor, factor, clock.Elapsed.TotalSeconds, Anim.Reduced);
            }

            // Once a second: music, a focus session, and whether anyone is at the keyboard
            void Cue()
            {
                if (Isle == null || !placed || sheet == null) return;
                bool react = Isle.PetReactOn;
                bool calm = FocusActive;
                bool groove = react && !calm && Isle.mediaInfo != null && Isle.mediaInfo.Playing;
                double idle = IdleSeconds();
                bool wasAway = brain.Away;
                if (!wasAway && react && idle >= AwayAfter) brain.SetAway(true, 0);
                else if (wasAway && (!react || idle < 2)) brain.SetAway(false, StretchSeconds);
                bool changed = wasAway != brain.Away || groove != brain.Groove || calm != brain.Calm;
                brain.Groove = groove;
                brain.Calm = calm;
                if (groove && !grooveWas && !brain.Away && !petPressed && !brain.Manipulating) Emote(PetEmote.Note);
                if (changed) Frame();
                grooveWas = groove;
            }
        }

        // A small glyph that rises and fades above the pet. Click-through; frames come from the pet's pacer.
        sealed class PetEmoteWindow : Form
        {
            public const double Life = 0.9;
            IntPtr memDC, dib, oldBmp;
            int f, cell, kind = -1, x, y;
            double start;
            bool still, shown;

            public PetEmoteWindow()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Text = "Deskmate Pet Emote";
                Bounds = new Rectangle(-32000, -32000, 1, 1);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style = Native.WS_POPUP;
                    cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            protected override void OnPaint(PaintEventArgs e) { }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            public bool Active { get { return kind >= 0; } }

            // centerX, bottom: screen px of the spot above the pet's head; reduced motion keeps it still
            public void Pop(PetEmote what, int centerX, int bottom, int factor, double now, bool reduced)
            {
                if (factor != f || memDC == IntPtr.Zero) Build(factor);
                if (memDC == IntPtr.Zero) return;
                kind = (int)what;
                x = centerX - cell / 2;
                y = bottom - cell;
                start = now;
                still = reduced;
                if (!shown) { Show(); shown = true; }
                Step(now);
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            }

            // False once it has faded
            public bool Step(double now)
            {
                if (kind < 0) return false;
                double u = (now - start) / (kind == (int)PetEmote.Scratch ? PetBrain.HopTime : Life);
                if (u >= 1 || u < 0) { Stop(); return false; }
                int rise = still ? 0 : (int)Math.Round((1 - (1 - u) * (1 - u)) * 4 * f);
                byte a = (byte)(u < 0.55 ? 255 : Math.Round(255 * (1 - (u - 0.55) / 0.45)));
                var dst = new Native.POINT(x, y - rise);
                var size = new Native.SIZE(cell, cell);
                var src = new Native.POINT(kind * cell, 0);
                var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = a, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
                return true;
            }

            public void Stop()
            {
                if (kind < 0) return;
                kind = -1;
                if (IsHandleCreated) Native.ShowWindow(Handle, Native.SW_HIDE);
            }

            void Build(int factor)
            {
                Free();
                f = factor;
                cell = PetEmoteArt.Cell * f;
                int w = cell * PetEmoteArt.Count, h = cell;
                int[] px = PetEmoteArt.Atlas(f);
                var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                memDC = Native.CreateCompatibleDC(screen);
                Native.ReleaseDC(IntPtr.Zero, screen);
                IntPtr bits;
                dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) { Native.DeleteDC(memDC); memDC = IntPtr.Zero; return; }
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

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                kind = -1;
                Free();
                base.OnFormClosed(e);
            }
        }

        // Emote glyphs on a 7 px grid inside a 9 px cell, outlined like the pets. '+' is the highlight.
        static class PetEmoteArt
        {
            public const int Cell = 9;
            const int Outline = unchecked((int)0xFF2A1E17);

            static readonly string[][] Maps =
            {
                new[] { ".##.##.", "#+#####", "#######", ".#####.", "..###..", "...#..." },
                new[] { "..###..", "..###..", "..###..", "..###..", ".......", "..###.." },
                new[] { "...##..", "...#.#.", "...#..#", "...#...", ".###...", "####...", ".##...." },
                new[] { "...#...", "...#...", "..###..", "###+###", "..###..", "...#...", "...#..." },
                new[] { "....#.#", "...#.#.", "..+.#.#", ".#.#.#.", "#.#.#..", ".#.#...", "#.#...." },
            };

            static readonly int[][] Inks =
            {
                new[] { unchecked((int)0xFFF0597A), unchecked((int)0xFFFFB3C4) },
                new[] { unchecked((int)0xFFFFC93C), unchecked((int)0xFFFFF0B0) },
                new[] { unchecked((int)0xFFF5F1EA), unchecked((int)0xFFFFFFFF) },
                new[] { unchecked((int)0xFFFFD45A), unchecked((int)0xFFFFFFFF) },
                new[] { unchecked((int)0xFFFDE6A5), unchecked((int)0xFFFFFFFF) },
            };

            public static int Count { get { return Maps.Length; } }

            // Opaque or clear pixels only, so the result is already premultiplied
            public static int[] Atlas(int f)
            {
                int c = Cell * f, w = c * Maps.Length;
                var px = new int[w * c];
                for (int k = 0; k < Maps.Length; k++)
                {
                    var g = new int[Cell * Cell];
                    var map = Maps[k];
                    for (int yy = 0; yy < map.Length; yy++)
                        for (int xx = 0; xx < map[yy].Length; xx++)
                        {
                            char ch = map[yy][xx];
                            if (ch != '.') g[(yy + 1) * Cell + xx + 1] = ch == '+' ? Inks[k][1] : Inks[k][0];
                        }
                    var o = (int[])g.Clone();
                    for (int yy = 0; yy < Cell; yy++)
                        for (int xx = 0; xx < Cell; xx++)
                        {
                            if (g[yy * Cell + xx] != 0) continue;
                            if ((xx > 0 && g[yy * Cell + xx - 1] != 0) || (xx < Cell - 1 && g[yy * Cell + xx + 1] != 0) ||
                                (yy > 0 && g[(yy - 1) * Cell + xx] != 0) || (yy < Cell - 1 && g[(yy + 1) * Cell + xx] != 0))
                                o[yy * Cell + xx] = Outline;
                        }
                    for (int gy = 0; gy < Cell; gy++)
                        for (int gx = 0; gx < Cell; gx++)
                        {
                            int v = o[gy * Cell + gx];
                            if (v == 0) continue;
                            for (int yy = 0; yy < f; yy++)
                                for (int xx = 0; xx < f; xx++) px[(gy * f + yy) * w + k * c + gx * f + xx] = v;
                        }
                }
                return px;
            }
        }
    }
}
