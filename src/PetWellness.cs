using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class PetWindow
        {
            PetWellnessBubble wellness;
            public bool WellnessOpen { get { return wellness != null; } }

            bool WellnessAllowed()
            {
                return Isle != null && !Isle.IsDisposed && Isle.PetOn && placed && !hidden && !closing &&
                    !Isle.PrompterOn && !Isle.QuietToast(View.Remind, 2) &&
                    (away == null || !away()) && !Native.IsFullscreenOn(monitor);
            }

            public bool TryWellness(RemindKind kind, string detail)
            {
                if (WellnessOpen || !WellnessAllowed() || !Visible || sheet == null || memDC == IntPtr.Zero ||
                    drawnX == int.MinValue) return false;

                PetWellnessBubble b = null;
                try
                {
                    var isle = Isle;
                    Action main = kind == RemindKind.Drink ? (Action)isle.Drank : null;
                    b = new PetWellnessBubble(kind, detail, scale, work.Width, WellnessAllowed, main, () => isle.Snooze(15));
                    if (!PositionWellness(b)) return false;
                    b.FormClosed += delegate
                    {
                        if (wellness != b) return;
                        wellness = null;
                        if (!isle.IsDisposed && (isle.toast == null || !IsReminder(isle.toast.View))) isle.StopRemind();
                    };
                    b.Show(this);
                    if (!b.Visible) return false;
                    wellness = b;
                    return true;
                }
                catch (Win32Exception ex)
                {
                    Log.Write("pet reminder: " + ex.Message);
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    Log.Write("pet reminder: " + ex.Message);
                    return false;
                }
                finally
                {
                    if (b != null && wellness != b) b.Dispose();
                }
            }

            public void CloseWellness(RemindKind kind = RemindKind.None)
            {
                if (wellness != null && (kind == RemindKind.None || wellness.Kind == kind)) wellness.Close();
            }

            bool PositionWellness(PetWellnessBubble b)
            {
                // Use the standing height for Y so hopping or breathing doesn't jiggle the buttons.
                int x = drawnX + cw / 2;
                int y = lane.Y - (sheet.Base + 1 - sheet.Top) * f;
                return b.MoveAbove(work, new Point(x, y));
            }

            void PlaceWellness()
            {
                if (wellness != null && !PositionWellness(wellness)) CloseWellness();
            }
        }

        sealed class BubbleButton : KitButton
        {
            readonly float scale;
            readonly Color secRest, secHover, secPress, secDisabled, secText, secFaint;

            public BubbleButton(float scale, Color secRest, Color secHover, Color secPress,
                Color secDisabled, Color secText, Color secFaint)
            {
                this.scale = scale > 0 ? scale : 1f;
                this.secRest = secRest;
                this.secHover = secHover;
                this.secPress = secPress;
                this.secDisabled = secDisabled;
                this.secText = secText;
                this.secFaint = secFaint;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                var under = KitInk.Under(this);
                g.Clear(under);
                float s = scale;
                var r = RingOn
                    ? new RectangleF(3.5f * s, 3.5f * s, Width - 7 * s, Height - 7 * s)
                    : new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
                Color bg, fg;
                if (!Enabled)
                {
                    bg = secDisabled;
                    fg = secFaint;
                }
                else if (Primary)
                {
                    bg = KitInk.Deepen(KitInk.Accent, KitInk.OnAccent, pressed ? 0.2f : hovered ? 0.1f : 0f);
                    fg = KitInk.OnAccent;
                }
                else
                {
                    bg = pressed ? secPress : hovered ? secHover : secRest;
                    fg = secText;
                }

                if (pressed && Enabled) r.Offset(0, 0.75f * s);
                float radius = Radius(r, s);
                KitInk.FillRound(g, r, radius, bg);
                KitInk.Draw(g, Text, Font, fg, r, ContentAlignment.MiddleCenter);
                if (RingOn)
                {
                    float grow = 2.5f * s;
                    KitInk.FocusRing(g, RectangleF.Inflate(r, grow, grow), radius + grow, under, s);
                }
            }
        }

        sealed class PetWellnessBubble : Form
        {
            readonly float scale;
            readonly Timer timer = new Timer { Interval = 1000 };
            readonly Stopwatch clock = new Stopwatch();
            readonly Func<bool> allowed;
            readonly Label time;
            readonly int bodyH, pad, eyeBtnW;
            readonly Color tint, sticker, timerTrack, fore, secRest, secHover, secPress, secDisabled, secFaint, rim;
            int tailAt = -1, tailRoot = -1, seconds = WellnessSeconds;

            // Hand-drawn at the same pixel rhythm as the dock pet: drop, eye, stretch.
            static readonly string[] Drop = {
                "...#...", "...##..", "..####.", ".##+##.", ".#+###.", ".#####.", "..###.."
            };
            static readonly string[] Eye = {
                ".......", "..###..", ".##+##.", "##+#+##", ".##+##.", "..###..", "......."
            };
            static readonly string[] Stretch = {
                "...#...", "#.....#", ".#...#.", "..###..", "...#...", "..#.#..", ".#...#."
            };

            public RemindKind Kind { get; private set; }
            int Px(float v) { return (int)Math.Round(v * scale); }

            public PetWellnessBubble(RemindKind kind, string detail, float scale, int availableWidth,
                Func<bool> allowed, Action main, Action later)
            {
                Kind = kind;
                this.scale = scale;
                this.allowed = allowed;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                var tone = Ink.Base;
                bool dark = tone.IsDark;
                tint = Ink.Label(kind == RemindKind.Drink ? "sky" :
                    kind == RemindKind.Eyes ? "violet" : "emerald", !dark);
                BackColor = Ink.Mix(Ink.Mix(tone.Bg, tone.Text, dark ? 0.08f : 0.015f),
                    tint, dark ? 0.08f : 0.04f);
                fore = Color.FromArgb(255, tone.Text.R, tone.Text.G, tone.Text.B);
                sticker = Ink.Mix(BackColor, tint, dark ? 0.17f : 0.12f);
                timerTrack = Ink.Mix(BackColor, tint, dark ? 0.22f : 0.18f);
                secRest = Ink.Mix(BackColor, fore, dark ? 0.12f : 0.10f);
                secHover = Ink.Mix(BackColor, fore, dark ? 0.19f : 0.16f);
                secPress = Ink.Mix(BackColor, fore, dark ? 0.25f : 0.22f);
                secDisabled = Ink.Mix(BackColor, fore, dark ? 0.06f : 0.05f);
                secFaint = Ink.Mix(BackColor, fore, dark ? 0.66f : 0.69f);
                rim = Ink.Mix(BackColor, fore, dark ? 0.14f : 0.11f);
                Text = Title(kind);
                AccessibleName = Text;

                int windowW = Math.Max(Px(246), Math.Min(Px(278), availableWidth - Px(12)));
                pad = Px(14);
                int width = windowW - 2 * pad;
                int textX = Px(52), textW = windowW - textX - pad;
                var titleFont = Ink.Semi(Px(14.5f));
                var detailFont = Ink.Text(Px(12));
                var btnFont = Ink.Semi(Px(12.5f));
                int titleH = Math.Max(Px(22), WrappedHeight(Text, titleFont, textW));
                int detailH = Math.Max(Px(19), WrappedHeight(detail, detailFont, textW));
                int extra = titleH - Px(22) + detailH - Px(19);
                bodyH = Px(112) + extra;
                ClientSize = new Size(windowW, bodyH + Px(12));

                Controls.Add(Line(Text, fore, titleFont, textX, Px(12), textW, titleH));
                Controls.Add(Line(detail, Ink.Mix(BackColor, fore, 0.76f), detailFont,
                    textX, Px(12) + titleH, textW, detailH));

                int row = Px(62) + extra;
                string primaryText = Lang.T(kind == RemindKind.Drink ? "+1 gelas" : "Selesai");
                int primaryW = Math.Max(Px(84), Math.Min(Px(108), TextWidth(primaryText, btnFont) + Px(28)));
                if (kind == RemindKind.Eyes)
                {
                    eyeBtnW = primaryW;
                    time = Line(SecondsText(WellnessSeconds), fore, Ink.Semi(Px(15)),
                        pad, row + Px(2), Math.Max(Px(80), width - eyeBtnW - Px(14)), Px(23));
                    Controls.Add(time);
                    AddAction(primaryText, ClientSize.Width - pad - eyeBtnW, row, eyeBtnW, btnFont, true, main);
                }
                else
                {
                    string laterText = Lang.T("Tunda 15 menit");
                    int minSecW = Math.Max(Px(116), TextWidth(laterText, btnFont) + Px(24));
                    primaryW = Math.Min(primaryW, Math.Max(Px(76), width - Px(8) - minSecW));
                    AddAction(primaryText, pad, row, primaryW, btnFont, true, main);
                    AddAction(laterText, pad + primaryW + Px(8), row, width - primaryW - Px(8), btnFont, false, later);
                }

                timer.Tick += delegate
                {
                    int left = SecondsLeft(clock.Elapsed.TotalSeconds);
                    if (left == 0 || !this.allowed()) { Close(); return; }
                    if (time != null && seconds != left)
                    {
                        seconds = left;
                        time.Text = SecondsText(left);
                        Invalidate(Rectangle.Inflate(CountdownTrack(), 2, 2));
                    }
                };
            }

            public static string Title(RemindKind kind)
            {
                return Lang.T(kind == RemindKind.Drink ? "Yuk, minum air!" :
                    kind == RemindKind.Eyes ? "Lihat jauh dulu, ya." : "Yuk, regangkan badan!");
            }

            public static int SecondsLeft(double elapsed)
            {
                return Math.Max(0, (int)Math.Ceiling(WellnessSeconds - elapsed));
            }

            static string SecondsText(int n) { return Lang.F("{0} detik", n); }

            static int TextWidth(string text, Font font)
            {
                return TextRenderer.MeasureText(text, font, Size.Empty,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            }

            static int WrappedHeight(string text, Font font, int width)
            {
                return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            }

            static Label Line(string text, Color color, Font font, int x, int y, int w, int h)
            {
                return new Label { Text = text, ForeColor = color, Font = font, UseMnemonic = false,
                    TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(x, y, w, h) };
            }

            void AddAction(string text, int x, int y, int width, Font font, bool primary, Action action)
            {
                var b = new BubbleButton(scale, secRest, secHover, secPress, secDisabled, fore, secFaint)
                {
                    Text = text,
                    Primary = primary,
                    Font = font,
                    Bounds = new Rectangle(x, y, width, Px(36))
                };
                b.Click += delegate { Close(); if (action != null) action(); };
                Controls.Add(b);
            }

            Rectangle CountdownTrack()
            {
                int trackW = Math.Max(Px(88), ClientSize.Width - pad * 2 - eyeBtnW - Px(14));
                return new Rectangle(pad, time.Bottom + Px(5), trackW, Px(4));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                KitInk.Smooth(g);

                // Subtle unified inner rim around the bubble body and pointer tail.
                float inset = Math.Max(0.75f, 0.85f * scale);
                var bodyRect = new RectangleF(inset, inset, ClientSize.Width - 2 * inset, bodyH - 2 * inset);
                KitInk.StrokeRound(g, bodyRect, Math.Max(0, Px(Ink.RadiusCard) - inset), rim, Math.Max(1f, scale));
                if (tailRoot >= 0 && tailAt >= 0)
                {
                    int half = Px(10);
                    float joinY = bodyH - inset;
                    float endY = ClientSize.Height - 1 - inset;
                    using (var notch = new GraphicsPath())
                    {
                        notch.AddBezier(tailRoot - half + inset, joinY - Px(3), tailRoot - Px(3), joinY + Px(1),
                            tailAt - Px(3), endY - Px(1), tailAt, endY);
                        notch.AddBezier(tailAt, endY, tailAt + Px(3), endY - Px(1),
                            tailRoot + Px(3), joinY + Px(1), tailRoot + half - inset, joinY - Px(3));
                        notch.CloseFigure();
                        g.FillPath(KitInk.BrushOf(BackColor), notch);
                    }
                    using (var leg = new GraphicsPath())
                    {
                        leg.AddBezier(tailRoot - half + inset * 0.5f, joinY, tailRoot - Px(3), joinY + Px(1.5f),
                            tailAt - Px(3), endY - Px(1), tailAt, endY);
                        leg.AddBezier(tailAt, endY, tailAt + Px(3), endY - Px(1),
                            tailRoot + Px(3), joinY + Px(1.5f), tailRoot + half - inset * 0.5f, joinY);
                        g.DrawPath(KitInk.PenOf(rim, Math.Max(1f, scale)), leg);
                    }
                }

                KitInk.FillRound(g, new RectangleF(pad, Px(16), Px(30), Px(30)), Px(9), sticker);

                var mark = Kind == RemindKind.Drink ? Drop : Kind == RemindKind.Eyes ? Eye : Stretch;
                // Draw on whole screen pixels so the miniature stays crisp at fractional DPI.
                int unit = Math.Max(1, (int)Math.Round(2 * scale));
                int x = pad + Px(15) - 7 * unit / 2, y = Px(31) - 7 * unit / 2;
                for (int row = 0; row < mark.Length; row++)
                    for (int col = 0; col < mark[row].Length; col++)
                    {
                        char pixel = mark[row][col];
                        if (pixel == '.') continue;
                        g.FillRectangle(KitInk.BrushOf(pixel == '#' ? tint : sticker),
                            x + col * unit, y + row * unit, unit, unit);
                    }

                if (time == null) return;
                var track = CountdownTrack();
                var fill = new RectangleF(track.X, track.Y, track.Width * seconds / (float)WellnessSeconds, track.Height);
                KitInk.FillRound(g, track, track.Height / 2f, timerTrack);
                KitInk.FillRound(g, fill, Math.Min(fill.Height, fill.Width) / 2f, tint);
            }

            // Clamp the body to the monitor; keep the tail aligned with the pet as it walks.
            public static Rectangle BoundsAbove(Rectangle area, Point anchor, Size size, int margin)
            {
                int left = area.Left + margin, right = area.Right - margin - size.Width;
                int top = anchor.Y - margin - size.Height;
                if (right < left || top < area.Top + margin || top + size.Height > area.Bottom - margin)
                    return Rectangle.Empty;
                return new Rectangle(Math.Max(left, Math.Min(right, anchor.X - size.Width / 2)), top, size.Width, size.Height);
            }

            public bool MoveAbove(Rectangle area, Point anchor)
            {
                var r = BoundsAbove(area, anchor, Size, Px(6));
                if (r.IsEmpty) return false;
                int tip = Math.Max(Px(8), Math.Min(r.Width - Px(8), anchor.X - r.Left));
                if (tip != tailAt)
                {
                    tailAt = tip;
                    int half = Px(10);
                    tailRoot = Math.Max(Px(26) + half, Math.Min(r.Width - Px(26) - half, tip));
                    using (var path = Ink.Round(new RectangleF(0, 0, r.Width, bodyH), Px(Ink.RadiusCard)))
                    using (var tail = new GraphicsPath())
                    {
                        int join = bodyH - Px(3), end = r.Height - 1;
                        tail.AddBezier(tailRoot - Px(11), join, tailRoot - Px(3), join + Px(2),
                            tip - Px(3), end - Px(1), tip, end);
                        tail.AddBezier(tip, end, tip + Px(3), end - Px(1),
                            tailRoot + Px(3), join + Px(2), tailRoot + Px(11), join);
                        tail.CloseFigure();
                        var old = Region;
                        var shape = new Region(path);
                        shape.Union(tail);
                        Region = shape;
                        if (old != null) old.Dispose();
                    }
                    Invalidate();
                }
                if (Bounds != r) Bounds = r;
                return true;
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

            protected override void OnShown(EventArgs e) { base.OnShown(e); clock.Start(); timer.Start(); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) timer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
