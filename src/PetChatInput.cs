using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Kit button for the chat window: a pet pixel icon or an icon-font glyph. Ghost has no fill at rest.
        internal sealed class PetChatButton : KitButton
        {
            public string[] Pixels;
            public bool Ghost;
            public float IconScale = 1f;

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Pixels == null && !Ghost) { base.OnPaint(e); return; }
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Under(this));
                float s = UiScale;
                var r = Body(s);
                bool live = Enabled;
                if (live && (pressed || hovered || !Ghost))
                    KitInk.FillRound(g, r, Radius(r, s), pressed ? KitInk.Tint(0.22f) : hovered ? KitInk.Tint(0.17f) : KitInk.Line);
                if (pressed && live) r.Offset(0, 0.75f * s);
                Color ink = !live ? KitInk.Faint : Ghost && !hovered ? KitInk.Sub : KitInk.Text;
                if (Pixels != null)
                {
                    int unit = Math.Max(1, (int)Math.Round(2 * IconScale)), size = 7 * unit;
                    PetIcons.Paint(g, Pixels, ink, (int)Math.Round(r.X + (r.Width - size) / 2f), (int)Math.Round(r.Y + (r.Height - size) / 2f), unit);
                }
                else KitInk.Draw(g, Text, Font, ink, r, ContentAlignment.MiddleCenter);
                DrawRing(g, r, s);
            }
        }

        // Compact floating window anchored above the pet: the room's chat with quick emotes and pranks,
        // or a single question to the pet (Asking).
        sealed class PetChatInput : Form
        {
            const char IcoSend = '\uE724', IcoClose = '\uE711';
            // Room chat sits above the band where the pets' bubbles pop, so new bubbles don't cover it
            const int BubbleBand = 110;
            const int LogTall = 220, LogShort = 120;

            readonly float scale;
            readonly KitText box;
            readonly KitButton sendBtn;
            readonly PetChatButton closeBtn;
            readonly List<PetChatButton> emoteButtons = new List<PetChatButton>();
            readonly Action<string, int> onSend;
            readonly Action<PetEmote> onEmote;
            readonly Action<PetPrank> onPrank;
            readonly Func<int> prankWait;
            readonly List<KitButton> prankButtons = new List<KitButton>();
            readonly ToolTip tips = new ToolTip { InitialDelay = 500, ShowAlways = true };
            readonly Color fore, dim, rim, fieldBg, fieldRim, fieldFocus, warn;
            readonly PetChatLog log;
            readonly bool room;
            readonly int winW, maxChars;
            Label prankCaption;
            Timer prankTimer;
            Rectangle field, hintRect;
            int headerH, logH;
            bool counterOn;
            int selectedEmote = -1;

            int Px(float v) { return (int)Math.Round(v * scale); }

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
                room = history != null && !Asking;

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                TopMost = true;
                Text = Asking ? Lang.T("Tanya pet") : room ? Lang.T("Chat room") : Lang.T("Kirim pesan lewat pet");
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

                var tone = Ink.Base;
                bool dark = tone.IsDark;
                BackColor = Ink.Mix(tone.Bg, tone.Text, dark ? 0.08f : 0.015f);
                fore = Color.FromArgb(255, tone.Text.R, tone.Text.G, tone.Text.B);
                dim = Ink.Mix(BackColor, fore, 0.64f);
                rim = Ink.Mix(BackColor, fore, dark ? 0.16f : 0.13f);
                fieldBg = Ink.Mix(BackColor, fore, dark ? 0.07f : 0.045f);
                fieldRim = Ink.Mix(BackColor, fore, dark ? 0.14f : 0.16f);
                fieldFocus = Ink.Mix(BackColor, KitInk.Focus, 0.75f);
                warn = Color.FromArgb(255, tone.Negative.R, tone.Negative.G, tone.Negative.B);
                maxChars = Asking ? 500 : PetPacket.MaxChatChars;
                winW = Math.Max(Px(260), Math.Min(Px(Asking ? 340 : room ? 340 : 300), availableWidth - Px(16)));

                if (room)
                {
                    log = new PetChatLog(history, this.scale, BackColor, fore, dark);
                    Controls.Add(log);
                    closeBtn = new PetChatButton
                    {
                        Ghost = true,
                        IconScale = this.scale,
                        Text = Ink.Str(IcoClose),
                        Font = Ink.Icons(Px(10.5f)),
                        AccessibleName = Lang.T("Tutup"),
                        TabStop = false
                    };
                    closeBtn.Click += delegate { Close(); };
                    Controls.Add(closeBtn);
                    tips.SetToolTip(closeBtn, Lang.T("Tutup") + " (Esc)");
                }

                // KitText: Ctrl+Backspace and Ctrl+Delete remove a word, and undo still works
                box = new KitText
                {
                    BorderStyle = BorderStyle.None,
                    BackColor = fieldBg,
                    ForeColor = fore,
                    Font = Ink.Text(Px(13f)),
                    MaxLength = maxChars,
                    AccessibleName = Lang.T("Isi pesan"),
                    TabIndex = 0
                };
                box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt)
                    {
                        e.SuppressKeyPress = true;
                        Submit();
                    }
                };
                box.TextChanged += delegate { SyncComposer(); };
                box.GotFocus += delegate { Invalidate(Rectangle.Inflate(field, Px(2), Px(2))); };
                box.LostFocus += delegate { Invalidate(Rectangle.Inflate(field, Px(2), Px(2))); };
                Controls.Add(box);
                // Grey hint inside the empty box (EM_SETCUEBANNER, shown even while focused)
                string hint = !string.IsNullOrEmpty(cue) ? cue : Lang.T("Tulis pesan…");
                box.AccessibleDescription = hint;
                box.HandleCreated += delegate { Native.SendMessage(box.Handle, 0x1501, (IntPtr)1, hint); };

                string sendName = Asking ? Lang.T("Tanya") : Lang.T("Kirim");
                sendBtn = new KitButton
                {
                    Primary = true,
                    Text = Ink.Str(IcoSend),
                    Font = Ink.Icons(Px(14f)),
                    AccessibleName = sendName,
                    TabIndex = 1,
                    Enabled = false
                };
                sendBtn.Click += delegate { Submit(); };
                Controls.Add(sendBtn);
                tips.SetToolTip(sendBtn, sendName + " (Enter)");

                if (!Asking)
                {
                    var emotes = new[]
                    {
                        new { Kind = PetEmote.Heart, Icon = PetIcons.Heart, Name = Lang.T("Hati") },
                        new { Kind = PetEmote.Spark, Icon = PetIcons.Spark, Name = Lang.T("Kilau") },
                        new { Kind = PetEmote.Note,  Icon = PetIcons.Note,  Name = Lang.T("Nada") },
                        new { Kind = PetEmote.Bang,  Icon = PetIcons.Bang,  Name = Lang.T("Kaget") }
                    };
                    int tab = 2;
                    foreach (var item in emotes)
                    {
                        var kind = item.Kind;
                        var eb = new PetChatButton { Pixels = item.Icon, IconScale = this.scale, AccessibleName = item.Name, TabIndex = tab++ };
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
                                // With a draft, the emote rides along with the message
                                selectedEmote = (int)kind;
                                Submit();
                            }
                        };
                        Controls.Add(eb);
                        emoteButtons.Add(eb);
                        tips.SetToolTip(eb, item.Name);
                    }
                }

                // Mischief on the friend's screen: a short overlay there that clicks pass through
                if (this.onPrank != null)
                {
                    prankCaption = new Label
                    {
                        Text = Lang.T("Rusuh di layar teman"),
                        AutoSize = false,
                        UseMnemonic = false,
                        BackColor = BackColor,
                        ForeColor = dim,
                        Font = Ink.Text(Px(11f)),
                        TextAlign = ContentAlignment.MiddleLeft
                    };
                    Controls.Add(prankCaption);
                    foreach (var pk in new[] { PetPrank.Fire, PetPrank.Crack, PetPrank.Paint, PetPrank.Snow })
                    {
                        var kind = pk;
                        var pb = new KitButton
                        {
                            Text = prankLabel != null ? prankLabel(kind) : kind.ToString(),
                            Primary = false,
                            Font = Ink.Semi(Px(11.5f)),
                            TabIndex = 10 + prankButtons.Count
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

                Lay(Px(LogTall));
                if (log != null) log.Added();
                ActiveControl = box;
                Deactivate += delegate { if (!IsDisposed) Close(); };
            }

            // Header, history, composer, emotes, pranks: top to bottom, 12 px inset, sized to fit
            void Lay(int historyH)
            {
                int pad = Px(12), y;
                logH = room ? historyH : 0;
                if (room)
                {
                    headerH = Px(40);
                    int cs = Px(28);
                    closeBtn.Bounds = new Rectangle(winW - Px(8) - cs, (headerH - cs) / 2, cs, cs);
                    // The history reaches into the right inset so its scrollbar sits in the margin
                    log.Bounds = new Rectangle(pad, headerH + Px(6), winW - pad * 2 + Px(6), logH);
                    y = log.Bottom + Px(10);
                }
                else
                {
                    headerH = 0;
                    y = pad;
                }

                int fieldH = Px(36), gap = Px(8);
                field = new Rectangle(pad, y, winW - pad * 2 - fieldH - gap, fieldH);
                sendBtn.Bounds = new Rectangle(field.Right + gap, y, fieldH, fieldH);
                PlaceBox();
                y = field.Bottom;

                if (emoteButtons.Count > 0)
                {
                    y += Px(8);
                    int ex = pad, ew = Px(36), eh = Px(30);
                    foreach (var b in emoteButtons) { b.Bounds = new Rectangle(ex, y, ew, eh); ex += ew + Px(4); }
                    hintRect = new Rectangle(ex + Px(8), y, Math.Max(0, winW - pad - ex - Px(8)), eh);
                    y += eh;
                }
                else hintRect = Rectangle.Empty;

                if (prankCaption != null)
                {
                    y += Px(12);
                    prankCaption.Bounds = new Rectangle(pad, y, winW - pad * 2, Px(18));
                    y += Px(18) + Px(4);
                    int g = Px(6), n = prankButtons.Count, pw = (winW - pad * 2 - g * (n - 1)) / n, px = pad;
                    foreach (var pb in prankButtons) { pb.Bounds = new Rectangle(px, y, pw, Px(30)); px += pw + g; }
                    y += Px(30);
                }

                ClientSize = new Size(winW, y + pad);
                var old = Region;
                using (var path = Ink.Round(new RectangleF(0, 0, winW, ClientSize.Height), Px(Ink.RadiusCard)))
                    Region = new Region(path);
                if (old != null) old.Dispose();
                Invalidate();
            }

            // The counter takes the box's right end only while it shows
            void PlaceBox()
            {
                int h = box.PreferredHeight, left = Px(12), right = counterOn ? Px(68) : Px(12);
                box.SetBounds(field.X + left, field.Y + (field.Height - h) / 2, Math.Max(Px(20), field.Width - left - right), h);
            }

            void SyncComposer()
            {
                bool any = (box.Text ?? "").Trim().Length > 0;
                if (sendBtn.Enabled != any) sendBtn.Enabled = any;
                bool near = box.TextLength >= maxChars - 40, flip = near != counterOn;
                counterOn = near;
                if (flip) PlaceBox();
                if (flip || near) Invalidate(field);
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
                if (disposing)
                {
                    if (prankTimer != null) { prankTimer.Stop(); prankTimer.Dispose(); prankTimer = null; }
                    tips.Dispose();
                }
                base.Dispose(disposing);
            }

            public void LogChanged() { if (log != null && !log.IsDisposed) log.Added(); }

            // Esc closes from any control in the window
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape) { Close(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }

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
                    // Tall history clear of the bubble band; on a short screen, a shorter history before giving up the band
                    foreach (int h in new[] { LogTall, LogShort })
                    {
                        if (Px(h) != logH) Lay(Px(h));
                        var high = PetChatBubble.BoundsAbove(area, new Point(anchor.X, anchor.Y - Px(BubbleBand)), Size, Px(8));
                        if (!high.IsEmpty) { Bounds = high; return true; }
                    }
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

                if (room)
                {
                    KitInk.Draw(g, Text, Ink.Semi(Px(13.5f)), fore,
                        new RectangleF(Px(16), 0, closeBtn.Left - Px(16) - Px(8), headerH), ContentAlignment.MiddleLeft);
                    g.FillRectangle(KitInk.BrushOf(rim), 0, headerH - Math.Max(1, Px(1)), winW, Math.Max(1, Px(1)));
                }

                // Composer well: quiet at rest, accent edge while typing
                float radius = Math.Min(field.Height * 0.35f, Px(Ink.RadiusControl));
                KitInk.FillRound(g, field, radius, fieldBg);
                bool focused = box.Focused;
                float edge = Math.Max(1f, scale * (focused ? 1.25f : 1f));
                var er = RectangleF.Inflate(field, -edge / 2, -edge / 2);
                KitInk.StrokeRound(g, er, radius - edge / 2, focused ? fieldFocus : fieldRim, edge);

                if (counterOn)
                {
                    int n = box.TextLength;
                    var cr = new RectangleF(box.Right + Px(4), field.Y, field.Right - Px(12) - box.Right - Px(4), field.Height);
                    KitInk.Draw(g, n + "/" + maxChars, Ink.Text(Px(11f)), n >= maxChars ? warn : dim, cr, ContentAlignment.MiddleRight);
                }

                if (hintRect.Width > Px(60))
                    KitInk.Draw(g, Lang.T("Enter kirim · Esc tutup"), Ink.Text(Px(11f)), dim, hintRect, ContentAlignment.MiddleRight);

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
