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
        // Messages, chips, typing dots; scrolls on its own
        class ChatList : KitControl
        {
            const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
            const TextFormatFlags Single = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            static readonly string[][] Ideas =
            {
                new[] { "Ingetin minum 30 menit lagi", "" }, new[] { "Timer 25 menit", "" },
                new[] { "15% dari 240.000", "" }, new[] { "Catat: beli kopi", "" },
            };
            readonly Island island;
            readonly ChatWindow win;
            readonly List<Item> items = new List<Item>();
            readonly List<Tap> taps = new List<Tap>();
            readonly Timer glide = new Timer { Interval = 15 };
            readonly Timer tick = new Timer { Interval = 1200 };
            readonly Timer undoEnd = new Timer { Interval = 8000 };
            float scroll, target, contentH;
            // Follows new messages until scrolled up
            bool pinned = true;
            int laidW = -1, laidH = -1, focus = -1, hoverItem = -1;
            string hot, pressed;
            // Thumb drag: pointer offset inside it
            float grab = -1;
            ChatMsg copied;
            RectangleF emptyFace, titleRect, noteRect;
            string emptyNote = "";

            class Item
            {
                public ChatMsg Msg;
                public string Stamp;
                public bool Typing;
                public RectangleF Box, Bubble, Face;
                public readonly List<RectangleF> Chips = new List<RectangleF>();
            }

            class Tap
            {
                public string Id, Label = "";
                public char Glyph;
                public RectangleF Rect;
                public Action Act;
                // Copy shows only over its own message
                public int Item = -1;
                // Undo banner: fixed, not scrolled
                public bool Fixed;
            }

            public ChatList(Island island, ChatWindow win)
            {
                this.island = island;
                this.win = win;
                glide.Tick += delegate
                {
                    scroll += (target - scroll) * 0.28f;
                    if (Math.Abs(target - scroll) < 0.6f) { scroll = target; glide.Stop(); }
                    Track(PointToClient(Cursor.Position));
                    Invalidate();
                };
                tick.Tick += delegate { tick.Stop(); copied = null; Invalidate(); };
                undoEnd.Tick += delegate { undoEnd.Stop(); island.chatCleared = null; Reflow(null); };
            }

            List<ChatMsg> Chat { get { return island.chat ?? new List<ChatMsg>(); } }
            bool Empty { get { return Chat.Count == 0 && !island.suruhBusy; } }
            Font MsgFont { get { return Ink.Text(14f * Ui); } }
            Font ChipFont { get { return Ink.Text(13f * Ui); } }
            Font ButtonFont { get { return Ink.Text(12.5f * Ui); } }
            float MaxScroll { get { return Math.Max(0, contentH - Height); } }

            RectangleF On(RectangleF r) { return new RectangleF(r.X, r.Y - scroll, r.Width, r.Height); }

            // Layout

            void Arrange()
            {
                items.Clear();
                taps.Clear();
                laidW = Width;
                laidH = Height;
                if (Empty) LayoutEmpty();
                else LayoutChat();
                LayoutBanner();
            }

            void LayoutChat()
            {
                float s = Ui, w = Width, pad = 16 * s, lead = pad + 38 * s;
                float maxW = Math.Max(140 * s, Math.Min(w * 0.82f, w - lead - pad - 36 * s));
                float y = 10 * s;
                ChatMsg prev = null;
                foreach (var m in Chat)
                {
                    // A stamp when the day turns or after a quiet half hour
                    if (prev == null || m.Utc.ToLocalTime().Date != prev.Utc.ToLocalTime().Date || (m.Utc - prev.Utc).TotalMinutes > 30)
                    {
                        if (prev != null) y += 8 * s;
                        items.Add(new Item { Stamp = StampText(m.Utc), Box = new RectangleF(0, y, w, 26 * s) });
                        y += 30 * s;
                    }
                    else y += (prev.Mine == m.Mine ? 6 : 14) * s;
                    int at = items.Count;
                    var it = new Item { Msg = m };
                    float top = y;
                    var msg = m;
                    if (m.Text.Length > 0)
                    {
                        float inset = m.Fix != null ? 22 * s : 0;
                        var size = TextRenderer.MeasureText(m.Text, MsgFont, new Size((int)(maxW - 28 * s - inset), int.MaxValue), Wrap);
                        float bw = size.Width + 28 * s + inset, bh = size.Height + 18 * s;
                        it.Bubble = new RectangleF(m.Mine ? w - pad - bw : lead, y, bw, bh);
                        float cy = y + Math.Min(bh, 38 * s) / 2;
                        var copy = new RectangleF(m.Mine ? it.Bubble.X - 34 * s : it.Bubble.Right + 6 * s, cy - 14 * s, 28 * s, 28 * s);
                        taps.Add(new Tap { Id = "copy" + at, Glyph = IcoCopy, Rect = copy, Item = at, Act = delegate { Copy(msg); } });
                        y += bh;
                    }
                    for (int k = 0; k < m.Acts.Count; k++)
                    {
                        var a = m.Acts[k];
                        if (y > top) y += 6 * s;
                        float tw = Math.Min(maxW - 64 * s, TextRenderer.MeasureText(a.Text, ChipFont, Size.Empty, Single).Width);
                        var chip = new RectangleF(0, y, tw + 54 * s, 32 * s);
                        chip.X = m.Mine ? w - pad - chip.Width : lead;
                        it.Chips.Add(chip);
                        if (island.CanUndo(a))
                        {
                            var act = a;
                            string label = Lang.T("Batalkan");
                            float uw = TextRenderer.MeasureText(label, ButtonFont).Width + 22 * s;
                            taps.Add(new Tap { Id = "undo" + at + "." + k, Label = label, Rect = new RectangleF(chip.Right + 6 * s, y + 3 * s, uw, 26 * s), Act = delegate { island.UndoAct(act); } });
                        }
                        y += 32 * s;
                    }
                    if (m.Fix != null)
                    {
                        y += 8 * s;
                        float x = lead;
                        if (m.Fix == "retry") x = AddButton("retry" + at, Lang.T("Coba lagi"), x, y, delegate { island.RetrySuruh(msg); }) + 6 * s;
                        AddButton("ai" + at, Lang.T("Atur AI"), x, y, delegate { island.OpenPrefs("ai"); });
                        y += 28 * s;
                    }
                    if (!m.Mine) it.Face = new RectangleF(pad, top, 28 * s, 28 * s);
                    it.Box = new RectangleF(0, top, w, y - top);
                    items.Add(it);
                    prev = m;
                }
                if (island.suruhBusy)
                {
                    y += 14 * s;
                    items.Add(new Item { Typing = true, Bubble = new RectangleF(lead, y, 62 * s, 38 * s), Face = new RectangleF(pad, y, 28 * s, 28 * s), Box = new RectangleF(0, y, w, 38 * s) });
                    y += 38 * s;
                }
                contentH = y + 18 * s + (island.chatCleared != null ? 60 * s : 0);
            }

            // Pill button; returns its right edge
            float AddButton(string id, string label, float x, float y, Action act)
            {
                float bw = TextRenderer.MeasureText(label, ButtonFont).Width + 24 * Ui;
                taps.Add(new Tap { Id = id, Label = label, Rect = new RectangleF(x, y, bw, 28 * Ui), Act = act });
                return x + bw;
            }

            // Empty: a face, what Deskmate can do, ideas to try
            void LayoutEmpty()
            {
                float s = Ui, w = Width, cx = w / 2;
                bool ai = Ai.Ready(Ai.Text);
                float y = Math.Max(20 * s, Height * 0.12f);
                emptyFace = new RectangleF(cx - 30 * s, y, 60 * s, 60 * s);
                y += 76 * s;
                titleRect = new RectangleF(16 * s, y, w - 32 * s, 26 * s);
                y += 32 * s;
                emptyNote = ai ? Lang.T("Tanya apa saja, atau suruh pasang pengingat, timer, dan catatan.") : Lang.T("Tanpa layanan AI, Deskmate paham timer, pengingat, catatan, dan hitungan.");
                float nw = Math.Min(w - 48 * s, 340 * s);
                var ns = TextRenderer.MeasureText(emptyNote, ChipFont, new Size((int)nw, int.MaxValue), Wrap | TextFormatFlags.HorizontalCenter);
                noteRect = new RectangleF(cx - nw / 2, y, nw, ns.Height + 2);
                y += ns.Height + 20 * s;
                if (!ai)
                {
                    string label = Lang.T("Sambungkan AI");
                    float bw = TextRenderer.MeasureText(label, ButtonFont).Width + 28 * s;
                    taps.Add(new Tap { Id = "connect", Label = label, Rect = new RectangleF(cx - bw / 2, y, bw, 30 * s), Act = delegate { island.OpenPrefs("ai"); } });
                    y += 48 * s;
                }
                // Ideas wrap into centred rows
                var row = new List<Tap>();
                float rowW = 0, gap = 8 * s, avail = w - 32 * s;
                for (int i = 0; i < Ideas.Length; i++)
                {
                    string label = Lang.T(Ideas[i][0]);
                    float iw = TextRenderer.MeasureText(label, ChipFont, Size.Empty, Single).Width + 44 * s;
                    if (row.Count > 0 && rowW + gap + iw > avail) { y = PlaceRow(row, rowW, y, cx); row.Clear(); rowW = 0; }
                    rowW += (row.Count > 0 ? gap : 0) + iw;
                    var tap = new Tap { Id = "idea" + i, Label = label, Glyph = Ideas[i][1][0], Rect = new RectangleF(0, 0, iw, 34 * s) };
                    tap.Act = delegate { win.Suggest(tap.Label); };
                    row.Add(tap);
                    taps.Add(tap);
                }
                if (row.Count > 0) y = PlaceRow(row, rowW, y, cx);
                contentH = y + 16 * s;
            }

            float PlaceRow(List<Tap> row, float rowW, float y, float cx)
            {
                float x = cx - rowW / 2;
                foreach (var t in row)
                {
                    t.Rect = new RectangleF(x, y, t.Rect.Width, t.Rect.Height);
                    x += t.Rect.Width + 8 * Ui;
                }
                return y + 42 * Ui;
            }

            RectangleF BannerRect() { float s = Ui; return new RectangleF(16 * s, Height - 58 * s, Width - 32 * s, 46 * s); }

            void LayoutBanner()
            {
                if (island.chatCleared == null) return;
                float s = Ui;
                var bar = BannerRect();
                string label = Lang.T("Urungkan");
                float bw = TextRenderer.MeasureText(label, ButtonFont).Width + 24 * s;
                taps.Add(new Tap { Id = "restore", Label = label, Fixed = true, Rect = new RectangleF(bar.Right - 9 * s - bw, bar.Y + (bar.Height - 28 * s) / 2, bw, 28 * s), Act = delegate { undoEnd.Stop(); island.RestoreChat(); } });
            }

            static string StampText(DateTime utc)
            {
                var t = utc.ToLocalTime();
                string day = t.Date == DateTime.Today ? Lang.T("Hari ini") : t.Date == DateTime.Today.AddDays(-1) ? Lang.T("Kemarin")
                    : t.ToString("d MMM", Lang.English ? CultureInfo.InvariantCulture : new CultureInfo("id-ID"));
                return day + " " + Fmt.Hm(t);
            }

            // Updates from the window

            public void Reflow(ChatMsg added)
            {
                bool stick = pinned || island.suruhBusy || (added != null && added.Mine);
                Arrange();
                ScrollTo(stick ? MaxScroll : target, added != null);
                if (focus >= items.Count || (focus >= 0 && items[focus].Msg == null)) focus = -1;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
            }

            public void Relayout()
            {
                bool end = pinned;
                Arrange();
                ScrollTo(end ? MaxScroll : target, false);
            }

            public void ToEnd() { if (laidW != Width) Arrange(); ScrollTo(MaxScroll, false); }

            public void Clear()
            {
                island.ClearChat();
                focus = -1;
                undoEnd.Stop();
                undoEnd.Start();
                Reflow(null);
            }

            public void Pulse()
            {
                foreach (var it in items)
                {
                    if (!it.Typing) continue;
                    var r = On(RectangleF.Union(it.Face, it.Bubble));
                    r.Inflate(4, 4);
                    Invalidate(Rectangle.Round(r));
                }
            }

            protected override void OnResize(EventArgs e)
            {
                bool end = pinned;
                base.OnResize(e);
                if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
                Arrange();
                ScrollTo(end ? MaxScroll : target, false);
            }

            void ScrollTo(float v, bool smooth)
            {
                target = Math.Max(0, Math.Min(MaxScroll, v));
                pinned = target >= MaxScroll - 4 * Ui;
                if (!smooth || Anim.Reduced || !Visible) { scroll = target; glide.Stop(); Invalidate(); return; }
                if (!glide.Enabled) glide.Start();
            }

            void Reveal(RectangleF box)
            {
                float s = Ui;
                if (box.Top < target + 8 * s) ScrollTo(box.Top - 8 * s, true);
                else if (box.Bottom > target + Height - 8 * s) ScrollTo(box.Bottom - Height + 8 * s, true);
            }

            // Painting

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Window);
                if (laidW != Width || laidH != Height) Arrange();
                if (Empty) DrawEmpty(g);
                else
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it.Box.Bottom - scroll < -40 * Ui || it.Box.Top - scroll > Height + 40 * Ui) continue;
                        DrawItem(g, it, i);
                    }
                foreach (var t in taps) if (!t.Fixed && Shown(t)) DrawTap(g, t);
                DrawThumb(g);
                DrawBanner(g);
            }

            void DrawEmpty(Graphics g)
            {
                float s = Ui;
                DrawFace(g, On(emptyFace), 0);
                KitInk.Draw(g, Lang.T("Mau Deskmate bantu apa?"), Ink.Semi(17f * s), KitInk.Text, On(titleRect), ContentAlignment.MiddleCenter);
                TextRenderer.DrawText(g, emptyNote, ChipFont, Rectangle.Round(On(noteRect)), KitInk.Sub, Wrap | TextFormatFlags.HorizontalCenter);
            }

            void DrawItem(Graphics g, Item it, int i)
            {
                float s = Ui;
                if (it.Stamp != null) { KitInk.Draw(g, it.Stamp, Ink.Small(11.5f * s), KitInk.Sub, On(it.Box), ContentAlignment.MiddleCenter); return; }
                if (!it.Face.IsEmpty) DrawFace(g, On(it.Face), it.Typing && !Anim.Reduced ? (float)Math.Sin(win.Phase * 0.45f) : 0);
                if (it.Typing) { DrawTyping(g, On(it.Bubble)); return; }
                var m = it.Msg;
                if (!it.Bubble.IsEmpty)
                {
                    var b = On(it.Bubble);
                    float r = Math.Min(17 * s, b.Height / 2), tail = 6 * s;
                    // The corner nearest the speaker stays sharp
                    using (var path = m.Mine ? Corners(b, r, r, tail, r) : Corners(b, tail, r, r, r))
                    using (var br = new SolidBrush(m.Mine ? KitInk.Accent : DeskmateFill)) g.FillPath(br, path);
                    float inset = m.Fix != null ? 22 * s : 0;
                    if (m.Fix != null) KitInk.Draw(g, Ink.Str(IcoFail), Ink.Icons(13f * s), KitInk.Sub, new RectangleF(b.X + 11 * s, b.Y, 20 * s, Math.Min(b.Height, 38 * s)), ContentAlignment.MiddleCenter);
                    var tr = new RectangleF(b.X + 14 * s + inset, b.Y + 9 * s, b.Width - 28 * s - inset + 2, b.Height - 18 * s + 2);
                    TextRenderer.DrawText(g, m.Text, MsgFont, Rectangle.Round(tr), m.Mine ? KitInk.OnAccent : KitInk.Text, Wrap);
                }
                for (int k = 0; k < it.Chips.Count && k < m.Acts.Count; k++) DrawChip(g, On(it.Chips[k]), m.Acts[k]);
                if (i == focus && Focused && ShowFocusCues && !KitInk.ByMouse)
                {
                    var first = On(!it.Bubble.IsEmpty ? it.Bubble : it.Chips.Count > 0 ? it.Chips[0] : it.Box);
                    first.Inflate(3 * s, 3 * s);
                    using (var path = Ink.Round(first, Math.Min(20 * s, first.Height / 2)))
                    using (var pen = new Pen(KitInk.Text, 2f)) g.DrawPath(pen, path);
                }
            }

            void DrawChip(Graphics g, RectangleF c, ChatAct a)
            {
                float s = Ui;
                using (var path = Ink.Round(c, c.Height / 2))
                using (var pen = new Pen(KitInk.Line, Math.Max(1f, 1.2f * s))) g.DrawPath(pen, path);
                Color ink = a.Undone ? KitInk.Sub : KitInk.Text;
                KitInk.Draw(g, Ink.Str(a.Glyph), Ink.Icons(14f * s), ink, new RectangleF(c.X + 10 * s, c.Y, 22 * s, c.Height), ContentAlignment.MiddleCenter);
                var tr = new RectangleF(c.X + 38 * s, c.Y, c.Width - 48 * s, c.Height);
                KitInk.Draw(g, a.Text, ChipFont, ink, tr, ContentAlignment.MiddleLeft);
                // Cancelled: struck through, not only greyed
                if (!a.Undone) return;
                float tw = Math.Min(tr.Width, TextRenderer.MeasureText(a.Text, ChipFont, Size.Empty, Single).Width);
                using (var pen = new Pen(ink, Math.Max(1f, s))) g.DrawLine(pen, tr.X + 3 * s, c.Y + c.Height / 2, tr.X + tw - 3 * s, c.Y + c.Height / 2);
            }

            void DrawTyping(Graphics g, RectangleF b)
            {
                float s = Ui, r = Math.Min(17 * s, b.Height / 2);
                var fill = DeskmateFill;
                using (var path = Corners(b, 6 * s, r, r, r)) using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                for (int k = 0; k < 3; k++)
                {
                    float a = Anim.Reduced ? 0.6f : 0.3f + 0.7f * Math.Max(0, (float)Math.Sin(win.Phase - k * 0.8f));
                    float cx = b.X + b.Width / 2 + (k - 1) * 11 * s, cy = b.Y + b.Height / 2;
                    using (var br = new SolidBrush(Ink.Mix(fill, KitInk.Text, a))) g.FillEllipse(br, cx - 3.5f * s, cy - 3.5f * s, 7 * s, 7 * s);
                }
            }

            // Copy shows over its message, or its keyboard focus
            bool Shown(Tap t)
            {
                if (t.Item < 0) return true;
                return t.Item == hoverItem || (t.Item == focus && Focused) || (copied != null && t.Item < items.Count && items[t.Item].Msg == copied);
            }

            void DrawTap(Graphics g, Tap t)
            {
                float s = Ui;
                var r = t.Fixed ? t.Rect : On(t.Rect);
                bool over = hot == t.Id, down = over && pressed == t.Id;
                if (t.Item >= 0)
                {
                    bool done = copied != null && t.Item < items.Count && items[t.Item].Msg == copied;
                    if (over) using (var b = new SolidBrush(down ? KitInk.Line : KitInk.Hover)) g.FillEllipse(b, r);
                    KitInk.Draw(g, Ink.Str(done ? IcoDone : t.Glyph), Ink.Icons(13f * s), over || done ? KitInk.Text : KitInk.Sub, r, ContentAlignment.MiddleCenter);
                    return;
                }
                Color fill = down ? KitInk.Line : over ? KitInk.Hover : t.Glyph != '\0' ? KitInk.Window : KitInk.Group;
                using (var path = Ink.Round(r, r.Height / 2))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var pen = new Pen(KitInk.Line, Math.Max(1f, 1.2f * s))) g.DrawPath(pen, path);
                }
                if (t.Glyph == '\0') { KitInk.Draw(g, t.Label, ButtonFont, KitInk.Text, r, ContentAlignment.MiddleCenter); return; }
                KitInk.Draw(g, Ink.Str(t.Glyph), Ink.Icons(13f * s), KitInk.Sub, new RectangleF(r.X + 10 * s, r.Y, 20 * s, r.Height), ContentAlignment.MiddleCenter);
                KitInk.Draw(g, t.Label, ChipFont, KitInk.Text, new RectangleF(r.X + 32 * s, r.Y, r.Width - 38 * s, r.Height), ContentAlignment.MiddleLeft);
            }

            RectangleF Thumb()
            {
                float s = Ui;
                if (contentH <= Height + 1) return RectangleF.Empty;
                float h = Math.Max(30 * s, Height * Height / contentH);
                float y = (Height - h) * scroll / Math.Max(1, contentH - Height);
                float wide = hot == "thumb" || grab >= 0 ? 7 * s : 4 * s;
                return new RectangleF(Width - wide - 3 * s, y, wide, h);
            }

            void DrawThumb(Graphics g)
            {
                var t = Thumb();
                if (t.IsEmpty) return;
                using (var path = Ink.Round(t, t.Width / 2))
                using (var b = new SolidBrush(hot == "thumb" || grab >= 0 ? KitInk.Sub : KitInk.Faint)) g.FillPath(b, path);
            }

            void DrawBanner(Graphics g)
            {
                if (island.chatCleared == null) return;
                float s = Ui;
                var bar = BannerRect();
                using (var path = Ink.Round(bar, 14 * s))
                {
                    using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                    using (var pen = new Pen(KitInk.Line, 1f)) g.DrawPath(pen, path);
                }
                KitInk.Draw(g, Lang.T("Obrolan lama dihapus"), ChipFont, KitInk.Text, new RectangleF(bar.X + 16 * s, bar.Y, bar.Width - 140 * s, bar.Height), ContentAlignment.MiddleLeft);
                foreach (var t in taps) if (t.Fixed) DrawTap(g, t);
            }

            // Pointer

            Tap TapAt(Point pt)
            {
                bool underBanner = island.chatCleared != null && BannerRect().Contains(pt);
                for (int i = taps.Count - 1; i >= 0; i--)
                {
                    var t = taps[i];
                    if (!(t.Fixed ? t.Rect : On(t.Rect)).Contains(pt)) continue;
                    if (t.Fixed) return t;
                    if (underBanner || !Shown(t) && t.Item != ItemAt(pt)) continue;
                    return t;
                }
                return null;
            }

            int ItemAt(Point pt)
            {
                for (int i = 0; i < items.Count; i++) if (items[i].Msg != null && On(items[i].Box).Contains(pt)) return i;
                return -1;
            }

            bool InStrip(Point pt) { return pt.X >= Width - 14 * Ui && !Thumb().IsEmpty; }

            void Track(Point pt)
            {
                var t = TapAt(pt);
                string id = t != null ? t.Id : InStrip(pt) ? "thumb" : null;
                int item = ItemAt(pt);
                if (id == hot && item == hoverItem) return;
                hot = id;
                hoverItem = item;
                Cursor = t != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (grab >= 0) { DragTo(e.Y); return; }
                Track(e.Location);
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = null; hoverItem = -1; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (InStrip(e.Location))
                {
                    var th = Thumb();
                    // Off the thumb: centre it there first
                    if (!th.Contains(new PointF(th.X + th.Width / 2, e.Y))) { grab = th.Height / 2; DragTo(e.Y); }
                    grab = e.Y - Thumb().Y;
                    Capture = true;
                    Invalidate();
                    return;
                }
                var t = TapAt(e.Location);
                pressed = t != null ? t.Id : null;
                Invalidate();
            }

            void DragTo(int y)
            {
                var th = Thumb();
                float room = Height - th.Height;
                if (room > 0) ScrollTo((y - grab) / room * MaxScroll, false);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (grab >= 0) { grab = -1; Capture = false; Invalidate(); return; }
                if (e.Button == MouseButtons.Right)
                {
                    int item = ItemAt(e.Location);
                    if (item >= 0) { focus = item; Invalidate(); Menu(items[item].Msg, e.Location); }
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                var t = TapAt(e.Location);
                bool hit = t != null && t.Id == pressed;
                pressed = null;
                // Typing carries on in the box
                win.FocusInput();
                if (hit) t.Act();
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                ScrollTo(target - e.Delta / 120f * 64 * Ui, true);
            }

            // Keyboard: arrows walk messages, menu holds the rest

            protected override bool IsInputKey(Keys k)
            {
                switch (k & Keys.KeyCode)
                {
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Home:
                    case Keys.End:
                    case Keys.PageUp:
                    case Keys.PageDown:
                    case Keys.Enter:
                        return true;
                }
                return base.IsInputKey(k);
            }

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);
                if (focus < 0) focus = Next(items.Count, -1);
                // A click focuses too; only Tab jumps to a message
                if (focus >= 0 && !KitInk.ByMouse && Control.MouseButtons == MouseButtons.None) FocusItem(focus);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                var msg = focus >= 0 && focus < items.Count ? items[focus].Msg : null;
                switch (e.KeyCode)
                {
                    case Keys.Up: FocusItem(Next(focus < 0 ? items.Count : focus, -1)); break;
                    case Keys.Down: FocusItem(Next(focus, 1)); break;
                    case Keys.Home: FocusItem(Next(-1, 1)); break;
                    case Keys.End: FocusItem(Next(items.Count, -1)); break;
                    case Keys.PageUp: ScrollTo(target - Height * 0.85f, true); break;
                    case Keys.PageDown: ScrollTo(target + Height * 0.85f, true); break;
                    case Keys.Enter:
                    case Keys.Apps:
                        if (msg == null) return;
                        Menu(msg, MenuSpot());
                        break;
                    case Keys.F10:
                        if (!e.Shift || msg == null) return;
                        Menu(msg, MenuSpot());
                        break;
                    case Keys.C:
                        if (!e.Control || msg == null) return;
                        Copy(msg);
                        break;
                    default: return;
                }
                e.Handled = true;
            }

            int Next(int from, int dir)
            {
                for (int i = from + dir; i >= 0 && i < items.Count; i += dir) if (items[i].Msg != null) return i;
                return -1;
            }

            void FocusItem(int i)
            {
                if (i < 0) return;
                focus = i;
                Reveal(items[i].Box);
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Focus, Child(i));
                AccessibilityNotifyClients(AccessibleEvents.Selection, Child(i));
            }

            Point MenuSpot()
            {
                if (focus < 0) return new Point(Width / 2, Height / 2);
                var it = items[focus];
                var r = On(!it.Bubble.IsEmpty ? it.Bubble : it.Box);
                return new Point((int)(r.X + 12 * Ui), (int)Math.Max(0, Math.Min(Height - 4, r.Bottom)));
            }

            void Menu(ChatMsg m, Point at)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add(Lang.T("Salin"), null, delegate { Copy(m); });
                if (m.Mine) menu.Items.Add(Lang.T("Pakai lagi"), null, delegate { win.Suggest(m.Text); });
                if (m.Fix == "retry") menu.Items.Add(Lang.T("Coba lagi"), null, delegate { island.RetrySuruh(m); });
                foreach (var a in m.Acts)
                {
                    if (!island.CanUndo(a)) continue;
                    var act = a;
                    menu.Items.Add(Lang.T("Batalkan pengingat"), null, delegate { island.UndoAct(act); });
                }
                MenuLook.Apply(menu);
                menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
                menu.Show(this, at);
            }

            void Copy(ChatMsg m)
            {
                var sb = new StringBuilder(m.Text);
                foreach (var a in m.Acts) sb.Append(sb.Length > 0 ? Environment.NewLine : "").Append(a.Text);
                if (sb.Length == 0) return;
                try { Clipboard.SetText(sb.ToString()); }
                catch (Exception) { return; }
                copied = m;
                tick.Stop();
                tick.Start();
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { glide.Dispose(); tick.Dispose(); undoEnd.Dispose(); }
                base.Dispose(disposing);
            }

            // Readers: one list item per message

            static string Spoken(ChatMsg m)
            {
                var parts = new List<string>();
                if (m.Text.Length > 0) parts.Add(m.Text);
                foreach (var a in m.Acts) parts.Add(a.Undone ? a.Text + " (" + Lang.T("dibatalkan") + ")" : a.Text);
                return (m.Mine ? Lang.T("Kamu") : "Deskmate") + ": " + string.Join(". ", parts.ToArray());
            }

            int Child(int item)
            {
                int n = -1;
                for (int i = 0; i <= item && i < items.Count; i++) if (items[i].Msg != null) n++;
                return n;
            }

            int ItemOfChild(int child)
            {
                int n = -1;
                for (int i = 0; i < items.Count; i++) if (items[i].Msg != null && ++n == child) return i;
                return -1;
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new Access(this); }

            class Access : ControlAccessibleObject
            {
                readonly ChatList owner;
                public Access(ChatList owner) : base(owner) { this.owner = owner; }
                public override AccessibleRole Role { get { return AccessibleRole.List; } }
                public override int GetChildCount() { return owner.Child(owner.items.Count - 1) + 1; }
                public override AccessibleObject GetChild(int i) { int it = owner.ItemOfChild(i); return it < 0 ? null : new Entry(owner, it); }
                public override AccessibleObject GetFocused() { return owner.Focused && owner.focus >= 0 ? new Entry(owner, owner.focus) : null; }
                public override AccessibleObject GetSelected() { return GetFocused(); }
            }

            class Entry : AccessibleObject
            {
                readonly ChatList owner;
                readonly int item;
                public Entry(ChatList owner, int item) { this.owner = owner; this.item = item; }
                ChatMsg Msg { get { return item < owner.items.Count ? owner.items[item].Msg : null; } }
                public override string Name { get { var m = Msg; return m == null ? "" : Spoken(m); } }
                public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
                public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
                public override AccessibleStates State
                {
                    get { return AccessibleStates.Focusable | AccessibleStates.Selectable | (owner.Focused && owner.focus == item ? AccessibleStates.Focused | AccessibleStates.Selected : 0); }
                }
                public override Rectangle Bounds
                {
                    get { return item < owner.items.Count ? owner.RectangleToScreen(Rectangle.Round(owner.On(owner.items[item].Box))) : Rectangle.Empty; }
                }
                public override string DefaultAction { get { return Lang.T("Salin"); } }
                public override void DoDefaultAction() { var m = Msg; if (m != null) owner.Copy(m); }
            }
        }
    }
}
