using System;
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
            readonly Color rim;
            int selectedEmote = -1;

            int Px(float v) { return (int)Math.Round(v * scale); }

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

            // No emote handler: the pet is being asked something, not chatting to friends
            bool Asking { get { return onEmote == null; } }

            public PetChatInput(float scale, int availableWidth, Action<string, int> onSend, Action<PetEmote> onEmote, string cue = null,
                Action<PetPrank> onPrank = null, Func<PetPrank, string> prankLabel = null)
            {
                this.scale = scale > 0 ? scale : 1f;
                this.onSend = onSend;
                this.onEmote = onEmote;
                this.onPrank = Asking ? null : onPrank;

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                TopMost = true;
                KeyPreview = true;
                Text = Asking ? Lang.T("Tanya pet") : Lang.T("Kirim pesan lewat pet");

                var tone = Ink.Base;
                bool dark = tone.IsDark;
                BackColor = Ink.Mix(tone.Bg, tone.Text, dark ? 0.08f : 0.015f);
                Color fore = Color.FromArgb(255, tone.Text.R, tone.Text.G, tone.Text.B);
                Color fieldBg = Ink.Mix(BackColor, fore, dark ? 0.08f : 0.05f);
                rim = Ink.Mix(BackColor, fore, dark ? 0.18f : 0.14f);

                int pad = Px(10);
                int winW = Math.Max(Px(250), Math.Min(Px(Asking ? 340 : 300), availableWidth - Px(16)));
                int sendW = Px(62);
                int boxH = Px(28);
                int row1Y = pad;
                int row2Y = row1Y + boxH + Px(8);
                int emoteH = Px(26);
                int capH = Px(18);
                int row3Y = row2Y + emoteH + Px(10);
                int prankY = row3Y + capH;
                int winH = Asking ? row1Y + boxH + pad : this.onPrank != null ? prankY + emoteH + pad : row2Y + emoteH + pad;
                ClientSize = new Size(winW, winH);

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
                            Close();
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
                    Controls.Add(new Label
                    {
                        Text = Lang.T("Rusuh di layar teman"),
                        AutoSize = false,
                        BackColor = BackColor,
                        ForeColor = Ink.Mix(BackColor, fore, 0.6f),
                        Font = Ink.Text(Px(10.5f)),
                        Bounds = new Rectangle(pad, row3Y, winW - pad * 2, capH)
                    });
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
                            var act = this.onPrank;
                            Close();
                            if (act != null) act(kind);
                        };
                        Controls.Add(pb);
                        px += pw + gap;
                    }
                }

                Deactivate += delegate { if (!IsDisposed) Close(); };
                using (var path = Ink.Round(new RectangleF(0, 0, winW, winH), Px(Ink.RadiusCard)))
                    Region = new Region(path);
            }

            public bool MoveAbove(Rectangle area, Point anchor)
            {
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
