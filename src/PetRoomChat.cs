using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    // The room's chat history: every message since this room started, in memory only, shown in the
    // chat window. Each message still pops as a bubble above the pet of whoever sent it.
    partial class Island
    {
        internal sealed class PetChatLine
        {
            public string Sender, Text;
            public bool Mine, Notice;
            public DateTime At;
        }

        const int PetChatKeep = 100;
        readonly List<PetChatLine> petChatLog = new List<PetChatLine>();

        void LogPetChat(string sender, string text, bool mine, bool notice = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            petChatLog.Add(new PetChatLine { Sender = sender ?? "", Text = text, Mine = mine, Notice = notice, At = DateTime.Now });
            if (petChatLog.Count > PetChatKeep) petChatLog.RemoveRange(0, petChatLog.Count - PetChatKeep);
            if (petChatInput != null && !petChatInput.IsDisposed) petChatInput.LogChanged();
        }

        void ClearPetChatLog()
        {
            petChatLog.Clear();
            if (petChatInput != null && !petChatInput.IsDisposed) petChatInput.LogChanged();
        }

        // Owner-drawn message list, newest at the bottom. Friends' messages sit left, yours right in the
        // accent tint; a run from one sender within a few minutes shares one name and time line.
        // Follows new messages unless the user scrolled up to read older ones; then a pill offers the way back.
        internal sealed class PetChatLog : Control
        {
            sealed class Row
            {
                public int Y, H, BodyW, BodyH, TextW;
                public bool Head;
                public string Shown;
            }

            readonly List<PetChatLine> lines;
            readonly float scale;
            readonly Color fore, name, dim, rule, theirs, mine, accent, onAccent;
            readonly Font nameFont, textFont, smallFont;
            readonly List<Row> rows = new List<Row>();
            int scrollY, contentH;
            bool stick = true, unseen;
            PetChatLine last;
            Rectangle jump;

            int Px(float v) { return (int)Math.Round(v * scale); }
            const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;
            const TextFormatFlags One = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            // Measuring: no ellipsis, or an empty proposed width measures only the "…"
            const TextFormatFlags Fit = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            // Messages from one sender this close together read as one run
            static readonly TimeSpan RunGap = TimeSpan.FromMinutes(3);

            public PetChatLog(List<PetChatLine> lines, float scale, Color back, Color fore, bool dark)
            {
                this.lines = lines;
                this.scale = scale > 0 ? scale : 1f;
                this.fore = fore;
                name = Ink.Mix(back, fore, 0.82f);
                dim = Ink.Mix(back, fore, 0.64f);
                rule = Ink.Mix(back, fore, dark ? 0.14f : 0.12f);
                theirs = Ink.Mix(back, fore, dark ? 0.09f : 0.065f);
                accent = KitInk.Accent;
                onAccent = KitInk.OnAccent;
                // Grafit has no hue: tint less so ivory text keeps its contrast
                float k = accent.GetSaturation() < 0.15f ? (dark ? 0.2f : 0.12f) : (dark ? 0.3f : 0.17f);
                mine = Ink.Mix(back, accent, k);
                BackColor = back;
                nameFont = Ink.Semi(Px(11.5f));
                textFont = Ink.Text(Px(12.5f));
                smallFont = Ink.Text(Px(11f));
                // Not selectable: a click here keeps the caret in the composer
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                AccessibleRole = AccessibleRole.List;
                AccessibleName = Lang.T("Chat room");
            }

            int Gutter { get { return Px(6); } }
            int ContentW { get { return Math.Max(Px(80), ClientSize.Width - Gutter); } }
            int PadX { get { return Px(10); } }
            int PadY { get { return Px(6); } }
            int MaxText { get { return Math.Max(Px(40), (int)(ContentW * 0.8f) - PadX * 2); } }
            int MaxScroll { get { return Math.Max(0, contentH - ClientSize.Height); } }
            internal int ContentHeight { get { return contentH; } }
            internal int ScrollOffset { get { return scrollY; } }
            internal bool Unseen { get { return unseen; } }
            // Widest wrapped text as measured, before any clamp; never past TextLimit
            internal int WidestText { get { int w = 0; foreach (var r in rows) w = Math.Max(w, r.TextW); return w; } }
            internal int TextLimit { get { return MaxText; } }

            static bool Joined(PetChatLine prev, PetChatLine l)
            {
                return prev != null && !prev.Notice && !l.Notice && prev.Mine == l.Mine && prev.Sender == l.Sender && l.At - prev.At < RunGap;
            }

            // Word wrap cannot split a link or a long laugh: cut such runs where they reach the bubble edge
            string Breakable(string text)
            {
                int max = MaxText;
                if (TextRenderer.MeasureText(text, textFont, new Size(max, int.MaxValue), Wrap).Width <= max) return text;
                var sb = new System.Text.StringBuilder(text.Length + 8);
                foreach (string word in text.Split(' '))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    string rest = word;
                    while (rest.Length > 1 && TextRenderer.MeasureText(rest, textFont, Size.Empty, Fit).Width > max)
                    {
                        int n = rest.Length - 1;
                        while (n > 1 && TextRenderer.MeasureText(rest.Substring(0, n), textFont, Size.Empty, Fit).Width > max) n--;
                        if (char.IsHighSurrogate(rest[n - 1]) && n > 1) n--;
                        sb.Append(rest, 0, n).Append('\n');
                        rest = rest.Substring(n);
                    }
                    sb.Append(rest);
                }
                return sb.ToString();
            }

            void Relayout()
            {
                rows.Clear();
                int y = Px(4), max = MaxText;
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    bool joined = Joined(i > 0 ? lines[i - 1] : null, l);
                    if (i > 0) y += joined ? Px(3) : Px(12);
                    var r = new Row { Y = y };
                    if (l.Notice) r.H = smallFont.Height + Px(4);
                    else
                    {
                        r.Head = !joined;
                        r.Shown = Breakable(l.Text);
                        var sz = TextRenderer.MeasureText(r.Shown, textFont, new Size(max, int.MaxValue), Wrap);
                        r.TextW = sz.Width;
                        r.BodyW = Math.Min(max, sz.Width) + PadX * 2;
                        r.BodyH = Math.Max(sz.Height, textFont.Height) + PadY * 2;
                        r.H = (r.Head ? nameFont.Height + Px(3) : 0) + r.BodyH;
                    }
                    y += r.H;
                    rows.Add(r);
                }
                contentH = rows.Count > 0 ? y + Px(4) : 0;
                scrollY = stick ? MaxScroll : Math.Min(scrollY, MaxScroll);
            }

            // The log changed: a new message, or cleared
            public void Added()
            {
                var newest = lines.Count > 0 ? lines[lines.Count - 1] : null;
                if (newest == null) { unseen = false; stick = true; }
                else if (newest != last)
                {
                    // Sending always brings you back to the bottom; a friend's line waits behind the pill
                    if (newest.Mine) stick = true;
                    else if (!stick && last != null) unseen = true;
                }
                last = newest;
                Relayout();
                if (stick) unseen = false;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Relayout();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollBy(-e.Delta * Px(36) / 120);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (unseen && e.Button == MouseButtons.Left && jump.Contains(e.Location)) ScrollBy(contentH);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var want = unseen && jump.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
                if (Cursor != want) Cursor = want;
            }

            public void ScrollBy(int dy)
            {
                int next = Math.Max(0, Math.Min(MaxScroll, scrollY + dy));
                stick = next >= MaxScroll;
                bool clear = stick && unseen;
                if (clear) unseen = false;
                if (next == scrollY && !clear) return;
                scrollY = next;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                KitInk.Smooth(g);
                if (lines.Count == 0)
                {
                    TextRenderer.DrawText(g, Lang.T("Belum ada pesan. Sapa temanmu!"), smallFont, ClientRectangle, dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    return;
                }
                if (rows.Count != lines.Count) Relayout();
                int w = ContentW, view = ClientSize.Height;
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    var r = rows[i];
                    int y = r.Y - scrollY;
                    if (y + r.H < 0 || y > view) continue;
                    if (l.Notice)
                    {
                        // Joined / left: a quiet divider across the history
                        int tw = TextRenderer.MeasureText(l.Text, smallFont, Size.Empty, Fit).Width;
                        tw = Math.Min(tw, w - Px(48));
                        int tx = (w - tw) / 2, mid = y + r.H / 2, line = Math.Max(1, Px(1));
                        var rb = KitInk.BrushOf(rule);
                        if (tx - Px(10) > Px(8))
                        {
                            g.FillRectangle(rb, Px(8), mid, tx - Px(10) - Px(8), line);
                            g.FillRectangle(rb, tx + tw + Px(10), mid, w - Px(8) - (tx + tw + Px(10)), line);
                        }
                        TextRenderer.DrawText(g, l.Text, smallFont, new Rectangle(tx, y, tw, r.H), dim, One | TextFormatFlags.HorizontalCenter);
                        continue;
                    }
                    int bx = l.Mine ? w - r.BodyW : 0;
                    if (r.Head)
                    {
                        string time = l.At.ToString("HH:mm");
                        int hh = nameFont.Height;
                        int timeW = TextRenderer.MeasureText(time, smallFont, Size.Empty, Fit).Width;
                        if (l.Mine)
                            TextRenderer.DrawText(g, time, smallFont, new Rectangle(w - PadX - timeW, y, timeW, hh), dim, One);
                        else
                        {
                            string who = string.IsNullOrEmpty(l.Sender) ? Lang.T("Teman") : l.Sender;
                            int nw = Math.Min(TextRenderer.MeasureText(who, nameFont, Size.Empty, Fit).Width, w - PadX - timeW - Px(8));
                            TextRenderer.DrawText(g, who, nameFont, new Rectangle(PadX, y, nw, hh), name, One);
                            TextRenderer.DrawText(g, time, smallFont, new Rectangle(PadX + nw + Px(6), y, timeW, hh), dim, One);
                        }
                        y += hh + Px(3);
                    }
                    var body = new Rectangle(bx, y, r.BodyW, r.BodyH);
                    KitInk.FillRound(g, body, Math.Min(Px(Ink.RadiusControl), body.Height * 0.36f), l.Mine ? mine : theirs);
                    TextRenderer.DrawText(g, r.Shown, textFont, new Rectangle(bx + PadX, y + PadY, r.BodyW - PadX * 2, r.BodyH - PadY * 2), fore, Wrap);
                }

                // Soft edges say there is more above or below
                int fade = Px(14);
                if (scrollY > 0) Fade(g, new Rectangle(0, 0, ClientSize.Width, fade), true);
                if (scrollY < MaxScroll) Fade(g, new Rectangle(0, view - fade, ClientSize.Width, fade), false);

                if (MaxScroll > 0)
                {
                    int track = view - Px(8);
                    int barH = Math.Max(Px(20), (int)((long)track * view / Math.Max(1, contentH)));
                    int barY = Px(4) + (int)((long)(track - barH) * scrollY / MaxScroll);
                    KitInk.FillRound(g, new RectangleF(ClientSize.Width - Px(4), barY, Px(3), barH), Px(1.5f), Color.FromArgb(110, dim));
                }

                jump = Rectangle.Empty;
                if (unseen)
                {
                    string label = Lang.T("Pesan baru");
                    var icon = Ink.Icons(Px(9f));
                    int tw = TextRenderer.MeasureText(label, nameFont, Size.Empty, Fit).Width;
                    int iw = Px(12), ph = Px(24), pw = Px(12) + tw + Px(4) + iw + Px(10);
                    jump = new Rectangle((w - pw) / 2, view - ph - Px(8), pw, ph);
                    KitInk.FillRound(g, jump, ph * 0.35f, accent);
                    TextRenderer.DrawText(g, label, nameFont, new Rectangle(jump.X + Px(12), jump.Y, tw, ph), onAccent, One);
                    KitInk.Draw(g, Ink.Str('\uE70D'), icon, onAccent, new RectangleF(jump.Right - Px(10) - iw, jump.Y, iw, ph), ContentAlignment.MiddleCenter);
                }
            }

            void Fade(Graphics g, Rectangle r, bool top)
            {
                if (r.Height <= 0 || r.Width <= 0) return;
                var solid = BackColor;
                var clear = Color.FromArgb(0, solid);
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2),
                    top ? solid : clear, top ? clear : solid, System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    g.FillRectangle(b, r);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

            sealed class Access : ControlAccessibleObject
            {
                readonly PetChatLog log;
                public Access(PetChatLog log) : base(log) { this.log = log; }
                public override string Value
                {
                    get
                    {
                        if (log.lines.Count == 0) return Lang.T("Belum ada pesan. Sapa temanmu!");
                        var l = log.lines[log.lines.Count - 1];
                        return l.Notice ? l.Text : l.Sender + ": " + l.Text;
                    }
                }
            }
        }
    }
}
