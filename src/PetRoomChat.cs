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

        // Owner-drawn message list: sender and time over the wrapped text, newest at the bottom.
        // Follows new messages unless the user scrolled up to read older ones.
        internal sealed class PetChatLog : Control
        {
            readonly List<PetChatLine> lines;
            readonly float scale;
            readonly Color fore, dim, mine, notice;
            readonly Font nameFont, textFont, smallFont;
            readonly List<int> heights = new List<int>();
            int scrollY, contentH;
            bool stick = true;

            int Px(float v) { return (int)Math.Round(v * scale); }
            const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            const TextFormatFlags One = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

            public PetChatLog(List<PetChatLine> lines, float scale, Color back, Color fore, bool dark)
            {
                this.lines = lines;
                this.scale = scale > 0 ? scale : 1f;
                this.fore = fore;
                dim = Ink.Mix(back, fore, 0.55f);
                notice = Ink.Mix(back, fore, 0.45f);
                mine = Ink.Label("amber", !dark);
                BackColor = back;
                nameFont = Ink.Semi(Px(11f));
                textFont = Ink.Text(Px(12f));
                smallFont = Ink.Text(Px(10.5f));
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = false;
                AccessibleRole = AccessibleRole.List;
                AccessibleName = Lang.T("Chat room");
            }

            int Gap { get { return Px(8); } }
            int TextW { get { return Math.Max(Px(40), ClientSize.Width - Px(4)); } }
            int MaxScroll { get { return Math.Max(0, contentH - ClientSize.Height); } }
            internal int ContentHeight { get { return contentH; } }
            internal int ScrollOffset { get { return scrollY; } }

            int Measure(PetChatLine l)
            {
                if (l.Notice) return TextRenderer.MeasureText(l.Text, smallFont, new Size(TextW, int.MaxValue), Wrap).Height;
                return nameFont.Height + Px(2) + TextRenderer.MeasureText(l.Text, textFont, new Size(TextW, int.MaxValue), Wrap).Height;
            }

            void Relayout()
            {
                heights.Clear();
                contentH = 0;
                foreach (var l in lines)
                {
                    int h = Measure(l);
                    heights.Add(h);
                    contentH += h + Gap;
                }
                if (contentH > 0) contentH -= Gap;
                scrollY = stick ? MaxScroll : Math.Min(scrollY, MaxScroll);
            }

            // The log changed: a new message, or cleared
            public void Added()
            {
                Relayout();
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

            public void ScrollBy(int dy)
            {
                int next = Math.Max(0, Math.Min(MaxScroll, scrollY + dy));
                stick = next >= MaxScroll;
                if (next == scrollY) return;
                scrollY = next;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                if (lines.Count == 0)
                {
                    TextRenderer.DrawText(g, Lang.T("Belum ada pesan. Sapa temanmu!"), smallFont, ClientRectangle, dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    return;
                }
                if (heights.Count != lines.Count) Relayout();
                int y = -scrollY, w = TextW;
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    int h = heights[i];
                    if (y + h >= 0 && y <= ClientSize.Height)
                    {
                        if (l.Notice)
                        {
                            TextRenderer.DrawText(g, l.Text, smallFont, new Rectangle(0, y, w, h), notice,
                                Wrap | TextFormatFlags.HorizontalCenter);
                        }
                        else
                        {
                            string time = l.At.ToString("HH:mm");
                            int timeW = TextRenderer.MeasureText(time, smallFont, Size.Empty, One).Width;
                            TextRenderer.DrawText(g, l.Sender, nameFont, new Rectangle(0, y, w - timeW - Px(6), nameFont.Height),
                                l.Mine ? mine : fore, One);
                            TextRenderer.DrawText(g, time, smallFont, new Rectangle(w - timeW, y, timeW, nameFont.Height), dim,
                                One | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                            int ty = y + nameFont.Height + Px(2);
                            TextRenderer.DrawText(g, l.Text, textFont, new Rectangle(0, ty, w, h - (ty - y)), fore, Wrap);
                        }
                    }
                    y += h + Gap;
                }
                // Older messages above: a thin bar shows where the view is
                if (MaxScroll > 0)
                {
                    int trackH = ClientSize.Height;
                    int barH = Math.Max(Px(16), trackH * trackH / Math.Max(1, contentH));
                    int barY = (int)((long)(trackH - barH) * scrollY / MaxScroll);
                    using (var b = new SolidBrush(Color.FromArgb(90, dim)))
                        g.FillRectangle(b, ClientSize.Width - Px(3), barY, Px(2), barH);
                }
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
