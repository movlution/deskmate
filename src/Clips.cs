using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Clipboard history lives in memory.
    partial class Island
    {
        class ClipItem
        {
            public string Text;
            public Bitmap Image, Thumb;
            public long Id;
            public DateTime Utc;
            public long ImageBytes { get { return Image == null ? 0 : (long)Image.Width * Image.Height * 4; } }
        }

        const int ClipMax = 25, ClipChars = 100000;
        const int ClipImageMax = 8, ClipImageLimit = 40 * 1024 * 1024, ClipImageBudget = 64 * 1024 * 1024;
        const float ClipCardW = 148, ClipCardH = 92, ClipPitch = 160, ClipFilterH = 34, ClipGalleryH = 252;
        readonly Spring clipLift = new Spring(0, 430, 32, 0.01f);
        long liftedClip;
        readonly List<ClipItem> clips = new List<ClipItem>(), imageClips = new List<ClipItem>();
        bool showImageClips;
        long clipId;
        uint imageClipSequence;
        List<ClipItem> VisibleClips { get { return showImageClips ? imageClips : clips; } }
        readonly Spring clipScroll = new Spring(0, 300, 34, 0.2f);
        readonly System.Windows.Forms.Timer clipRetry = new System.Windows.Forms.Timer { Interval = 120 };
        bool clipListening;

        [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int memcmp(IntPtr left, IntPtr right, UIntPtr count);

        bool ClipsOn { get { return settings.GetBool("clipboard", false); } }

        void ApplyClips()
        {
            bool want = ClipsOn || ShotsOn || ScreenTextArmed;
            if (!ClipsOn)
            {
                ClearClips(clips);
                ClearClips(imageClips);
                clipScroll.Snap(0);
            }
            if (want == clipListening) return;
            if (want) clipListening = AddClipboardFormatListener(Handle);
            else { RemoveClipboardFormatListener(Handle); clipListening = false; clipRetry.Stop(); }
            clipRetry.Tick -= ClipRetryTick;
            clipRetry.Tick += ClipRetryTick;
        }

        void ClipRetryTick(object sender, EventArgs e) { clipRetry.Stop(); if (clipListening) ReadClipboard(false); }

        // WM_CLIPBOARDUPDATE
        void OnClipboardUpdate()
        {
            if (!clipListening) return;
            ReadClipboard(true);
            if (expanded && page == PageShelf) ShelfClipCheck();
        }

        void ReadClipboard(bool retry)
        {
            // Deskmate putting the old clipboard back
            if (DateTime.UtcNow < clipQuietUtc) return;
            try
            {
                uint sequence = GetClipboardSequenceNumber();
                var data = System.Windows.Forms.Clipboard.GetDataObject();
                if (data != null) ReadClipboardData(data, sequence);
            }
            catch (ExternalException)
            {
                // Another app holds the clipboard
                if (retry) clipRetry.Start();
            }
        }

        void ReadClipboardData(IDataObject data, uint sequence)
        {
            // Password managers flag secrets this way.
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return;
            if (data.GetDataPresent("CanIncludeInClipboardHistory"))
            {
                var flag = data.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                if (flag != null && flag.Length >= 4 && BitConverter.ToInt32(flag.ToArray(), 0) == 0) return;
            }
            bool hasText = data.GetDataPresent(DataFormats.UnicodeText);
            bool hasBitmap = data.GetDataPresent(DataFormats.Bitmap);
            if (hasBitmap && !hasText && TakeScreenText(data)) return;
            if ((sequence == 0 || sequence != imageClipSequence) && (ClipsOn || (!hasText && ShotsOn)))
            {
                if (hasBitmap)
                {
                    using (var bitmap = data.GetData(DataFormats.Bitmap, true) as Bitmap)
                        if (bitmap != null)
                        {
                            if (ClipsOn) RecordImageClip(bitmap, sequence);
                            if (!hasText && ShotsOn) OnClipImage(bitmap);
                        }
                }
                else if (ClipsOn && data.GetDataPresent("PNG", false))
                {
                    object png = data.GetData("PNG", false);
                    var bytes = png as byte[];
                    using (var stream = bytes != null ? new MemoryStream(bytes) : png as MemoryStream)
                    {
                        if (stream != null && AllowedClipPng(stream))
                        {
                            stream.Position = 0;
                            try { using (var bitmap = new Bitmap(stream)) RecordImageClip(bitmap, sequence); }
                            catch (ArgumentException) { } // Clipboard data can be malformed.
                        }
                    }
                }
            }
            if (!ClipsOn || !hasText) return;
            string text = data.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > ClipChars) text = text.Substring(0, ClipChars);
            clips.RemoveAll(c => c.Text == text);
            clips.Insert(0, new ClipItem { Id = ++clipId, Text = text, Utc = DateTime.UtcNow });
            if (clips.Count > ClipMax) clips.RemoveRange(ClipMax, clips.Count - ClipMax);
            if (expanded && (page == PageHome || (page == PageShelf && shelfSeg == 1 && !showImageClips))) Kick();
        }

        static bool AllowedClipPng(MemoryStream stream)
        {
            if (stream.Length < 24 || stream.Length > ClipImageLimit) return false;
            stream.Position = 0;
            var header = new byte[24];
            if (stream.Read(header, 0, header.Length) != header.Length ||
                header[0] != 137 || header[1] != 'P' || header[2] != 'N' || header[3] != 'G' ||
                header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R')
                return false;
            long width = (uint)(header[16] << 24 | header[17] << 16 | header[18] << 8 | header[19]);
            long height = (uint)(header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23]);
            return width > 0 && height > 0 && width <= ClipImageLimit / 4 / height;
        }

        void RecordImageClip(Bitmap bitmap, uint sequence)
        {
            long bytes = (long)bitmap.Width * bitmap.Height * 4;
            if (bytes > ClipImageLimit) return;
            imageClipSequence = sequence;
            int duplicate = imageClips.FindIndex(item => SameClipImage(item.Image, bitmap));
            if (duplicate >= 0)
            {
                var item = imageClips[duplicate];
                imageClips.RemoveAt(duplicate);
                item.Id = ++clipId;
                item.Utc = DateTime.UtcNow;
                imageClips.Insert(0, item);
                RefreshImageClips();
                return;
            }
            var image = OwnCopy(bitmap);
            Bitmap thumb;
            try { thumb = ShotThumb(image, 1.5f); }
            catch { image.Dispose(); throw; }
            long used = bytes;
            foreach (var item in imageClips) used += item.ImageBytes;
            while (imageClips.Count >= ClipImageMax || used > ClipImageBudget)
            {
                int last = imageClips.Count - 1;
                used -= imageClips[last].ImageBytes;
                RemoveClip(imageClips, last);
            }
            imageClips.Insert(0, new ClipItem { Id = ++clipId, Image = image, Thumb = thumb, Utc = DateTime.UtcNow });
            RefreshImageClips();
        }

        static bool SameClipImage(Bitmap left, Bitmap right)
        {
            if (left.Width != right.Width || left.Height != right.Height) return false;
            var bounds = new Rectangle(0, 0, right.Width, right.Height);
            BitmapData a = null, b = null;
            try
            {
                a = left.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                b = right.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int rowBytes = bounds.Width * 4;
                if (a.Stride == rowBytes && b.Stride == rowBytes)
                    return memcmp(a.Scan0, b.Scan0, (UIntPtr)(rowBytes * bounds.Height)) == 0;
                for (int y = 0; y < bounds.Height; y++)
                    if (memcmp(IntPtr.Add(a.Scan0, y * a.Stride), IntPtr.Add(b.Scan0, y * b.Stride), (UIntPtr)rowBytes) != 0)
                        return false;
                return true;
            }
            catch (ArgumentException) { return false; }
            finally
            {
                if (b != null) right.UnlockBits(b);
                if (a != null) left.UnlockBits(a);
            }
        }

        void RefreshImageClips()
        {
            if (expanded && (page == PageHome || (page == PageShelf && shelfSeg == 1 && showImageClips))) Kick();
        }

        void RemoveClip(List<ClipItem> history, int index)
        {
            if (index < 0) return;
            var item = history[index];
            history.RemoveAt(index);
            if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
            if (item.Thumb != null) { item.Thumb.Dispose(); item.Thumb = null; }
        }

        void ClearClips(List<ClipItem> history)
        {
            while (history.Count > 0) RemoveClip(history, history.Count - 1);
        }

        ClipItem LatestClip()
        {
            if (clips.Count == 0) return imageClips.Count > 0 ? imageClips[0] : null;
            if (imageClips.Count > 0 && imageClips[0].Id > clips[0].Id) return imageClips[0];
            return clips[0];
        }

        static string ClipImageSize(ClipItem item)
        {
            return item.Image.Width + " × " + item.Image.Height + " px";
        }

        void CopyClip(ClipItem c)
        {
            try
            {
                if (c.Image != null)
                {
                    System.Windows.Forms.Clipboard.SetImage(c.Image);
                    imageClipSequence = GetClipboardSequenceNumber();
                }
                else if (c.Text != null) System.Windows.Forms.Clipboard.SetText(c.Text);
                else return;
            }
            catch (ExternalException ex) { Log.Write("clipboard set: " + ex.Message); return; }
            ShowInfo('', Lang.T("Disalin lagi"), c.Image != null ? Lang.T("Gambar") : Preview(c.Text, 60), true);
        }

        static string Preview(string text, int max)
        {
            var sb = new System.Text.StringBuilder();
            bool space = false;
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(ch);
                if (sb.Length >= max) break;
            }
            return sb.ToString();
        }

        float ClipScrollMax(float w)
        {
            return Math.Max(0, VisibleClips.Count * ClipPitch - (ClipPitch - ClipCardW) + 8 - (w - 20));
        }

        bool ScrollClipCards(int step)
        {
            float max = ClipScrollMax(PanelW);
            if (max <= 0) return false;
            float target = Clamp(clipScroll.Target + step * ClipPitch, 0, max);
            if (Anim.Reduced) clipScroll.Snap(target);
            else clipScroll.Target = target;
            Kick();
            return true;
        }

        void ChooseClipKind(bool images)
        {
            if (showImageClips == images) return;
            showImageClips = images;
            clipScroll.Snap(0);
            liftedClip = 0;
            clipLift.Snap(0);
            Kick();
        }

        void DrawClipsPage(float w, float h, DateTime now)
        {
            if (!ClipsOn)
            {
                EmptyState(new RectangleF(0, PanelTop, w, h - PanelTop), '', Lang.T("Riwayat clipboard mati"), Lang.T("Riwayat teks dan gambar hanya disimpan di memori"), Lang.T("Nyalakan"), "clipsOn", delegate { SetFlag("clipboard", true); }, PillKind.Primary);
                return;
            }
            var list = VisibleClips;
            float top = PanelTop + ClipFilterH;
            Pill(new RectangleF(20, PanelTop, 72, 26), "historyText", Lang.T("Teks"),
                showImageClips ? PillKind.Secondary : PillKind.Primary, delegate { ChooseClipKind(false); });
            Pill(new RectangleF(98, PanelTop, 84, 26), "historyImage", Lang.T("Gambar"),
                showImageClips ? PillKind.Primary : PillKind.Secondary, delegate { ChooseClipKind(true); });
            if (list.Count == 0)
            {
                EmptyState(new RectangleF(0, top, w, h - top), '', Lang.T("Belum ada yang disalin"),
                    Lang.T(showImageClips ? "Gambar yang kamu salin muncul di sini" : "Teks yang kamu salin muncul di sini"));
                return;
            }
            float max = ClipScrollMax(w);
            if (clipScroll.Target > max) clipScroll.Target = max;
            if (max > 0)
            {
                Pill(new RectangleF(w - 92, PanelTop, 32, 26), "historyPrev", "‹", PillKind.Secondary,
                    delegate { ScrollClipCards(-1); }, clipScroll.Target > 0.5f);
                Pill(new RectangleF(w - 54, PanelTop, 32, 26), "historyNext", "›", PillKind.Secondary,
                    delegate { ScrollClipCards(1); }, clipScroll.Target < max - 0.5f);
            }

            var area = new RectangleF(10, top + 4, w - 20, h - top - 14);
            if (area.Height <= 0) return;
            var st = g.Save();
            g.IntersectClip(area);
            float cableY = area.Y + 8;
            g.DrawLine(p.P(Ink.A(p.T.Text, 0.36f), 1.4f), area.Left, cableY, area.Right, cableY);
            float x = area.X + 4 - Math.Min(clipScroll.Value, max);
            clipLift.Target = 0;
            for (int i = 0; i < list.Count; i++, x += ClipPitch)
            {
                if (x + ClipCardW <= area.Left || x >= area.Right) continue;
                var item = list[i];
                string id = "clip" + item.Id, del = "clipDel" + item.Id, edit = "clipEdit" + item.Id;
                bool hot = Hover(id) || Hover(del) || Hover(edit), focused = keyboardMode && (focusId == id || focusId == del);
                if (hot && !Anim.Reduced)
                {
                    if (liftedClip != item.Id) { liftedClip = item.Id; clipLift.Snap(0); }
                    clipLift.Target = 1;
                }
                float lift = !Anim.Reduced && liftedClip == item.Id ? 3.5f * Ease.Clamp01(clipLift.Value) : 0;
                var card = new RectangleF(x, cableY + 10 - lift, ClipCardW, ClipCardH);
                DrawClipCard(item, card, cableY, id, del, edit, hot, focused);

                var hit = RectangleF.Intersect(card, area);
                if (hit.Width <= 0 || hit.Height <= 0) continue;
                AddHit(hit, id, delegate
                {
                    var tc = CanvasRect(new RectangleF(w / 2, 16, 0, 0));
                    VanishTo(new[] { CanvasRect(hit) }, new PointF(tc.X, tc.Y), false);
                    CopyClip(item);
                });
                if (item.Image != null)
                {
                    var editHit = RectangleF.Intersect(ClipEditRect(card), area);
                    if (editHit.Width > 0 && editHit.Height > 0)
                        AddHit(editHit, edit, delegate { if (item.Image != null) OpenRedactBitmap(OwnCopy(item.Image)); });
                }
                var remove = RectangleF.Intersect(ClipDeleteRect(card), area);
                if (remove.Width > 0 && remove.Height > 0) AddHit(remove, del, delegate
                {
                    int index = list.IndexOf(item);
                    if (index < 0) return;
                    Vanish(new[] { CanvasRect(hit) }, false);
                    RemoveClip(list, index);
                    clipScroll.Target = Math.Min(clipScroll.Target, ClipScrollMax(w));
                    if (Anim.Reduced) clipScroll.Snap(clipScroll.Target);
                    Kick();
                });
            }
            g.Restore(st);
        }

        static RectangleF ClipDeleteRect(RectangleF card)
        {
            return new RectangleF(card.Right - 29, card.Y + 6, 24, 24);
        }

        static RectangleF ClipEditRect(RectangleF card)
        {
            return new RectangleF(card.Right - 53, card.Bottom - 30, 47, 24);
        }

        void DrawClipCard(ClipItem item, RectangleF card, float cableY, string id, string del, string edit, bool hot, bool focused)
        {
            bool image = item.Image != null;
            var shadow = card;
            shadow.Y += 3;
            p.FillRound(shadow, 9, Ink.Scrim(36));
            float s = Squish(id, card, 0.96f);
            Color paper = Ink.Mix(p.T.Bg, p.T.Text, hot ? 0.19f : 0.13f);
            if (!image && p.T.IsDark) paper = Ink.Mix(paper, Color.Peru, 0.10f);
            p.FillRound(card, 9, Color.FromArgb(255, paper));
            if (image)
            {
                float width = card.Width - 8, height = width * item.Thumb.Height / item.Thumb.Width;
                p.Image(item.Thumb, new RectangleF(card.X + 4, card.Y + (card.Height - height) / 2, width, height), 6);
            }
            else
            {
                string note = item.Text.Length > 220 ? item.Text.Substring(0, 220) : item.Text;
                p.TextBox(note, Ink.Semi(13f), p.T.Text, new RectangleF(card.X + 11, card.Y + 16, card.Width - 24, card.Height - 25));
            }
            Unsquish(card, s);
            if (image)
            {
                var b = ClipEditRect(card);
                var fill = Ink.Mix(Color.FromArgb(255, p.T.Bg), p.T.Text, Pressed(edit) ? 0.24f : Hover(edit) ? 0.18f : 0.09f);
                p.FillRound(b, 12, fill);
                p.TextCenter(Lang.T("Edit"), Ink.Semi(11.5f), p.T.Text, b.X + b.Width / 2, b.Y + b.Height / 2);
            }
            Color pin = p.T.IsDark ? Color.FromArgb(212, 184, 143) : Color.FromArgb(146, 110, 72);
            p.FillRound(new RectangleF(card.X + 16, cableY - 3, 5, card.Y - cableY + 10), 2, pin);
            p.FillRound(new RectangleF(card.Right - 21, cableY - 3, 5, card.Y - cableY + 10), 2, pin);
            p.Text(Fmt.Hm(item.Utc.ToLocalTime()), Ink.Small(11f), p.T.Sub, card.X + card.Width / 2, card.Bottom + 14,
                card.Width, StringAlignment.Center);
            if (hot || focused)
            {
                var r = ClipDeleteRect(card);
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                p.Circle(cx, cy, 10, Ink.Mix(Color.FromArgb(255, p.T.Bg), p.T.Text, Hover(del) ? 0.24f : 0.1f));
                p.Cross(cx, cy, 8, p.T.Text);
            }
        }
    }
}
