using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Compact floating compose popup anchored above the pet for sending quick chat & emotes.
        sealed class PetChatInput : Form
        {
            readonly float scale;
            readonly TextBox box;
            readonly Action<string, int> onSend;
            readonly Action<PetEmote> onEmote;
            readonly Action<PetPrank> onPrank;
            readonly Func<int> prankWait;
            readonly List<KitButton> prankButtons = new List<KitButton>();
            Label prankCaption;
            Timer prankTimer;
            readonly Color rim;
            readonly PetChatLog log;
            int selectedEmote = -1;
            // Room chat sits above the band where the pets' bubbles pop, so new bubbles don't cover it
            const int BubbleBand = 110;

            int Px(float v) { return (int)Math.Round(v * scale); }

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

            // No emote handler: the pet is being asked something, not chatting to friends
            bool Asking { get { return onEmote == null; } }
            // With the room's history: stays open to keep chatting
            bool Room { get { return log != null; } }

            public PetChatInput(float scale, int availableWidth, Action<string, int> onSend, Action<PetEmote> onEmote, string cue = null,
                Action<PetPrank> onPrank = null, Func<PetPrank, string> prankLabel = null, Func<int> prankWait = null,
                List<PetChatLine> history = null)
            {
                this.scale = scale > 0 ? scale : 1f;
                this.onSend = onSend;
                this.onEmote = onEmote;
                this.onPrank = Asking ? null : onPrank;
                this.prankWait = prankWait;

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                TopMost = true;
                KeyPreview = true;
                Text = Asking ? Lang.T("Tanya pet") : history != null ? Lang.T("Chat room") : Lang.T("Kirim pesan lewat pet");

                var tone = Ink.Base;
                bool dark = tone.IsDark;
                BackColor = Ink.Mix(tone.Bg, tone.Text, dark ? 0.08f : 0.015f);
                Color fore = Color.FromArgb(255, tone.Text.R, tone.Text.G, tone.Text.B);
                Color fieldBg = Ink.Mix(BackColor, fore, dark ? 0.08f : 0.05f);
                rim = Ink.Mix(BackColor, fore, dark ? 0.18f : 0.14f);

                int pad = Px(10);
                bool room = history != null && !Asking;
                int winW = Math.Max(Px(250), Math.Min(Px(Asking ? 340 : room ? 320 : 300), availableWidth - Px(16)));
                int sendW = Px(62);
                int boxH = Px(28);
                int logH = room ? Px(200) : 0;
                int row1Y = room ? pad + logH + Px(8) : pad;
                int row2Y = row1Y + boxH + Px(8);
                int emoteH = Px(26);
                int capH = Px(18);
                int row3Y = row2Y + emoteH + Px(10);
                int prankY = row3Y + capH;
                int winH = Asking ? row1Y + boxH + pad : this.onPrank != null ? prankY + emoteH + pad : row2Y + emoteH + pad;
                ClientSize = new Size(winW, winH);

                if (room)
                {
                    log = new PetChatLog(history, this.scale, BackColor, fore, dark)
                    {
                        Bounds = new Rectangle(pad, pad, winW - pad * 2, logH)
                    };
                    Controls.Add(log);
                    log.Added();
                }

                box = new TextBox
                {
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = fieldBg,
                    ForeColor = fore,
                    Font = Ink.Text(Px(12.5f)),
                    MaxLength = Asking ? 500 : PetPacket.MaxChatChars,
                    Bounds = new Rectangle(pad, row1Y, winW - pad * 2 - sendW - Px(6), boxH)
                };
                box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        Submit();
                    }
                    else if (e.KeyCode == Keys.Escape)
                    {
                        e.SuppressKeyPress = true;
                        Close();
                    }
                };
                Controls.Add(box);
                // Grey hint inside the empty box (EM_SETCUEBANNER, shown even while focused)
                if (!string.IsNullOrEmpty(cue)) box.HandleCreated += delegate { SendMessage(box.Handle, 0x1501, (IntPtr)1, cue); };

                var sendBtn = new KitButton
                {
                    Text = Asking ? Lang.T("Tanya") : Lang.T("Kirim"),
                    Primary = true,
                    Font = Ink.Semi(Px(12f)),
                    Bounds = new Rectangle(box.Right + Px(6), row1Y, sendW, boxH)
                };
                sendBtn.Click += delegate { Submit(); };
                Controls.Add(sendBtn);

                // Quick emote buttons
                var emotes = new[]
                {
                    new { Kind = PetEmote.Heart, Label = "\u2665" },
                    new { Kind = PetEmote.Spark, Label = "\u2726" },
                    new { Kind = PetEmote.Note,  Label = "\u266A" },
                    new { Kind = PetEmote.Bang,  Label = "!" }
                };
                int ex = pad;
                int ew = Px(34);
                if (!Asking) foreach (var item in emotes)
                {
                    var kind = item.Kind;
                    var eb = new KitButton
                    {
                        Text = item.Label,
                        Primary = false,
                        Font = Ink.Semi(Px(12.5f)),
                        Bounds = new Rectangle(ex, row2Y, ew, emoteH)
                    };
                    eb.Click += delegate
                    {
                        if (string.IsNullOrWhiteSpace(box.Text))
                        {
                            if (this.onEmote != null) this.onEmote(kind);
                            if (Room) box.Focus();
                            else Close();
                        }
                        else
                        {
                            selectedEmote = (int)kind;
                            Submit();
                        }
                    };
                    Controls.Add(eb);
                    ex += ew + Px(6);
                }

                // Mischief on the friend's screen: a short overlay there that clicks pass through
                if (this.onPrank != null)
                {
                    prankCaption = new Label
                    {
                        Text = Lang.T("Rusuh di layar teman"),
                        AutoSize = false,
                        BackColor = BackColor,
                        ForeColor = Ink.Mix(BackColor, fore, 0.6f),
                        Font = Ink.Text(Px(10.5f)),
                        Bounds = new Rectangle(pad, row3Y, winW - pad * 2, capH)
                    };
                    Controls.Add(prankCaption);
                    var pranks = new[] { PetPrank.Fire, PetPrank.Crack, PetPrank.Paint, PetPrank.Snow };
                    int gap = Px(6);
                    int pw = (winW - pad * 2 - gap * (pranks.Length - 1)) / pranks.Length;
                    int px = pad;
                    foreach (var pk in pranks)
                    {
                        var kind = pk;
                        var pb = new KitButton
                        {
                            Text = prankLabel != null ? prankLabel(kind) : kind.ToString(),
                            Primary = false,
                            Font = Ink.Semi(Px(11f)),
                            Bounds = new Rectangle(px, prankY, pw, emoteH)
                        };
                        pb.Click += delegate
                        {
                            if (PrankSecondsLeft() > 0) { SyncPrankButtons(); return; }
                            var act = this.onPrank;
                            Close();
                            if (act != null) act(kind);
                        };
                        Controls.Add(pb);
                        prankButtons.Add(pb);
                        px += pw + gap;
                    }
                    SyncPrankButtons();
                    if (PrankSecondsLeft() > 0)
                    {
                        // Counts down while open, and unlocks the buttons when the wait is over
                        prankTimer = new Timer { Interval = 500 };
                        prankTimer.Tick += delegate { SyncPrankButtons(); };
                        prankTimer.Start();
                    }
                }

                Deactivate += delegate { if (!IsDisposed) Close(); };
                using (var path = Ink.Round(new RectangleF(0, 0, winW, winH), Px(Ink.RadiusCard)))
                    Region = new Region(path);
            }

            int PrankSecondsLeft() { return prankWait != null ? Math.Max(0, prankWait()) : 0; }

            void SyncPrankButtons()
            {
                if (prankCaption == null) return;
                int left = PrankSecondsLeft();
                bool ready = left <= 0;
                prankCaption.Text = ready
                    ? Lang.T("Rusuh di layar teman")
                    : string.Format(Lang.T("Rusuh lagi dalam {0} dtk"), left);
                foreach (var b in prankButtons)
                    if (b.Enabled != ready) b.Enabled = ready;
                if (ready && prankTimer != null) prankTimer.Stop();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && prankTimer != null) { prankTimer.Stop(); prankTimer.Dispose(); prankTimer = null; }
                base.Dispose(disposing);
            }

            public void LogChanged() { if (log != null && !log.IsDisposed) log.Added(); }

            // The wheel scrolls the history wherever the pointer is in the window
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (log != null) log.ScrollBy(-e.Delta * Px(36) / 120);
            }

            public bool MoveAbove(Rectangle area, Point anchor)
            {
                if (Room)
                {
                    var high = PetChatBubble.BoundsAbove(area, new Point(anchor.X, anchor.Y - Px(BubbleBand)), Size, Px(8));
                    if (!high.IsEmpty) { Bounds = high; return true; }
                }
                var r = PetChatBubble.BoundsAbove(area, anchor, Size, Px(8));
                if (r.IsEmpty) return false;
                Bounds = r;
                return true;
            }

            public void FocusInput()
            {
                Activate();
                box.Focus();
            }

            void Submit()
            {
                string txt = (box.Text ?? "").Trim();
                int em = selectedEmote;
                if (Room)
                {
                    // Keep chatting: clear the box, the message lands in the history and above the pet
                    box.Clear();
                    selectedEmote = -1;
                    box.Focus();
                    if (txt.Length > 0 && onSend != null) onSend(txt, em);
                    return;
                }
                Close();
                if (txt.Length > 0 && onSend != null) onSend(txt, em);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                KitInk.Smooth(g);
                float inset = Math.Max(0.75f, 0.85f * scale);
                KitInk.StrokeRound(g, new RectangleF(inset, inset, ClientSize.Width - 2 * inset, ClientSize.Height - 2 * inset),
                    Math.Max(0, Px(Ink.RadiusCard) - inset), rim, Math.Max(1f, scale));
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                    return cp;
                }
            }
        }
    }
}
