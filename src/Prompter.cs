using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Deskmate
{
    // Teleprompter: pasted script scrolls on the island
    partial class Island
    {
        const float PrompterPad = 28, PrompterMinW = 400, PrompterMaxW = 900, PrompterMinH = 110, PrompterMaxH = 600;
        float PrompterW { get { return prompterSizeW > 0 ? prompterSizeW : Clamp(settings.GetInt("prompter.width", 560), PrompterMinW, PrompterMaxW); } }
        float PrompterH { get { return prompterSizeH > 0 ? prompterSizeH : Clamp(settings.GetInt("prompter.height", 150), PrompterMinH, PrompterMaxH); } }
        float PrompterBg { get { return Clamp(settings.GetInt("prompter.bg", 55), 15, 100) / 100f; } }
        const int PrompterHotkey = 2, PrompterFaster = 4, PrompterSlower = 5, PrompterSetupKey = 6, PrompterRestart = 7, PrompterStop = 8, PrompterBack = 9, PrompterAhead = 10;

        string prompterText;
        float prompterWrapW, prompterWrapPx, prompterWrapLh, prompterPos, pxPerWord = 4;
        bool prompterWrapped, prompterHeld;

        // Paused script takes clicks; running passes them
        bool prompterPaused
        {
            get { return prompterHeld; }
            set
            {
                if (prompterHeld == value) return;
                prompterHeld = value;
                if (!value && current == View.Prompter) PrompterThrough();
                ApplyClickThrough();
            }
        }

        bool PrompterOn { get { return prompterText != null; } }
        int PrompterWpm { get { return Math.Max(60, Math.Min(400, settings.GetInt("prompter.wpm", 140))); } }
        float PrompterPx { get { return Math.Max(15, Math.Min(26, settings.GetInt("prompter.size", 19))); } }
        float PrompterLineH { get { return (float)Math.Round(PrompterPx * 1.55f); } }
        bool PrompterMirror { get { return settings.GetBool("prompter.mirror", false); } }
        static string PrompterFile { get { return Path.Combine(Settings.Dir, "prompter.txt"); } }

        readonly List<float> prompterMarks = new List<float>();
        int prompterNextMark;
        DateTime prompterGoUtc;
        long prompterDrawn;

        // Words measured once per script and size
        readonly List<string> pWords = new List<string>();
        readonly List<float> pWidth = new List<float>();
        readonly List<bool> pGlue = new List<bool>();
        readonly List<int> pParaEnd = new List<int>(), pMarkAt = new List<int>();
        readonly List<bool> pParaBlank = new List<bool>();
        readonly Dictionary<string, float> pMeasured = new Dictionary<string, float>();
        string pTokFor;
        float pTokPx, pSpace;
        int pWordCount;
        static StringFormat measureFormat;

        // Lines as word ranges; text built lazily
        readonly List<int> lineStart = new List<int>(), lineEnd = new List<int>();
        readonly Dictionary<long, string> lineText = new Dictionary<long, string>();
        readonly StringBuilder lineBuild = new StringBuilder();
        int leftShown = -1, anchorTok = -1;
        float anchorFrac, anchorPos;
        string leftStr = "";

        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        bool prompterSetupOpen;

        void OpenPrompter()
        {
            if (prompterSetupOpen) return;
            bool live = PrompterOn, wasPaused = prompterPaused;
            // Hold still while adjusting
            if (live) prompterPaused = true;
            else expanded = false;
            Kick();
            string last = live ? prompterText : "";
            if (!live)
                try { if (File.Exists(PrompterFile)) last = File.ReadAllText(PrompterFile); }
                catch (Exception ex) { Log.Write("prompter read: " + ex.Message); }
            int wpm0 = PrompterWpm, px0 = (int)PrompterPx, bg0 = (int)Math.Round(PrompterBg * 100);
            bool mirror0 = PrompterMirror;
            prompterSetupOpen = true;
            using (var f = new PrompterSetup(last, wpm0, px0, mirror0, bg0, live))
            {
                PrompterKeys(f);
                // Live script previews every change
                if (live) f.Changed += delegate { if (!PrompterOn) return; PrompterLook(f.Wpm, f.Px, f.Mirror, f.Bg); Kick(); };
                bool ok = f.ShowDialog() == DialogResult.OK;
                prompterSetupOpen = false;
                if (!ok)
                {
                    PrompterLook(wpm0, px0, mirror0, bg0);
                    if (live && PrompterOn) prompterPaused = wasPaused;
                    Kick();
                    return;
                }
                PrompterLook(f.Wpm, f.Px, f.Mirror, f.Bg);
                settings.Save();
                try { File.WriteAllText(PrompterFile, f.Script); }
                catch (Exception ex) { Log.Write("prompter save: " + ex.Message); }
                if (!live || !PrompterOn || f.Script.Trim() != prompterText) { StartPrompter(f.Script); return; }
                // Same script: rewrap keeps the place
                prompterPaused = wasPaused;
                Kick();
            }
        }

        void PrompterLook(int wpm, int px, bool mirror, int bg)
        {
            settings.Set("prompter.wpm", wpm);
            settings.Set("prompter.size", px);
            settings.Set("prompter.mirror", mirror);
            settings.Set("prompter.bg", bg);
        }

        void StartPrompter(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            prompterText = text.Trim();
            prompterWrapped = false;
            prompterPaused = false;
            expanded = false;
            Rewind();
            // Shortcut is held only while a script runs
            PrompterHotkeys(true);
            prompterHalf = PrompterW / 2;
            if (WantHalf > canvasHalf) CreateCanvas();
            ApplyGlass();
            Kick();
        }

        void StopPrompter()
        {
            if (prompterSizing != 0) { Capture = false; EndPrompterSize(); }
            prompterText = null;
            prompterWrapped = false;
            prompterHeld = false;
            PrompterCursor(0);
            PrompterHotkeys(false);
            // Frame shrinks canvas once shell fits
            ApplyGlass();
            Kick();
        }

        // Back to the top, with a 3-2-1 countdown
        void Rewind()
        {
            prompterPos = -PrompterLineH * 1.5f;
            prompterNextMark = 0;
            prompterGoUtc = DateTime.UtcNow.AddSeconds(3);
        }

        // From the top, countdown, then rolling
        void RestartPrompter()
        {
            if (!PrompterOn) return;
            Rewind();
            prompterPaused = false;
            Kick();
        }

        // Jump by reading time at current speed
        void SeekPrompter(float seconds)
        {
            if (!PrompterOn) return;
            prompterPos = Clamp(prompterPos + PrompterSpeedPx() * seconds, -PrompterLineH * 1.5f, PrompterEnd());
            SyncMarks();
            Kick();
        }

        void TogglePrompter()
        {
            if (!PrompterOn) return;
            if (prompterPaused && prompterPos >= PrompterEnd()) Rewind();
            prompterPaused = !prompterPaused;
            Kick();
        }

        void NudgeSpeed(int step)
        {
            settings.Set("prompter.wpm", Math.Max(60, Math.Min(400, PrompterWpm + step * 20)));
            settings.Save();
            Kick();
        }

        void ScrollPrompter(int delta)
        {
            prompterPos = Clamp(prompterPos - delta / 120f * PrompterLineH, -PrompterLineH * 1.5f, PrompterEnd());
            SyncMarks();
            Kick();
        }

        void SyncMarks()
        {
            prompterNextMark = 0;
            while (prompterNextMark < prompterMarks.Count && prompterMarks[prompterNextMark] <= prompterPos) prompterNextMark++;
        }

        float PrompterEnd() { return !prompterWrapped ? 0 : Math.Max(0, (lineStart.Count - 1) * PrompterLineH); }

        bool PrompterRunning() { return PrompterOn && !prompterPaused && current == View.Prompter; }

        int Countdown() { return (int)Math.Ceiling((prompterGoUtc - DateTime.UtcNow).TotalSeconds); }

        float PrompterSpeedPx() { return PrompterWpm / 60f * pxPerWord; }

        void StepPrompter(float dt)
        {
            StepPrompterSize();
            if (!PrompterRunning() || !prompterWrapped || Countdown() > 0) return;
            float next = prompterPos + PrompterSpeedPx() * dt;
            // [jeda] in the script stops here
            if (prompterNextMark < prompterMarks.Count && next >= prompterMarks[prompterNextMark])
            {
                prompterPos = prompterMarks[prompterNextMark++];
                prompterPaused = true;
                return;
            }
            prompterPos = next;
            float end = PrompterEnd();
            if (prompterPos >= end) { prompterPos = end; prompterPaused = true; }
        }

        // What the frame shows; same key means no redraw
        long PrompterKey()
        {
            int cd = Countdown();
            long k = (long)Math.Round(prompterPos * scale);
            k = k * 31 + (cd > 0 && !prompterPaused ? cd : 0);
            k = k * 31 + (prompterPaused ? 1 : 2);
            k = k * 31 + LeftSeconds();
            k = k * 31 + (pointerInside ? 1 : 2);
            k = k * 31 + (hoverId == null ? 0 : hoverId.GetHashCode());
            k = k * 31 + prompterEdge * 16 + prompterSizing;
            return k;
        }

        int LeftSeconds()
        {
            float speed = PrompterSpeedPx();
            if (speed <= 0) return -1;
            return (int)Math.Ceiling(Math.Max(0, PrompterEnd() - prompterPos) / speed);
        }

        string LeftText()
        {
            int s = LeftSeconds();
            if (s < 0) return "";
            if (s != leftShown) { leftShown = s; leftStr = (s / 60) + ":" + (s % 60).ToString("00"); }
            return leftStr;
        }

        float WordWidth(string s, Font f)
        {
            float w;
            if (pMeasured.TryGetValue(s, out w)) return w;
            if (measureFormat == null)
            {
                measureFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
                measureFormat.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            }
            w = g.MeasureString(s, f, PointF.Empty, measureFormat).Width;
            pMeasured[s] = w;
            return w;
        }

        // Script into measured words, [jeda] marks
        void TokenizePrompter(Font f)
        {
            pWords.Clear(); pWidth.Clear(); pGlue.Clear(); pParaEnd.Clear(); pParaBlank.Clear(); pMarkAt.Clear();
            pMeasured.Clear();
            lineText.Clear();
            pWordCount = 0;
            pSpace = WordWidth(" ", f);
            // Narrowest line any width allows
            float limit = PrompterMinW - PrompterPad * 2 - 2;
            foreach (var para in prompterText.Replace("\r\n", "\n").Split('\n'))
            {
                var parts = para.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in parts)
                {
                    if (raw.Equals("[jeda]", StringComparison.OrdinalIgnoreCase) || raw.Equals("[pause]", StringComparison.OrdinalIgnoreCase))
                    {
                        pMarkAt.Add(pWords.Count);
                        continue;
                    }
                    pWordCount++;
                    float w = WordWidth(raw, f);
                    if (w <= limit) { pWords.Add(raw); pWidth.Add(w); pGlue.Add(false); continue; }
                    // Wider than a line: split by characters
                    int start = 0;
                    while (start < raw.Length)
                    {
                        int len = 1;
                        while (start + len < raw.Length && WordWidth(raw.Substring(start, len + 1), f) <= limit) len++;
                        string piece = raw.Substring(start, len);
                        pWords.Add(piece); pWidth.Add(WordWidth(piece, f)); pGlue.Add(start > 0);
                        start += len;
                    }
                }
                pParaEnd.Add(pWords.Count);
                pParaBlank.Add(parts.Length == 0);
            }
            pTokFor = prompterText;
            pTokPx = PrompterPx;
        }

        // Rewrap by sums; reading place stays put
        void WrapPrompter(float width, Font f)
        {
            float lh = PrompterLineH;
            int anchor = -1;
            float frac = 0;
            bool same = prompterWrapped && ReferenceEquals(pTokFor, prompterText) && lineStart.Count > 0 && prompterWrapLh > 0;
            // Unmoved since last wrap: keep the word
            if (same && prompterPos >= 0 && anchorTok >= 0 && prompterPos == anchorPos) { anchor = anchorTok; frac = anchorFrac; }
            else if (same && prompterPos >= 0)
            {
                float at = prompterPos / prompterWrapLh;
                int li = Math.Min(lineStart.Count - 1, (int)at);
                anchor = lineStart[li];
                frac = Math.Min(0.999f, at - li);
            }
            float oldLh = prompterWrapLh;
            if (!ReferenceEquals(pTokFor, prompterText) || pTokPx != PrompterPx) TokenizePrompter(f);

            lineStart.Clear();
            lineEnd.Clear();
            // Margin keeps sums under the drawn width
            float room = width - 2;
            int tok = 0, filled = 0;
            for (int p = 0; p < pParaEnd.Count; p++)
            {
                int end = pParaEnd[p];
                if (tok == end)
                {
                    int n = lineStart.Count;
                    if (pParaBlank[p] && n > 0 && lineEnd[n - 1] > lineStart[n - 1]) { lineStart.Add(tok); lineEnd.Add(tok); }
                    continue;
                }
                int s = tok;
                float cur = 0;
                for (; tok < end; tok++)
                {
                    float add = pWidth[tok] + (tok > s && !pGlue[tok] ? pSpace : 0);
                    if (tok > s && cur + add > room) { lineStart.Add(s); lineEnd.Add(tok); filled++; s = tok; cur = pWidth[tok]; }
                    else cur += add;
                }
                lineStart.Add(s);
                lineEnd.Add(end);
                filled++;
            }
            if (lineStart.Count == 0) { lineStart.Add(0); lineEnd.Add(0); }
            prompterWrapped = true;
            prompterWrapW = width;
            prompterWrapPx = PrompterPx;
            prompterWrapLh = lh;
            pxPerWord = lh * Math.Max(1, filled) / Math.Max(1, pWordCount);
            prompterMarks.Clear();
            foreach (int m in pMarkAt)
            {
                float y = (m == 0 ? 0 : LineOf(m - 1)) * lh;
                if (prompterMarks.Count == 0 || prompterMarks[prompterMarks.Count - 1] < y) prompterMarks.Add(y);
            }
            if (anchor >= 0) prompterPos = (LineOf(anchor) + frac) * lh;
            else if (same && prompterPos < 0) prompterPos *= lh / oldLh;
            prompterPos = Clamp(prompterPos, -lh * 1.5f, PrompterEnd());
            anchorTok = anchor;
            anchorFrac = frac;
            anchorPos = prompterPos;
            SyncMarks();
        }

        // Last line starting at or before word
        int LineOf(int word)
        {
            int lo = 0, hi = lineStart.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (lineStart[mid] <= word) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        string LineText(int i)
        {
            int s = lineStart[i], e = lineEnd[i];
            if (e <= s) return "";
            long key = ((long)s << 32) | (uint)e;
            string str;
            if (lineText.TryGetValue(key, out str)) return str;
            lineBuild.Length = 0;
            for (int t = s; t < e; t++)
            {
                if (t > s && !pGlue[t]) lineBuild.Append(' ');
                lineBuild.Append(pWords[t]);
            }
            str = lineBuild.ToString();
            if (lineText.Count > 4096) lineText.Clear();
            lineText[key] = str;
            return str;
        }

        // See-through backdrop, no frost
        void FillPrompterBg(System.Drawing.Drawing2D.GraphicsPath path)
        {
            g.FillPath(p.B(Ink.A(Ink.Base.Bg, PrompterBg)), path);
            g.DrawPath(p.P(Ink.Base.Line, 1f), path);
        }

        void DrawPrompter(float w, float h, DateTime now)
        {
            // Fading out after Stop: nothing left to draw
            if (prompterText == null) return;
            var f = Ink.Semi(PrompterPx);
            // Wrap at the final width so a morph never reflows the script
            float lh = PrompterLineH, textW = PrompterW - PrompterPad * 2;
            if (!prompterWrapped || prompterWrapW != textW || prompterWrapPx != PrompterPx || !ReferenceEquals(pTokFor, prompterText)) WrapPrompter(textW, f);

            var st = g.Save();
            g.IntersectClip(new RectangleF(0, 6, w, h - 12));
            if (PrompterMirror) { g.TranslateTransform(w, 0); g.ScaleTransform(-1, 1); }
            int cd = Countdown();
            if (cd > 0 && !prompterPaused) p.TextCenter(cd.ToString(), Ink.Display(44f), p.T.Text, w / 2, h / 2 - 4);
            else DrawPrompterLines(w, h, f, lh, textW);
            g.Restore(st);

            // Progress along the bottom edge
            float end = PrompterEnd(), done = end <= 0 ? 1 : Ease.Clamp01(prompterPos / end);
            var track = new RectangleF(PrompterPad, h - 8, w - PrompterPad * 2, 2);
            p.FillRound(track, 1, p.T.Line);
            if (done > 0) p.FillRound(new RectangleF(track.X, track.Y, Math.Max(2, track.Width * done), 2), 1, p.T.Accent);
            DrawKnob(track.X + track.Width * done, track.Y + 1, track.X, track.Right);

            if (prompterSizing != 0) p.TextRight(SizeText(), Ink.Small(11f), p.T.Sub, w - 22, 18);
            else if (pointerInside) DrawPrompterControls(w, h);
            else
            {
                string state = !prompterPaused ? LeftText() : prompterPos >= end ? Lang.T("selesai") : Lang.T("jeda") + " · " + LeftText();
                p.TextRight(state, Ink.Small(11f), p.T.Sub, w - 22, 18);
            }
            if (prompterPaused && (pointerInside || prompterSizing != 0)) DrawPrompterGrips(w, h);
        }

        void DrawPrompterLines(float w, float h, Font f, float lh, float textW)
        {
            // Whole device pixels: crisp text, fewer redraws
            float pos = (float)Math.Round(prompterPos * scale) / scale;
            float readY = (float)Math.Round(h * 0.4f), baseA = p.Alpha, bottom = pointerInside && prompterSizing == 0 ? h - 52 : h - 4;
            int first = Math.Max(0, (int)Math.Floor((pos - readY) / lh));
            for (int i = first; i < lineStart.Count; i++)
            {
                float cy = readY + i * lh - pos;
                if (cy - lh > h) break;
                float near = Ease.Clamp01(Math.Abs(cy - readY) / lh);
                float edge = Ease.Clamp01(Math.Min(cy - 4, bottom - cy) / (lh * 1.3f));
                p.Alpha = baseA * edge * (1 - 0.55f * near);
                if (p.Alpha <= 0.02f) continue;
                if (lineEnd[i] > lineStart[i]) p.Text(LineText(i), f, p.T.Text, w / 2, cy, textW, StringAlignment.Center);
                if (prompterMarks.Contains(i * lh)) p.Circle(PrompterPad - 12, cy, 3f, p.T.Accent);
            }
            p.Alpha = baseA;
        }

        void DrawPrompterControls(float w, float h)
        {
            float cy = h - 30, bw = 30;
            var bar = new RectangleF(w / 2 - 132, cy - 18, 264, 36);
            p.FillRound(bar, 18, p.T.Fill);
            var slow = new RectangleF(bar.X + 6, cy - 15, bw, bw);
            var play = new RectangleF(slow.Right + 4, cy - 15, bw, bw);
            var fast = new RectangleF(play.Right + 4, cy - 15, bw, bw);
            RoundButton(slow, "pSlow", delegate { NudgeSpeed(-1); });
            p.FillRound(new RectangleF(slow.X + 10, cy - 0.8f, 10, 1.6f), 0.8f, p.T.Text);
            RoundButton(play, "pPlay", delegate { TogglePrompter(); });
            if (prompterPaused)
                using (var tri = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    tri.AddPolygon(new[] { new PointF(play.X + 12, cy - 6), new PointF(play.X + 21, cy), new PointF(play.X + 12, cy + 6) });
                    g.FillPath(p.B(p.T.Text), tri);
                }
            else
            {
                p.FillRound(new RectangleF(play.X + 10.5f, cy - 5.5f, 3, 11), 1, p.T.Text);
                p.FillRound(new RectangleF(play.X + 16.5f, cy - 5.5f, 3, 11), 1, p.T.Text);
            }
            RoundButton(fast, "pFast", delegate { NudgeSpeed(1); });
            p.FillRound(new RectangleF(fast.X + 10, cy - 0.8f, 10, 1.6f), 0.8f, p.T.Text);
            p.FillRound(new RectangleF(fast.X + 14.2f, cy - 5, 1.6f, 10), 0.8f, p.T.Text);
            p.Text(Lang.F("{0} kata/menit", PrompterWpm), Ink.Small(11f), p.T.Sub, fast.Right + 10, cy, bar.Right - fast.Right - 52, StringAlignment.Near);
            var close = new RectangleF(bar.Right - 36, cy - 15, bw, bw);
            RoundButton(close, "pClose", delegate { StopPrompter(); });
            p.Icon('', 10f, p.T.Text, close.X + bw / 2, cy);
        }

        void RoundButton(RectangleF r, string id, Action act)
        {
            if (Hover(id)) p.FillRound(r, r.Height / 2, p.T.FillHover);
            AddHit(r, id, act);
        }

        // Paused island: edges drag to resize

        const int EdgeLeft = 1, EdgeRight = 2, EdgeBottom = 4;
        const float GripReach = 10, GripCorner = 24;
        int prompterEdge, prompterSizing;
        bool prompterSizeMoved;
        float sizeW0, sizeH0, prompterSizeW, prompterSizeH, sizeShownW, sizeShownH;
        Point sizeFrom, sizeTo;
        string sizeStr = "";

        int PrompterEdgeAt(PointF pt)
        {
            if (current != View.Prompter || !prompterPaused || !PrompterOn) return 0;
            var s = shell;
            if (!s.Contains(pt)) return 0;
            bool nearL = pt.X <= s.X + GripReach, nearR = pt.X >= s.Right - GripReach, nearB = pt.Y >= s.Bottom - GripReach;
            bool cornerB = pt.Y >= s.Bottom - GripCorner;
            int e = 0;
            if (nearL || (nearB && pt.X <= s.X + GripCorner)) e |= EdgeLeft;
            else if (nearR || (nearB && pt.X >= s.Right - GripCorner)) e |= EdgeRight;
            if (nearB || (e != 0 && cornerB)) e |= EdgeBottom;
            return e;
        }

        void PrompterCursor(int edge)
        {
            var want = edge == EdgeBottom ? Cursors.SizeNS : edge == (EdgeLeft | EdgeBottom) ? Cursors.SizeNESW : edge == (EdgeRight | EdgeBottom) ? Cursors.SizeNWSE : edge != 0 ? Cursors.SizeWE : Cursors.Default;
            if (Cursor != want) Cursor = want;
        }

        // From mouse move: cursor and grip highlight
        void PrompterHover(PointF pt, bool inside)
        {
            int edge = inside ? PrompterEdgeAt(pt) : 0;
            PrompterCursor(edge);
            if (edge == prompterEdge) return;
            prompterEdge = edge;
            Kick();
        }

        bool PrompterGrab(PointF pt, Point screen)
        {
            int edge = PrompterEdgeAt(pt);
            if (edge == 0) return false;
            Capture = true;
            BeginPrompterSize(edge, screen);
            return true;
        }

        void BeginPrompterSize(int edge, Point screen)
        {
            prompterEdge = prompterSizing = edge;
            sizeFrom = sizeTo = screen;
            sizeW0 = prompterSizeW = PrompterW;
            sizeH0 = prompterSizeH = PrompterH;
            PrompterCursor(edge);
            // Widest canvas once, not per move
            prompterHalf = PrompterMaxW / 2;
            if (WantHalf > canvasHalf) CreateCanvas();
            Kick();
        }

        void SizePrompterTo(Point screen)
        {
            if (screen == sizeTo) return;
            sizeTo = screen;
            Kick();
        }

        // Running again: hover state from pause goes
        void PrompterThrough()
        {
            pointerInside = false;
            hoverId = null;
            prompterEdge = 0;
            PrompterCursor(0);
        }

        // Per frame: newest pointer sets the shape
        void StepPrompterSize()
        {
            prompterSizeMoved = false;
            if (prompterSizing == 0) return;
            float dx = (sizeTo.X - sizeFrom.X) / scale, dy = (sizeTo.Y - sizeFrom.Y) / scale;
            float w = sizeW0, h = sizeH0;
            // Centred island grows on both sides
            if ((prompterSizing & EdgeRight) != 0) w += dx * 2;
            if ((prompterSizing & EdgeLeft) != 0) w -= dx * 2;
            if ((prompterSizing & EdgeBottom) != 0) h += dy;
            w = (float)Math.Round(Clamp(w, PrompterMinW, PrompterMaxW) / 2) * 2;
            h = (float)Math.Round(Clamp(h, PrompterMinH, PrompterMaxH));
            // Changed shape: one vsync-paced frame
            prompterSizeMoved = w != prompterSizeW || h != prompterSizeH;
            prompterSizeW = w;
            prompterSizeH = h;
            sw.Snap(prompterSizeW);
            sh.Snap(prompterSizeH);
        }

        void EndPrompterSize()
        {
            if (prompterSizing == 0) return;
            StepPrompterSize();
            prompterSizing = 0;
            settings.Set("prompter.width", (int)prompterSizeW);
            settings.Set("prompter.height", (int)prompterSizeH);
            settings.Save();
            prompterSizeW = prompterSizeH = 0;
            prompterHalf = PrompterW / 2;
            // Canvas back to what fits
            if (canvasHalf > WantHalf + 0.5f) CreateCanvas();
            Kick();
        }

        string SizeText()
        {
            if (prompterSizeW != sizeShownW || prompterSizeH != sizeShownH)
            {
                sizeShownW = prompterSizeW;
                sizeShownH = prompterSizeH;
                sizeStr = (int)sizeShownW + " × " + (int)sizeShownH;
            }
            return sizeStr;
        }

        // Faint grips; the active edge brightens
        void DrawPrompterGrips(float w, float h)
        {
            int on = prompterSizing != 0 ? prompterSizing : prompterEdge;
            float side = Math.Min(36, h * 0.3f), cy = h / 2;
            p.FillRound(new RectangleF(4, cy - side / 2, 3, side), 1.5f, Ink.A(p.T.Text, (on & EdgeLeft) != 0 ? 0.85f : 0.3f));
            p.FillRound(new RectangleF(w - 7, cy - side / 2, 3, side), 1.5f, Ink.A(p.T.Text, (on & EdgeRight) != 0 ? 0.85f : 0.3f));
            if ((on & EdgeBottom) != 0) p.FillRound(new RectangleF(w / 2 - 22, h - 4.5f, 44, 3), 1.5f, Ink.A(p.T.Text, 0.85f));
        }
    }
}
