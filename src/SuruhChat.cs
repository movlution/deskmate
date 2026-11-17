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
    // One message in the Suruh Deskmate chat
    class ChatMsg
    {
        // "me" or "deskmate"
        public string Who = "deskmate";
        public string Text = "";
        public DateTime Utc = DateTime.UtcNow;
        public readonly List<ChatAct> Acts = new List<ChatAct>();
        // Failed reply: "retry", or "ai" when none is set up
        public string Fix;
        public bool Mine { get { return Who == "me"; } }
    }

    // Something Deskmate did, shown as a chip
    class ChatAct
    {
        public char Glyph;
        public string Text = "";
        // Pending reminder: the chip can cancel it
        public OneShot Reminder;
        public bool Undone;
    }

    // Suruh Deskmate as a chat: kept on disk, shown in its own window
    partial class Island
    {
        const int ChatKeep = 200, ChatContext = 16;
        const char IcoNote = '', IcoFail = '', IcoDone = '', IcoCopy = '', IcoSend = '';
        List<ChatMsg> chat;
        // Cleared chat, back with Undo for a moment
        List<ChatMsg> chatCleared;
        ChatWindow chatWin;
        // Spot and unsent text, for this run
        Rectangle chatBounds;
        string chatDraft = "";

        static string ChatPath { get { return Path.Combine(Settings.Dir, "obrolan.json"); } }

        void OpenSuruh()
        {
            expanded = false;
            Kick();
            LoadChat();
            if (chatWin == null || chatWin.IsDisposed)
            {
                chatWin = new ChatWindow(this);
                chatWin.FormClosed += delegate { chatWin = null; chatCleared = null; };
                chatWin.Show();
            }
            else if (chatWin.WindowState == FormWindowState.Minimized) chatWin.WindowState = FormWindowState.Normal;
            // Above the app in front, then focused
            chatWin.TopMost = true;
            chatWin.TopMost = false;
            chatWin.Activate();
            Native.SetForegroundWindow(chatWin.Handle);
            chatWin.FocusInput();
        }

        // Read once, on first use
        void LoadChat()
        {
            if (chat != null) return;
            chat = new List<ChatMsg>();
            try
            {
                if (!File.Exists(ChatPath)) return;
                var all = Json.Parse(File.ReadAllText(ChatPath, Encoding.UTF8)) as object[];
                if (all == null) return;
                foreach (var o in all)
                {
                    DateTime utc;
                    if (!DateTime.TryParse(Json.Str(o, "utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out utc)) utc = DateTime.UtcNow;
                    var m = new ChatMsg { Who = Json.Str(o, "who") == "me" ? "me" : "deskmate", Text = Json.Str(o, "text"), Utc = utc.ToUniversalTime() };
                    string fix = Json.Str(o, "fix");
                    if (fix.Length > 0) m.Fix = fix;
                    foreach (var a in Json.Arr(o, "acts"))
                        m.Acts.Add(new ChatAct
                        {
                            Glyph = (char)(int)Json.Num(a, "g", IcoDone),
                            Text = Json.Str(a, "t"),
                            Undone = Json.Str(a, "undone") == "True",
                            Reminder = FindOneShot(Json.Str(a, "at"), Json.Str(a, "what")),
                        });
                    chat.Add(m);
                }
            }
            catch (Exception ex) { Log.Write("obrolan: " + ex.Message); }
        }

        // A saved chip finds its reminder again
        OneShot FindOneShot(string at, string what)
        {
            if (at.Length == 0) return null;
            foreach (var o in oneShots)
                if (o.Text == what && o.At.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) == at) return o;
            return null;
        }

        void SaveChat()
        {
            try
            {
                while (chat.Count > ChatKeep) chat.RemoveAt(0);
                var all = new List<object>();
                foreach (var m in chat)
                {
                    var d = new Dictionary<string, object> { { "who", m.Who }, { "text", m.Text }, { "utc", m.Utc.ToString("o", CultureInfo.InvariantCulture) } };
                    if (m.Fix != null) d["fix"] = m.Fix;
                    if (m.Acts.Count > 0)
                    {
                        var acts = new List<object>();
                        foreach (var a in m.Acts)
                        {
                            var ad = new Dictionary<string, object> { { "g", (int)a.Glyph }, { "t", a.Text } };
                            if (a.Undone) ad["undone"] = true;
                            if (a.Reminder != null)
                            {
                                ad["at"] = a.Reminder.At.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                                ad["what"] = a.Reminder.Text;
                            }
                            acts.Add(ad);
                        }
                        d["acts"] = acts;
                    }
                    all.Add(d);
                }
                Directory.CreateDirectory(Settings.Dir);
                AtomicFile.Write(ChatPath, new UTF8Encoding(false).GetBytes(Json.Encode(all)));
            }
            catch (Exception ex) { Log.Write("simpan obrolan: " + ex.Message); }
        }

        // Replies follow their question
        void AddChat(ChatMsg m, ChatMsg after)
        {
            LoadChat();
            if (after == null) chat.Add(m);
            else
            {
                int at = chat.IndexOf(after);
                // Its question was cleared away
                if (at < 0) return;
                chat.Insert(at + 1, m);
            }
            SaveChat();
            ChatChanged(m);
        }

        void ChatChanged(ChatMsg added)
        {
            if (chatWin != null && !chatWin.IsDisposed) chatWin.Changed(added);
        }

        // AI turns up to this ask; failed replies left out
        List<KeyValuePair<string, string>> ChatTurns(ChatMsg last)
        {
            var turns = new List<KeyValuePair<string, string>>();
            int end = chat.IndexOf(last);
            for (int i = Math.Max(0, end - ChatContext + 1); i <= end; i++)
            {
                var m = chat[i];
                if (m.Fix != null) continue;
                string role = m.Mine ? "user" : "assistant", text = m.Mine ? m.Text : Recap(m);
                if (text.Length == 0) continue;
                int n = turns.Count;
                // Same speaker twice: one turn
                if (n > 0 && turns[n - 1].Key == role) turns[n - 1] = new KeyValuePair<string, string>(role, turns[n - 1].Value + "\n" + text);
                else turns.Add(new KeyValuePair<string, string>(role, text));
            }
            return turns;
        }

        // Deskmate's turn for the AI: words, then what it did
        static string Recap(ChatMsg m)
        {
            var sb = new StringBuilder(m.Text);
            foreach (var a in m.Acts) sb.Append(sb.Length > 0 ? "\n" : "").Append('(').Append(a.Text).Append(a.Undone ? ", cancelled" : "").Append(')');
            return sb.ToString();
        }

        // One line for toasts and the Voice list
        static string ChatSummary(ChatMsg m)
        {
            if (m.Text.Length > 0) return m.Text;
            var parts = new List<string>();
            foreach (var a in m.Acts) parts.Add(a.Text);
            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : Lang.T("Tidak ada yang dikerjakan");
        }

        bool ChatInFront { get { return chatWin != null && !chatWin.IsDisposed && Form.ActiveForm == chatWin; } }

        string LastAsk()
        {
            if (chat == null) return null;
            for (int i = chat.Count - 1; i >= 0; i--) if (chat[i].Mine) return chat[i].Text;
            return null;
        }

        void ClearChat()
        {
            if (chat == null || chat.Count == 0 || suruhBusy) return;
            chatCleared = chat;
            chat = new List<ChatMsg>();
            SaveChat();
            ChatChanged(null);
        }

        void RestoreChat()
        {
            if (chatCleared == null) return;
            // Anything said since stays after
            chatCleared.AddRange(chat);
            chat = chatCleared;
            chatCleared = null;
            SaveChat();
            ChatChanged(null);
        }

        bool CanUndo(ChatAct a) { return a.Reminder != null && !a.Undone && oneShots.Contains(a.Reminder); }

        void UndoAct(ChatAct a)
        {
            if (!CanUndo(a)) return;
            CancelOneShot(a.Reminder);
            a.Undone = true;
            SaveChat();
            ChatChanged(null);
        }

        string ChatStatus()
        {
            if (suruhBusy) return Lang.T("Sedang menjawab…");
            var p = Ai.Text;
            return Ai.Ready(p) ? Lang.F("Jawaban dari {0}", Ai.Label(p)) : Lang.T("Tanpa layanan AI");
        }

        // Beside the island: under it, or next to a side one
        Point ChatPlace(Size size)
        {
            var area = Screen.FromHandle(Handle).WorkingArea;
            int m = (int)(16 * scale), x, y = area.Y + (int)(64 * scale);
            switch (PlaceNow)
            {
                case PlaceTopLeft: x = area.X + m; break;
                case PlaceTopRight: x = area.Right - size.Width - m; break;
                case PlaceLeft: x = area.X + (int)(84 * scale); y = area.Y + (area.Height - size.Height) / 3; break;
                case PlaceRight: x = area.Right - size.Width - (int)(84 * scale); y = area.Y + (area.Height - size.Height) / 3; break;
                default: x = area.X + (area.Width - size.Width) / 2; break;
            }
            return new Point(Math.Max(area.X, Math.Min(area.Right - size.Width, x)), Math.Max(area.Y, Math.Min(area.Bottom - size.Height, y)));
        }

        // Deskmate's face: a dark disc, two eyes
        static void DrawFace(Graphics g, RectangleF r, float look)
        {
            using (var b = new SolidBrush(Color.FromArgb(255, 18, 18, 20))) g.FillEllipse(b, r);
            // Rim keeps it apart from a dark window
            using (var pen = new Pen(KitInk.Line, 1f)) g.DrawEllipse(pen, r);
            float d = r.Width, ew = d * 0.15f, eh = d * 0.27f, gap = d * 0.2f;
            float cx = r.X + d / 2 + look * d * 0.08f, cy = r.Y + d / 2;
            using (var b = new SolidBrush(Color.White))
                foreach (float x in new[] { cx - gap / 2 - ew, cx + gap / 2 })
                    using (var path = Ink.Round(new RectangleF(x, cy - eh / 2, ew, eh), ew / 2)) g.FillPath(b, path);
        }

        // Rounded box, each corner its own radius
        static GraphicsPath Corners(RectangleF r, float tl, float tr, float br, float bl)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90);
            p.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90);
            p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90);
            p.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Deskmate's bubbles: a step off the window
        static Color DeskmateFill { get { return Ink.Light ? KitInk.Hover : Ink.Mix(KitInk.Group, KitInk.Line, 0.4f); } }

    }
}
