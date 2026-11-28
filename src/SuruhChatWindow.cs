using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Suruh Deskmate window: header, chat, text box
        class ChatWindow : KitWindow
        {
            const float HeadDip = 62;
            readonly Island island;
            readonly ChatList list;
            readonly ChatWell well = new ChatWell();
            readonly ChatInput input = new ChatInput();
            readonly KitButton send = new KitButton { Primary = true };
            readonly KitButton fresh = new KitButton();
            readonly Timer pulse = new Timer { Interval = 40 };
            int lines = 1;
            // Busy animation clock
            public float Phase;

            public ChatWindow(Island island)
            {
                this.island = island;
                list = new ChatList(island, this);
                Text = Lang.T("Suruh Deskmate");
                StartPosition = FormStartPosition.Manual;
                input.Text = island.chatDraft;
                input.KeyDown += InputKey;
                input.TextChanged += delegate { Fit(); Ready(); };
                input.GotFocus += delegate { well.Lit = true; well.Invalidate(); };
                input.LostFocus += delegate { well.Lit = false; well.Invalidate(); };
                send.Click += delegate { Send(); };
                fresh.Click += delegate { list.Clear(); };
                pulse.Tick += delegate
                {
                    Phase += 0.3f;
                    list.Pulse();
                    Invalidate(Rectangle.Round(FaceRect()));
                };
                well.Controls.Add(input);
                well.Controls.Add(send);
                Controls.Add(list);
                Controls.Add(well);
                Controls.Add(fresh);
                // Type first, send next, read after
                well.TabIndex = 0;
                list.TabIndex = 1;
                fresh.TabIndex = 2;
                FormClosing += delegate
                {
                    island.chatDraft = input.Text;
                    if (WindowState == FormWindowState.Normal) island.chatBounds = Bounds;
                };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ShellApp.SetWindowAppId(Handle, "Deskmate.Suruh");
                MinimumSize = new Size((int)(340 * Ui), (int)(400 * Ui));
                bool seen = false;
                foreach (var sc in Screen.AllScreens) if (sc.WorkingArea.IntersectsWith(island.chatBounds)) seen = true;
                if (seen) Bounds = island.chatBounds;
                else
                {
                    var area = Screen.FromHandle(island.Handle).WorkingArea;
                    int chromeW = Width - ClientSize.Width, chromeH = Height - ClientSize.Height;
                    var size = new Size(Math.Min((int)(420 * Ui) + chromeW, area.Width - 40), Math.Min((int)(600 * Ui) + chromeH, area.Height - 40));
                    Bounds = new Rectangle(island.ChatPlace(size), size);
                }
                Relayout();
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                Fit();
                list.ToEnd();
                FocusInput();
                island.SyncSuruhDock();
            }

            // Settings may have changed meanwhile
            protected override void OnActivated(EventArgs e)
            {
                base.OnActivated(e);
                if (!IsHandleCreated) return;
                list.Relayout();
                Invalidate(new Rectangle(0, 0, ClientSize.Width, (int)(HeadDip * Ui)));
                island.SyncSuruhDock();
            }

            protected override void OnDeactivate(EventArgs e)
            {
                base.OnDeactivate(e);
                if (IsHandleCreated) island.SyncSuruhDock();
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if (IsHandleCreated) island.SyncSuruhDock();
            }

            protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated) { Place(); island.SyncSuruhDock(); } }

            protected override void OnFormClosed(FormClosedEventArgs e) { pulse.Stop(); base.OnFormClosed(e); }

            // A question placed by another feature (the pet's Ingatan button)
            public void SetDraft(string text)
            {
                input.Text = text ?? "";
                FocusInput();
            }

            public void FocusInput()
            {
                if (!input.CanFocus) return;
                input.Focus();
                input.SelectionStart = input.TextLength;
            }

            public override void Relayout()
            {
                float s = Ui;
                BackColor = KitInk.Window;
                Native.DarkFrame(Handle, !Ink.Light);
                Text = Lang.T("Suruh Deskmate");
                well.BackColor = KitInk.Group;
                input.Font = Ink.Text(14f * s);
                input.BackColor = KitInk.Group;
                input.ForeColor = KitInk.Text;
                input.Cue = Lang.T("Tanya atau suruh Deskmate…");
                input.AccessibleName = Lang.T("Suruh Deskmate");
                send.Font = Ink.Icons(14f * s);
                send.Text = Ink.Str(IcoSend);
                send.AccessibleName = Lang.T("Kirim");
                fresh.Font = Ink.Text(12.5f * s);
                fresh.Text = Lang.T("Obrolan baru");
                list.AccessibleName = Lang.T("Obrolan");
                lines = Math.Max(1, Math.Min(6, input.LineCount()));
                Place();
                list.Relayout();
                Ready();
                Invalidate(true);
            }

            void Place()
            {
                float s = Ui;
                int w = ClientSize.Width, h = ClientSize.Height;
                int lineH = input.Font.Height, inputH = lines * lineH + 2;
                int wellH = inputH + (int)(28 * s);
                int fw = TextRenderer.MeasureText(fresh.Text, fresh.Font).Width + (int)(26 * s);
                fresh.SetBounds(w - (int)(16 * s) - fw, (int)(16 * s), fw, (int)(30 * s));
                well.SetBounds((int)(12 * s), h - (int)(12 * s) - wellH, w - (int)(24 * s), wellH);
                int head = (int)(HeadDip * s);
                list.SetBounds(0, head, w, Math.Max(10, well.Top - (int)(6 * s) - head));
                // Send: centred on one line, low as text grows
                int d = (int)(32 * s), low = Math.Max(2, (lineH + 2 + (int)(28 * s) - d) / 2);
                send.SetBounds(well.Width - low - (int)(2 * s) - d, wellH - low - d, d, d);
                int ix = (int)(18 * s);
                input.SetBounds(ix, (int)(14 * s), Math.Max(10, send.Left - (int)(8 * s) - ix), inputH);
                Invalidate();
            }

            // The box grows to six lines
            void Fit()
            {
                int n = Math.Max(1, Math.Min(6, input.LineCount()));
                if (n == lines) return;
                lines = n;
                Place();
            }

            void Ready()
            {
                send.Enabled = input.Text.Trim().Length > 0 && !island.suruhBusy;
                fresh.Enabled = island.chat != null && island.chat.Count > 0 && !island.suruhBusy;
                if (island.suruhBusy && !Anim.Reduced) pulse.Start();
                else pulse.Stop();
            }

            // From the island: new message or busy changed
            public void Changed(ChatMsg added)
            {
                list.Reflow(added);
                Ready();
                Invalidate(new Rectangle(0, 0, ClientSize.Width, (int)(HeadDip * Ui)));
                if (added != null && !added.Mine && Form.ActiveForm == this) Say(ChatSummary(added));
            }

            // Readers hear replies while the chat is open
            void Say(string text)
            {
                if (!island.settings.GetBool("a11y.announce", true) || !ScreenReaderOn()) return;
                try
                {
                    AccessibilityObject.RaiseAutomationNotification(
                        System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                        System.Windows.Forms.Automation.AutomationNotificationProcessing.MostRecent, text);
                }
                catch (Exception ex) { Log.Write("obrolan announce: " + ex.Message); }
            }

            void InputKey(object sender, KeyEventArgs e)
            {
                // Enter sends; Shift+Enter starts a new line
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    Send();
                    return;
                }
                // Up in an empty box: the last ask, like a terminal
                if (e.KeyCode == Keys.Up && input.TextLength == 0)
                {
                    string last = island.LastAsk();
                    if (last == null) return;
                    e.SuppressKeyPress = true;
                    input.Text = last;
                    input.SelectionStart = input.TextLength;
                }
            }

            void Send()
            {
                string text = input.Text.Trim();
                if (text.Length == 0 || island.suruhBusy) return;
                input.Clear();
                island.chatDraft = "";
                island.RunSuruh(text);
                FocusInput();
            }

            // An idea or a past ask, ready to edit
            public void Suggest(string text)
            {
                input.Text = text;
                FocusInput();
            }

            RectangleF FaceRect() { float s = Ui; return new RectangleF(16 * s, 15 * s, 32 * s, 32 * s); }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(KitInk.Window);
                KitInk.Smooth(g);
                float s = Ui, head = HeadDip * s;
                var face = FaceRect();
                DrawFace(g, face, island.suruhBusy && !Anim.Reduced ? (float)Math.Sin(Phase * 0.45f) : 0);
                float tx = face.Right + 10 * s, tw = Math.Max(10, fresh.Left - 10 * s - tx);
                KitInk.Draw(g, "Deskmate", Ink.Semi(14.5f * s), KitInk.Text, new RectangleF(tx, 12 * s, tw, 20 * s), ContentAlignment.MiddleLeft);
                KitInk.Draw(g, island.ChatStatus(), Ink.Text(12f * s), KitInk.Sub, new RectangleF(tx, 31 * s, tw, 18 * s), ContentAlignment.MiddleLeft);
                using (var pen = new Pen(KitInk.Line, 1f)) g.DrawLine(pen, 0, head - 1, ClientSize.Width, head - 1);
            }

            protected override void Dispose(bool disposing) { if (disposing) pulse.Dispose(); base.Dispose(disposing); }
        }
    }
}
