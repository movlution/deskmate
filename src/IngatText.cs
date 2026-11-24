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

        // OCR fallback

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        // Picture of the window as it is on screen; null when off screen or tiny
        public static Bitmap Snap(IntPtr hwnd)
        {
            RECT r;
            if (IsIconic(hwnd) || !GetWindowRect(hwnd, out r)) return null;
            var bounds = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            bounds.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
            if (bounds.Width < 200 || bounds.Height < 120) return null;
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                return bmp;
            }
            catch (Exception) { bmp.Dispose(); return null; }
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
