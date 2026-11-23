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
            petReactUtc = now;
            pet.React(m == EyeMood.Happy);
        }

        enum PetEmote { Heart, Bang, Note, Spark }

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

            // A Deskmate moment: surprise turns toward the island with a small hop, joy hops and sparkles
            public void React(bool happy)
            {
                if (!placed || hidden || closing || sheet == null) return;
                var mode = happy ? PetMode.Happy : PetMode.Surprised;
                if (!brain.React(mode, Math.Max(happy ? 1.4 : 1.0, sheet.Length(mode)), happy ? 1 : 0.5)) return;
                if (!happy) brain.Dir = work.Left + work.Width / 2 >= drawnX + cw / 2 ? 1 : -1;
                Emote(happy ? PetEmote.Spark : PetEmote.Bang);
                Frame();
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
                int cx = drawnX + cw / 2 + brain.Dir * cw / 4;
                emote.Pop(kind, cx, drawnY + sheet.Top * f - factor, factor, clock.Elapsed.TotalSeconds, Anim.Reduced);
            }

            // Once a second: music, a focus session, and whether anyone is at the keyboard
            void Cue()
            {
                if (Isle == null || !placed || sheet == null) return;
                bool react = Isle.PetReactOn;
                bool calm = react && Isle.FocusHold;
                bool groove = react && !calm && Isle.mediaInfo != null && Isle.mediaInfo.Playing;
                double idle = IdleSeconds();
                bool wasAway = brain.Away;
                if (!wasAway && react && idle >= AwayAfter) brain.SetAway(true, 0);
                else if (wasAway && (!react || idle < 2)) brain.SetAway(false, Math.Max(1.2, sheet.Length(PetMode.Stretch)));
                bool changed = wasAway != brain.Away || groove != brain.Groove || calm != brain.Calm;
                brain.Groove = groove;
                brain.Calm = calm;
                if (groove && !grooveWas && !brain.Away) Emote(PetEmote.Note);
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
                double u = (now - start) / Life;
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
            };

            static readonly int[][] Inks =
            {
                new[] { unchecked((int)0xFFF0597A), unchecked((int)0xFFFFB3C4) },
                new[] { unchecked((int)0xFFFFC93C), unchecked((int)0xFFFFF0B0) },
                new[] { unchecked((int)0xFFF5F1EA), unchecked((int)0xFFFFFFFF) },
                new[] { unchecked((int)0xFFFFD45A), unchecked((int)0xFFFFFFFF) },
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
