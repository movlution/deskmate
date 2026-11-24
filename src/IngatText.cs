using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Deskmate
{
    // Words on screen: UI Automation first; OCR only when the app shows nothing to it and the picture changed
    static class IngatText
    {
        const int MaxNodes = 350, BudgetMs = 700, MaxChars = 6000;

        static readonly string[] Browsers = { "chrome", "msedge", "brave", "vivaldi", "opera", "firefox", "arc", "zen", "floorp", "librewolf", "thorium" };

        public static bool IsBrowser(string exe) { return Array.IndexOf(Browsers, (exe ?? "").ToLowerInvariant()) >= 0; }

        // Text of the window, its URL for browsers; secret: a password box has focus or shows
        public static string Read(IntPtr hwnd, string exe, out string url, out bool secret)
        {
            url = "";
            secret = false;
            AutomationElement root;
            try { root = AutomationElement.FromHandle(hwnd); }
            catch (Exception) { return ""; }
            if (root == null) return "";
            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused != null && focused.Current.IsPassword) { secret = true; return ""; }
            }
            catch (Exception) { }

            bool browser = IsBrowser(exe);
            var sb = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sw = Stopwatch.StartNew();
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(root);
            var walker = TreeWalker.ControlViewWalker;
            int nodes = 0;
            while (queue.Count > 0 && nodes < MaxNodes && sw.ElapsedMilliseconds < BudgetMs && sb.Length < MaxChars)
            {
                var el = queue.Dequeue();
                nodes++;
                try
                {
                    var c = el.Current;
                    if (c.IsPassword) { secret = true; continue; }
                    var type = c.ControlType;
                    if (type == ControlType.Edit)
                    {
                        string v = Value(el);
                        if (browser && url.Length == 0 && LooksLikeUrl(v)) { url = v; continue; }
                        Add(sb, seen, v);
                    }
                    else if (type == ControlType.Document)
                    {
                        string doc = DocumentText(el);
                        if (doc.Length > 0) { Add(sb, seen, doc); continue; }
                        Add(sb, seen, c.Name);
                    }
                    else if (type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.ListItem || type == ControlType.TabItem ||
                             type == ControlType.TreeItem || type == ControlType.Header || type == ControlType.HeaderItem || type == ControlType.DataItem)
                        Add(sb, seen, c.Name);
                    // Chrome and menus would fill the budget with buttons
                    if (type == ControlType.MenuBar || type == ControlType.ToolBar && !browser || type == ControlType.ScrollBar || type == ControlType.TitleBar) continue;
                    var child = walker.GetFirstChild(el);
                    while (child != null && queue.Count < MaxNodes * 2)
                    {
                        queue.Enqueue(child);
                        child = walker.GetNextSibling(child);
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }
            if (browser && url.Length > 0 && !url.Contains("://")) url = "https://" + url;
            string text = sb.ToString();
            return text.Length > MaxChars ? text.Substring(0, MaxChars) : text;
        }

        static void Add(StringBuilder sb, HashSet<string> seen, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            s = s.Trim();
            if (s.Length < 2 || s.Length > 4000 && sb.Length > 0 || !seen.Add(s)) return;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(s.Length > 4000 ? s.Substring(0, 4000) : s);
        }

        static string Value(AutomationElement el)
        {
            object p;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out p)) { try { return ((ValuePattern)p).Current.Value ?? ""; } catch (Exception) { } }
            return "";
        }

        static string DocumentText(AutomationElement el)
        {
            object p;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out p)) return "";
            try { return ((TextPattern)p).DocumentRange.GetText(MaxChars) ?? ""; }
            catch (Exception) { return ""; }
        }

        static bool LooksLikeUrl(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length > 2048 || v.IndexOf(' ') >= 0) return false;
            if (v.StartsWith("http://") || v.StartsWith("https://") || v.StartsWith("file://")) return true;
            int dot = v.IndexOf('.');
            return dot > 0 && dot < v.Length - 2;
        }

        // Chats: every line with its horizontal place in the window, "[62-95] text", top to bottom.
        // In most chat apps the user's own bubbles hug the right edge and a chat list fills the far left;
        // the position lets the model tell sent from received and previews from the open chat.
        public static string ReadChat(IntPtr hwnd, string exe, out string url, out bool secret)
        {
            url = "";
            secret = false;
            RECT wr;
            if (!GetWindowRect(hwnd, out wr) || wr.R - wr.L < 200) return "";
            double left = wr.L, width = Math.Max(1, wr.R - wr.L);
            AutomationElement root;
            try { root = AutomationElement.FromHandle(hwnd); }
            catch (Exception) { return ""; }
            if (root == null) return "";
            bool browser = IsBrowser(exe);
            var lines = new List<KeyValuePair<System.Windows.Rect, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sw = Stopwatch.StartNew();
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(root);
            var walker = TreeWalker.ControlViewWalker;
            int nodes = 0, chars = 0;
            // Web-based chats nest deep: a larger budget than the plain read
            while (queue.Count > 0 && nodes < 1600 && sw.ElapsedMilliseconds < 1600 && chars < 9000)
            {
                var el = queue.Dequeue();
                nodes++;
                try
                {
                    var c = el.Current;
                    if (c.IsPassword) { secret = true; continue; }
                    var type = c.ControlType;
                    if (type == ControlType.ScrollBar || type == ControlType.TitleBar || type == ControlType.MenuBar) continue;
                    if (type == ControlType.Edit)
                    {
                        string v = Value(el);
                        if (browser && url.Length == 0 && LooksLikeUrl(v)) { url = v; continue; }
                    }
                    else if (type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.ListItem && walker.GetFirstChild(el) == null)
                    {
                        string s = (c.Name ?? "").Trim();
                        var r = c.BoundingRectangle;
                        if (s.Length >= 1 && !r.IsEmpty && r.Width > 0 && seen.Add(s + "@" + (int)r.Top))
                        {
                            lines.Add(new KeyValuePair<System.Windows.Rect, string>(r, s.Length > 1500 ? s.Substring(0, 1500) : s));
                            chars += s.Length;
                        }
                        if (type == ControlType.Text) continue;
                    }
                    var child = walker.GetFirstChild(el);
                    while (child != null && queue.Count < 4000)
                    {
                        queue.Enqueue(child);
                        child = walker.GetNextSibling(child);
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }
            if (browser && url.Length > 0 && !url.Contains("://")) url = "https://" + url;
            lines.Sort((a, b) => a.Key.Top != b.Key.Top ? a.Key.Top.CompareTo(b.Key.Top) : a.Key.Left.CompareTo(b.Key.Left));
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                int x0 = (int)Math.Max(0, Math.Min(100, (l.Key.Left - left) * 100 / width));
                int x1 = (int)Math.Max(0, Math.Min(100, (l.Key.Right - left) * 100 / width));
                sb.Append('[').Append(x0).Append('-').Append(x1).Append("] ").Append(l.Value.Replace("\r", " ").Replace("\n", " / ")).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        // OCR fallback

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        // Picture of the window's own content, even where other windows cover it; null when minimized or tiny
        public static Bitmap Snap(IntPtr hwnd)
        {
            RECT r;
            if (IsIconic(hwnd) || !GetWindowRect(hwnd, out r)) return null;
            int w = r.R - r.L, h = r.B - r.T;
            if (w < 200 || h < 120 || w > 10000 || h > 10000) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try
            {
                bool ok;
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    // PW_RENDERFULLCONTENT: GPU-drawn content too (WebView2, Chromium, UWP)
                    try { ok = PrintWindow(hwnd, dc, 2); }
                    finally { g.ReleaseHdc(dc); }
                }
                if (ok && !Blank(bmp)) return bmp;
                // Some windows refuse: fall back to what the screen shows
                var bounds = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
                bounds.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
                if (bounds.Width < 200 || bounds.Height < 120) { bmp.Dispose(); return null; }
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); g.CopyFromScreen(bounds.Location, new Point(bounds.X - r.L, bounds.Y - r.T), bounds.Size); }
                return bmp;
            }
            catch (Exception) { bmp.Dispose(); return null; }
        }

        // All one color (black from a refused PrintWindow)
        static bool Blank(Bitmap b)
        {
            var first = b.GetPixel(b.Width / 2, b.Height / 2);
            for (int i = 1; i < 9; i++)
            {
                var p = b.GetPixel(b.Width * i / 10, b.Height * ((i * 7) % 10) / 10);
                if (p.ToArgb() != first.ToArgb()) return false;
            }
            return true;
        }

        // OCR with positions, for chats
        public static string OcrPlaced(Bitmap b)
        {
            if (!Deskmate.Ocr.Available) return "";
            string tmp = Path.Combine(IngatStore.Dir, "ocr-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Directory.CreateDirectory(IngatStore.Dir);
                b.Save(tmp, ImageFormat.Png);
                string text = Deskmate.Ocr.ReadPlaced(tmp) ?? "";
                return text.Length > 9000 ? text.Substring(text.Length - 9000) : text;
            }
            catch (Exception ex) { Log.Write("ingat ocr: " + ex.Message); return ""; }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
        }

        // 64-bit difference hash: rows of 9 greys, each bit "brighter than the next"
        public static ulong Hash(Bitmap b)
        {
            using (var small = new Bitmap(9, 8, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(b, 0, 0, 9, 8);
                }
                ulong h = 0;
                int bit = 0;
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++, bit++)
                    {
                        Color a = small.GetPixel(x, y), c = small.GetPixel(x + 1, y);
                        if (a.R + a.G + a.B > c.R + c.G + c.B) h |= 1UL << bit;
                    }
                return h;
            }
        }

        public static int Distance(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int n = 0;
            while (x != 0) { x &= x - 1; n++; }
            return n;
        }

        // Windows OCR on a temp PNG; the file is gone before this returns
        public static string Ocr(Bitmap b)
        {
            if (!Deskmate.Ocr.Available) return "";
            string tmp = Path.Combine(IngatStore.Dir, "ocr-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Directory.CreateDirectory(IngatStore.Dir);
                b.Save(tmp, ImageFormat.Png);
                string text = Deskmate.Ocr.Read(tmp) ?? "";
                return text.Length > MaxChars ? text.Substring(0, MaxChars) : text;
            }
            catch (Exception ex) { Log.Write("ingat ocr: " + ex.Message); return ""; }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
        }
    }
}
