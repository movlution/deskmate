using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Speech bubble above a pet for incoming/outgoing multiplayer chat messages.
        sealed class PetChatBubble : Form
        {
            readonly float scale;
            readonly Timer timer = new Timer { Interval = 250 };
            readonly Func<bool> allowed;
            readonly Action onReply;
            readonly int bodyH, pad;
            readonly Color fore, accent, rim;
            int tailAt = -1, tailRoot = -1;
            double remainingSec;
            bool hovered;

            int Px(float v) { return (int)Math.Round(v * scale); }

            public PetChatBubble(string sender, string text, bool masked, float scale, int availableWidth,
                Func<bool> allowed, Action onReply)
            {
                this.scale = scale > 0 ? scale : 1f;
                this.allowed = allowed;
                this.onReply = onReply;

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Cursor = onReply != null ? Cursors.Hand : Cursors.Default;

                var tone = Ink.Base;
                bool dark = tone.IsDark;
                accent = tone.Accent;
                BackColor = Ink.Mix(Ink.Mix(tone.Bg, tone.Text, dark ? 0.08f : 0.015f), accent, dark ? 0.07f : 0.03f);
                fore = Color.FromArgb(255, tone.Text.R, tone.Text.G, tone.Text.B);
                rim = Ink.Mix(BackColor, fore, dark ? 0.16f : 0.12f);

                string body = masked ? Lang.T("Pesan baru dari pet") : (text ?? "");
                string name = string.IsNullOrEmpty(sender) ? Lang.T("Teman") : sender;
                Text = name + ": " + body;
                AccessibleName = Text;

                remainingSec = Math.Max(6.0, Math.Min(12.0, 5.0 + body.Length * 0.06));

                pad = Px(12);
                int maxW = Math.Max(Px(160), Math.Min(Px(264), availableWidth - Px(16)));
                var nameFont = Ink.Semi(Px(11.5f));
                var msgFont = Ink.Text(Px(13f));

                int nameW = TextRenderer.MeasureText(name, nameFont, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                int msgLineW = TextRenderer.MeasureText(body, msgFont, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                int contentW = Math.Min(maxW - pad * 2, Math.Max(Px(110), Math.Max(nameW, msgLineW) + Px(4)));

                int nameH = Math.Max(Px(16), TextRenderer.MeasureText(name, nameFont, new Size(contentW, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Height);
                int msgH = Math.Max(Px(19), TextRenderer.MeasureText(body, msgFont, new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);

                int winW = contentW + pad * 2;
                bodyH = pad + nameH + Px(3) + msgH + pad;
                ClientSize = new Size(winW, bodyH + Px(11));

                var nameLbl = new Label
                {
                    Text = name,
                    ForeColor = accent,
                    BackColor = Color.Transparent,
                    Font = nameFont,
                    UseMnemonic = false,
                    Bounds = new Rectangle(pad, pad, contentW, nameH),
                    Cursor = Cursor
                };
                var msgLbl = new Label
                {
                    Text = body,
                    ForeColor = fore,
                    BackColor = Color.Transparent,
                    Font = msgFont,
                    UseMnemonic = false,
                    Bounds = new Rectangle(pad, pad + nameH + Px(3), contentW, msgH),
                    Cursor = Cursor
                };

                EventHandler click = delegate
                {
                    var cb = this.onReply;
                    Close();
                    if (cb != null) cb();
                };
                EventHandler enter = delegate { hovered = true; };
                EventHandler leave = delegate { hovered = false; };

                Click += click;
                MouseEnter += enter;
                MouseLeave += leave;
                nameLbl.Click += click;
                nameLbl.MouseEnter += enter;
                nameLbl.MouseLeave += leave;
                msgLbl.Click += click;
                msgLbl.MouseEnter += enter;
                msgLbl.MouseLeave += leave;

                Controls.Add(nameLbl);
                Controls.Add(msgLbl);

                timer.Tick += delegate
                {
                    if (this.allowed != null && !this.allowed()) { Close(); return; }
                    if (hovered) return;
                    remainingSec -= 0.25;
                    if (remainingSec <= 0) Close();
                };
            }

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
                    int half = Px(9);
                    tailRoot = Math.Max(Px(20) + half, Math.Min(r.Width - Px(20) - half, tip));
                    using (var path = Ink.Round(new RectangleF(0, 0, r.Width, bodyH), Px(Ink.RadiusCard)))
                    using (var tail = new GraphicsPath())
                    {
                        int join = bodyH - Px(3), end = r.Height - 1;
                        tail.AddBezier(tailRoot - Px(10), join, tailRoot - Px(3), join + Px(2),
                            tip - Px(3), end - Px(1), tip, end);
                        tail.AddBezier(tip, end, tip + Px(3), end - Px(1),
                            tailRoot + Px(3), join + Px(2), tailRoot + Px(10), join);
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

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                KitInk.Smooth(g);
                float inset = Math.Max(0.75f, 0.85f * scale);
                var bodyRect = new RectangleF(inset, inset, ClientSize.Width - 2 * inset, bodyH - 2 * inset);
                KitInk.StrokeRound(g, bodyRect, Math.Max(0, Px(Ink.RadiusCard) - inset), rim, Math.Max(1f, scale));
                if (tailRoot >= 0 && tailAt >= 0)
                {
                    int half = Px(9);
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

            protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) timer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
