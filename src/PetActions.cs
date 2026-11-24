using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // One button of the pet's action bar: a pixel icon, a short label, and what it does
        sealed class PetAction
        {
            public readonly string[] Icon;
            public readonly string Label;
            public readonly bool Enabled;
            public readonly Action Run;

            public PetAction(string[] icon, string label, bool enabled, Action run)
            {
                Icon = icon;
                Label = label;
                Enabled = enabled;
                Run = run;
            }
        }

        // 7 x 7 icons in the pet's own pixel rhythm. '#' is drawn, '.' is empty.
        static class PetIcons
        {
            public static readonly string[] Heart = {
                ".##.##.", "#######", "#######", ".#####.", "..###..", "...#...", "......."
            };
            public static readonly string[] Spark = {
                "...#...", "...#...", "..###..", "#######", "..###..", "...#...", "...#..."
            };
            public static readonly string[] Chase = {
                "#..#...", ".#..#..", "..#..#.", "...#..#", "..#..#.", ".#..#..", "#..#..."
            };
            public static readonly string[] Moon = {
                "####...", "..#....", ".#.....", "####...", "....###", ".....#.", "....###"
            };
            public static readonly string[] Chat = {
                "#######", "#.....#", "#.#.#.#", "#.....#", "#######", ".##....", "#......"
            };
            public static readonly string[] Gear = {
                "...#...", ".#####.", ".#...#.", "##.#.##", ".#...#.", ".#####.", "...#..."
            };
        }

        // Big, labelled buttons above the pet, opened by a plain click or a right-click on it.
        // It never takes focus: it closes on a click elsewhere, after a short while without the
        // pointer on it or the pet, or once an action runs.
        sealed class PetActionBar : Form
        {
            const double IdleClose = 2.5, RiseSeconds = 0.12;
            readonly float scale;
            readonly List<PetAction> actions;
            readonly Rectangle[] cells;
            readonly Func<bool> allowed;
            readonly Func<Rectangle> petBounds;
            readonly Timer timer = new Timer { Interval = 50 };
            readonly Stopwatch clock = Stopwatch.StartNew();
            readonly Color tint, fore, faint, hover, press, rim;
            readonly Font labelFont;
            readonly int bodyH;
            int hot = -1, down = -1, tailAt = -1;
            double lastOver;
            Rectangle target;
            bool rising;

            int Px(float v) { return (int)Math.Round(v * scale); }

            public PetActionBar(List<PetAction> actions, float scale, Func<bool> allowed, Func<Rectangle> petBounds)
            {
                this.actions = actions;
                this.scale = scale > 0 ? scale : 1f;
                this.allowed = allowed;
                this.petBounds = petBounds;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                DoubleBuffered = true;
                Text = Lang.T("Aksi pet");
                AccessibleName = Text;

                var tone = Ink.Base;
                bool dark = tone.IsDark;
                tint = Ink.Label("amber", !dark);
                BackColor = Ink.Mix(Ink.Mix(tone.Bg, tone.Text, dark ? 0.08f : 0.015f), tint, dark ? 0.06f : 0.035f);
                fore = Color.FromArgb(255, tone.Text.R, tone.Text.G, tone.Text.B);
                faint = Ink.Mix(BackColor, fore, dark ? 0.4f : 0.42f);
                hover = Ink.Mix(BackColor, tint, dark ? 0.2f : 0.16f);
                press = Ink.Mix(BackColor, tint, dark ? 0.3f : 0.26f);
                rim = Ink.Mix(BackColor, fore, dark ? 0.14f : 0.11f);
                labelFont = Ink.Semi(Px(11f));

                var widths = new int[actions.Count];
                for (int i = 0; i < actions.Count; i++)
                    widths[i] = TextRenderer.MeasureText(actions[i].Label, labelFont, Size.Empty,
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                Size body;
                cells = Cells(widths, this.scale, out body);
                bodyH = body.Height;
                ClientSize = new Size(body.Width, body.Height + Px(9));

                timer.Tick += delegate { Tick(); };
            }

            // Equal cells, each wide enough for the longest label and never under 54 logical px:
            // a pointer target that is easy to hit, laid out left to right with a small gap
            public static Rectangle[] Cells(int[] labelWidths, float scale, out Size body)
            {
                Func<float, int> px = v => (int)Math.Round(v * scale);
                int widest = 0;
                foreach (int w in labelWidths) widest = Math.Max(widest, w);
                int cellW = Math.Max(px(54), widest + px(12)), cellH = px(54), gap = px(4), pad = px(6);
                var r = new Rectangle[labelWidths.Length];
                for (int i = 0; i < r.Length; i++) r[i] = new Rectangle(pad + i * (cellW + gap), pad, cellW, cellH);
                body = new Size(pad * 2 + r.Length * cellW + Math.Max(0, r.Length - 1) * gap, pad * 2 + cellH);
                return r;
            }

            public bool MoveAbove(Rectangle area, Point anchor)
            {
                var r = PetChatBubble.BoundsAbove(area, anchor, Size, Px(6));
                if (r.IsEmpty) return false;
                int tip = Math.Max(Px(14), Math.Min(r.Width - Px(14), anchor.X - r.Left));
                if (tip != tailAt)
                {
                    tailAt = tip;
                    using (var path = Ink.Round(new RectangleF(0, 0, r.Width, bodyH), Px(Ink.RadiusCard)))
                    using (var tail = new GraphicsPath())
                    {
                        int join = bodyH - Px(2), end = r.Height - 1;
                        tail.AddPolygon(new[] { new Point(tip - Px(8), join), new Point(tip, end), new Point(tip + Px(8), join) });
                        var old = Region;
                        var shape = new Region(path);
                        shape.Union(tail);
                        Region = shape;
                        if (old != null) old.Dispose();
                    }
                    Invalidate();
                }
                target = r;
                if (!rising && Bounds != r) Bounds = r;
                return true;
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                lastOver = clock.Elapsed.TotalSeconds;
                // A small rise into place; reduced motion places it at once
                rising = !Anim.Reduced;
                if (rising) Bounds = new Rectangle(target.X, target.Y + Px(6), target.Width, target.Height);
                timer.Interval = rising ? 16 : 50;
                timer.Start();
            }

            void Tick()
            {
                double now = clock.Elapsed.TotalSeconds;
                if (rising)
                {
                    double u = Math.Min(1, now / RiseSeconds);
                    double ease = 1 - (1 - u) * (1 - u);
                    Bounds = new Rectangle(target.X, target.Y + (int)Math.Round((1 - ease) * Px(6)), target.Width, target.Height);
                    if (u >= 1) { rising = false; timer.Interval = 50; }
                }
                if (allowed != null && !allowed()) { Close(); return; }
                Point at = Cursor.Position;
                bool over = Bounds.Contains(at) || (petBounds != null && petBounds().Contains(at));
                if (over) lastOver = now;
                else if (now - lastOver > IdleClose) { Close(); return; }
                // A press anywhere else dismisses it, like a menu
                if (!over && (KeyState(0x01) < 0 || KeyState(0x02) < 0)) Close();
            }

            int CellAt(Point p)
            {
                for (int i = 0; i < cells.Length; i++) if (cells[i].Contains(p)) return i;
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int c = CellAt(e.Location);
                if (c != hot) { hot = c; Invalidate(); }
                Cursor = c >= 0 && actions[c].Enabled ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hot = -1;
                down = -1;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                down = CellAt(e.Location);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                int c = CellAt(e.Location);
                bool hit = c >= 0 && c == down && actions[c].Enabled;
                down = -1;
                Invalidate();
                if (!hit) return;
                var run = actions[c].Run;
                Close();
                if (run != null) run();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                KitInk.Smooth(g);
                float inset = Math.Max(0.75f, 0.85f * scale);
                KitInk.StrokeRound(g, new RectangleF(inset, inset, ClientSize.Width - 2 * inset, bodyH - 2 * inset),
                    Math.Max(0, Px(Ink.RadiusCard) - inset), rim, Math.Max(1f, scale));

                int unit = Math.Max(1, (int)Math.Round(2 * scale));
                for (int i = 0; i < cells.Length; i++)
                {
                    var a = actions[i];
                    var r = cells[i];
                    bool pressed = a.Enabled && i == down && i == hot;
                    if (a.Enabled && (i == hot || pressed))
                        KitInk.FillRound(g, r, Px(Ink.RadiusControl), pressed ? press : hover);
                    int dy = pressed ? Math.Max(1, Px(0.75f)) : 0;
                    Color ink = a.Enabled ? (i == hot ? tint : fore) : faint;

                    // Whole screen pixels keep the icon crisp at fractional DPI
                    int size = 7 * unit, x = r.X + (r.Width - size) / 2, y = r.Y + Px(9) + dy;
                    var brush = KitInk.BrushOf(ink);
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 7; col++)
                            if (a.Icon[row][col] == '#') g.FillRectangle(brush, x + col * unit, y + row * unit, unit, unit);

                    var label = new RectangleF(r.X, r.Y + Px(31) + dy, r.Width, Px(16));
                    KitInk.Draw(g, a.Label, labelFont, ink, label, ContentAlignment.MiddleCenter);
                }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) timer.Dispose();
                base.Dispose(disposing);
            }
        }

        sealed partial class PetWindow
        {
            PetActionBar actionBar;
            bool manualChase;

            public bool ActionsOpen { get { return actionBar != null; } }

            void ToggleActions()
            {
                if (actionBar != null) { CloseActions(); return; }
                OpenActions();
            }

            List<PetAction> BuildActions()
            {
                var list = new List<PetAction>
                {
                    new PetAction(PetIcons.Heart, Lang.T("Elus"), true, () => { Petted(); }),
                    new PetAction(PetIcons.Spark, Lang.T("Lompat"), true, () =>
                    {
                        brain.NoWalk = Anim.Reduced;
                        brain.Calm = FocusActive;
                        if (brain.React(PetMode.Happy, Math.Max(1.4, sheet.Length(PetMode.Happy)), 1)) Emote(PetEmote.Spark);
                        lastT = clock.Elapsed.TotalSeconds;
                        Frame();
                    }),
                    new PetAction(PetIcons.Chase, Lang.T("Kejar"), !PlayBlocked && lane.Width >= 24 * scale, () =>
                    {
                        if (PlayBlocked || !brain.StartChase(3.0)) return;
                        manualChase = true;
                        nextAutoFullscreen = 0;
                        lastT = clock.Elapsed.TotalSeconds;
                        Frame();
                    }),
                    new PetAction(PetIcons.Moon, Lang.T("Tidur"), !brain.Away && brain.Mode != PetMode.Sleep, () =>
                    {
                        brain.Nap();
                        lastT = clock.Elapsed.TotalSeconds;
                        Frame();
                    })
                };
                if (Isle != null && Isle.PetRoomConnected)
                    list.Add(new PetAction(PetIcons.Chat, Lang.T("Chat"), true, () => { Isle.OpenPetChatInput(this); }));
                list.Add(new PetAction(PetIcons.Gear, Lang.T("Atur"), true, () => { if (Isle != null) Isle.OpenPrefs("pet"); }));
                return list;
            }

            void OpenActions()
            {
                if (actionBar != null || hidden || closing || !placed || sheet == null || drawnX == int.MinValue || Isle == null) return;
                CloseChatBubble();
                PetActionBar b = null;
                try
                {
                    b = new PetActionBar(BuildActions(), scale, () => !hidden && !closing && placed && Visible,
                        () => new Rectangle(drawnX, drawnY, cw, ch));
                    if (!PositionActions(b)) return;
                    b.FormClosed += delegate
                    {
                        if (actionBar != b) return;
                        actionBar = null;
                        // Free to wander again
                        if (!closing && placed) { lastT = clock.Elapsed.TotalSeconds; Frame(); }
                    };
                    b.Show(this);
                    if (!b.Visible) return;
                    actionBar = b;
                    brain.Attending = true;
                    lastT = clock.Elapsed.TotalSeconds;
                    Frame();
                }
                catch (System.ComponentModel.Win32Exception ex) { Log.Write("pet actions: " + ex.Message); }
                catch (InvalidOperationException ex) { Log.Write("pet actions: " + ex.Message); }
                finally
                {
                    if (b != null && actionBar != b) b.Dispose();
                }
            }

            void CloseActions()
            {
                if (actionBar != null && !actionBar.IsDisposed) actionBar.Close();
                actionBar = null;
            }

            // Above the standing head, or above the reminder bubble when one is showing
            bool PositionActions(PetActionBar b)
            {
                int x = drawnX + cw / 2;
                int y = lane.Y - (sheet.Base + 1 - sheet.Top) * f;
                if (wellness != null && !wellness.IsDisposed) y = Math.Min(y, wellness.Top);
                return b.MoveAbove(work, new Point(x, y));
            }

            void PlaceActions()
            {
                if (actionBar != null && !PositionActions(actionBar)) CloseActions();
            }

            // A chase asked for from the bar follows the pointer anywhere along the lane, not only the dock band
            bool ManualChaseAllowed(double now)
            {
                if (away != null && away()) return false;
                if (now >= nextAutoFullscreen)
                {
                    autoFullscreen = Native.IsFullscreenOn(monitor);
                    nextAutoFullscreen = now + 0.12;
                }
                return !autoFullscreen;
            }
        }
    }
}
