using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace Deskmate
{
    // Dual-zone perceptual fingerprint: full window + inner content area so scrolling and new chat lines trigger immediately
    struct IngatStamp
    {
        public ulong Full;
        public ulong Inner;
        public bool Valid;
    }

    // Words on screen: cached UI Automation first, structured by region; in-memory OCR when the app exposes no text
    static class IngatText
    {
        const int MaxNodes = 750, MaxNavNodes = 100, BudgetMs = 450, ChatMaxNodes = 1800, ChatBudgetMs = 850;
        public const int MaxChars = 6500, ChatMaxChars = 9000;

        static readonly string[] Browsers = { "chrome", "msedge", "brave", "vivaldi", "opera", "firefox", "arc", "zen", "floorp", "librewolf", "thorium" };

        public static bool IsBrowser(string exe) { return Array.IndexOf(Browsers, (exe ?? "").ToLowerInvariant()) >= 0; }

        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);

        struct PlacedLine
        {
            public double Top;
            public double Left;
            public double Right;
            public string Text;
        }

        static CacheRequest NewCache()
        {
            // Element-only scope: each GetFirstChild/GetNextSibling(el, cache) batches properties in one COM call
            // without eagerly serializing hundreds of grandchildren on a single container node
            var cr = new CacheRequest
            {
                TreeFilter = Automation.ControlViewCondition,
                TreeScope = TreeScope.Element,
            };
            cr.Add(AutomationElement.ControlTypeProperty);
            cr.Add(AutomationElement.NameProperty);
            cr.Add(AutomationElement.IsPasswordProperty);
            cr.Add(AutomationElement.IsOffscreenProperty);
            cr.Add(AutomationElement.HasKeyboardFocusProperty);
            cr.Add(AutomationElement.BoundingRectangleProperty);
            cr.Add(AutomationElement.ProcessIdProperty);
            return cr;
        }

        // Structured capture of the active window: URL, focus, headings, ordered main content, navigation, and anchors
        public static IngatSnapshot ReadSnapshot(IntPtr hwnd, string exe, string title, bool chatMode)
        {
            var snap = new IngatSnapshot
            {
                Source = chatMode ? "chat" : "uia",
                Exe = exe ?? "",
                Title = title ?? "",
                CapturedUtc = DateTime.UtcNow,
            };
            if (hwnd == IntPtr.Zero || IsHungAppWindow(hwnd)) { snap.Source = "window"; return snap; }

            RECT wr;
            bool hasRect = GetWindowRect(hwnd, out wr) && (wr.R - wr.L) >= 160 && (wr.B - wr.T) >= 100;
            if (chatMode && !hasRect) { snap.Source = "window"; return snap; }
            double winL = hasRect ? wr.L : 0, winT = hasRect ? wr.T : 0;
            double winW = hasRect ? Math.Max(1, wr.R - wr.L) : 1000, winH = hasRect ? Math.Max(1, wr.B - wr.T) : 800;
            int winPid;
            Native.GetWindowThreadProcessId(hwnd, out winPid);

            var cache = NewCache();
            AutomationElement root;
            try
            {
                using (cache.Activate()) root = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception) { snap.Source = "window"; return snap; }
            if (root == null) { snap.Source = "window"; return snap; }

            // Focused control first: instant password check and active editor/tab/document text before walking chrome
            string focusedDoc = "";
            try
            {
                AutomationElement focused;
                using (cache.Activate()) focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    if (IsPassword(focused)) { snap.ClearSecret(); return snap; }
                    if (ProcId(focused) == winPid)
                    {
                        var ftype = CtrlType(focused);
                        string fname = IngatStructure.CleanLine(ElemName(focused));
                        if (fname.Length >= 2 && fname.Length <= 140 && !LooksLikeUrl(fname))
                            snap.Focus = fname;
                        if (!chatMode && ftype == ControlType.Document)
                            focusedDoc = DocumentText(focused);
                    }
                }
            }
            catch (Exception) { }

            bool browser = IsBrowser(exe);
            if (chatMode)
            {
                WalkChat(root, cache, browser, winL, winT, winW, winH, snap);
                if (snap.Secret) snap.ClearSecret();
                return snap;
            }

            WalkStructured(root, cache, browser, hasRect, winL, winT, winW, winH, focusedDoc, snap);
            if (snap.Secret) { snap.ClearSecret(); return snap; }
            snap.Kind = IngatStructure.Classify(exe, snap.Url, title, snap.Excerpt(1200));
            var anchors = IngatStructure.ExtractAnchors(title, snap.Url, snap.FormatText(3500), 12);
            for (int i = 0; i < anchors.Count; i++) IngatContext.AddUnique(snap.Anchors, anchors[i], 12);
            return snap;
        }

        static void WalkStructured(AutomationElement root, CacheRequest cache, bool browser, bool hasRect,
            double winL, double winT, double winW, double winH, string focusedDoc, IngatSnapshot snap)
        {
            var mainPlaced = new List<PlacedLine>();
            var sidePlaced = new List<PlacedLine>();
            var docChunks = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (focusedDoc.Length > 0) AddDocLines(docChunks, seen, focusedDoc);

            var sw = Stopwatch.StartNew();
            // Main viewport nodes are always drained before sidebars/trees/tabs so large file trees never starve content
            var mainQueue = new Queue<AutomationElement>();
            var navQueue = new Queue<AutomationElement>();
            mainQueue.Enqueue(root);
            int nodes = 0, navNodes = 0, chars = focusedDoc.Length;
            var walker = TreeWalker.ControlViewWalker;

            bool navWanted = true;
            while ((mainQueue.Count > 0 || (navWanted && navQueue.Count > 0 && navNodes < MaxNavNodes)) &&
                   nodes < MaxNodes && (nodes <= 2 || sw.ElapsedMilliseconds < BudgetMs) && chars < MaxChars)
            {
                bool fromNav = mainQueue.Count == 0;
                var el = fromNav ? navQueue.Dequeue() : mainQueue.Dequeue();
                nodes++;
                if (fromNav)
                {
                    navNodes++;
                    if (snap.NavLines.Count >= 12 && snap.Focus.Length > 0) navWanted = false;
                }
                try
                {
                    if (IsPassword(el)) { snap.Secret = true; return; }
                    var type = CtrlType(el);
                    if (type == null) continue;
                    if (type == ControlType.MenuBar || type == ControlType.ScrollBar || type == ControlType.TitleBar) continue;

                    bool offscreen = IsOffscreen(el);
                    // Browser address bars can report offscreen while collapsed; everything else invisible is skipped
                    if (offscreen && !(browser && snap.Url.Length == 0 && (type == ControlType.Edit || type == ControlType.ToolBar || type == ControlType.Pane || type == ControlType.Group)))
                        continue;

                    var r = Bounds(el);
                    double x0Pct = hasRect && !r.IsEmpty ? (r.Left - winL) * 100.0 / winW : 50;
                    double x1Pct = hasRect && !r.IsEmpty ? (r.Right - winL) * 100.0 / winW : 50;
                    double y0Pct = hasRect && !r.IsEmpty ? (r.Top - winT) * 100.0 / winH : 50;
                    bool sideArea = hasRect && !r.IsEmpty && x1Pct <= 20 && (x1Pct - x0Pct) <= 19 && y0Pct > 10;

                    if (type == ControlType.Edit)
                    {
                        string v = Value(el);
                        if (browser && snap.Url.Length == 0 && LooksLikeUrl(v)) { snap.Url = NormalizeUrl(v); continue; }
                        string cv = IngatStructure.CleanLine(v);
                        if (cv.Length >= 2 && seen.Add(cv))
                        {
                            mainPlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Text = cv });
                            chars += cv.Length;
                        }
                    }
                    else if (type == ControlType.Document)
                    {
                        string doc = DocumentText(el);
                        if (doc.Length > 0)
                        {
                            chars += AddDocLines(docChunks, seen, doc);
                            continue;
                        }
                        string dn = IngatStructure.CleanLine(ElemName(el));
                        if (dn.Length >= 2 && seen.Add(dn))
                        {
                            mainPlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Text = dn });
                            chars += dn.Length;
                        }
                    }
                    else if (type == ControlType.TabItem)
                    {
                        string tn = IngatStructure.CleanLine(ElemName(el));
                        if (tn.Length >= 2)
                        {
                            if (HasFocus(el) || IsSelectedTab(el) || snap.Title.IndexOf(tn, StringComparison.OrdinalIgnoreCase) >= 0 && snap.Focus.Length == 0)
                                snap.Focus = tn;
                            IngatContext.AddUnique(snap.NavLines, tn, 12);
                        }
                    }
                    else if (type == ControlType.Header || type == ControlType.HeaderItem)
                    {
                        string hn = IngatStructure.CleanLine(ElemName(el));
                        if (hn.Length >= 2 && hn.Length <= 140) IngatContext.AddUnique(snap.Headings, hn, 8);
                    }
                    else if (type == ControlType.TreeItem)
                    {
                        string trn = IngatStructure.CleanLine(ElemName(el));
                        if (trn.Length >= 2)
                        {
                            if (HasFocus(el) && snap.Focus.Length == 0) snap.Focus = trn;
                            IngatContext.AddUnique(snap.NavLines, trn, 12);
                        }
                        if (snap.NavLines.Count >= 12) continue;
                    }
                    else if (type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.ListItem || type == ControlType.DataItem)
                    {
                        string s = IngatStructure.CleanLine(ElemName(el));
                        if (s.Length >= 2 && seen.Add(s))
                        {
                            string capped = s.Length > 2000 ? s.Substring(0, 2000) : s;
                            if (sideArea)
                            {
                                IngatContext.AddUnique(snap.NavLines, capped, 12);
                                if (sidePlaced.Count < 30)
                                    sidePlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Text = capped });
                            }
                            else
                            {
                                if (y0Pct >= 4 && y0Pct <= 18 && s.Length <= 110 && snap.Headings.Count < 6 && type == ControlType.Text)
                                    IngatContext.AddUnique(snap.Headings, s, 8);
                                mainPlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Text = capped });
                                chars += capped.Length;
                            }
                        }
                        if (type == ControlType.Text) continue;
                    }

                    if (type == ControlType.ToolBar && !browser) continue;
                    bool navParent = fromNav || sideArea || type == ControlType.Tree || type == ControlType.Tab || type == ControlType.StatusBar;
                    int maxSiblings = navParent ? 24 : 320;
                    int count = 0;
                    var child = walker.GetFirstChild(el, cache);
                    while (child != null && count < maxSiblings && (nodes == 1 && count < 20 || sw.ElapsedMilliseconds < BudgetMs))
                    {
                        count++;
                        var ct = CtrlType(child);
                        bool childToNav = navParent || ct == ControlType.Tree || ct == ControlType.TreeItem || ct == ControlType.Tab || ct == ControlType.TabItem || ct == ControlType.StatusBar;
                        if (!childToNav && hasRect)
                        {
                            var cr = Bounds(child);
                            if (!cr.IsEmpty && ct != ControlType.Document && ct != ControlType.Edit)
                            {
                                double cx0 = (cr.Left - winL) * 100.0 / winW, cx1 = (cr.Right - winL) * 100.0 / winW, cy0 = (cr.Top - winT) * 100.0 / winH;
                                if (cx1 <= 20 && (cx1 - cx0) <= 19 && cy0 > 10) childToNav = true;
                            }
                        }
                        if (childToNav)
                        {
                            if (navQueue.Count < MaxNavNodes * 2) navQueue.Enqueue(child);
                        }
                        else if (mainQueue.Count < MaxNodes * 2)
                        {
                            mainQueue.Enqueue(child);
                        }
                        child = walker.GetNextSibling(child, cache);
                    }
                    // Cold UIA provider handshake happens on root (nodes == 1); start the subtree budget from here
                    if (nodes == 1) sw.Restart();
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }

            // If a narrow left-aligned layout held the only visible text, promote it rather than returning empty content
            if (docChunks.Count == 0 && mainPlaced.Count == 0 && sidePlaced.Count > 0)
                mainPlaced.AddRange(sidePlaced);

            // Document lines first (already in reading order), then viewport elements sorted top-to-bottom, left-to-right
            for (int i = 0; i < docChunks.Count && snap.MainLines.Count < 220; i++)
                snap.MainLines.Add(docChunks[i]);

            mainPlaced.Sort((a, b) =>
            {
                int rowA = (int)(a.Top / 6.0), rowB = (int)(b.Top / 6.0);
                return rowA != rowB ? rowA.CompareTo(rowB) : a.Left.CompareTo(b.Left);
            });
            for (int i = 0; i < mainPlaced.Count && snap.MainLines.Count < 260; i++)
                snap.MainLines.Add(mainPlaced[i].Text);
        }

        static int AddDocLines(List<string> into, HashSet<string> seen, string doc)
        {
            int chars = 0;
            var lines = doc.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = IngatStructure.CleanLine(lines[i]);
                if (l.Length < 2 || !seen.Add(l)) continue;
                if (l.Length > 600) l = l.Substring(0, 600);
                into.Add(l);
                chars += l.Length;
                if (chars >= MaxChars) break;
            }
            return chars;
        }

        // Chats: every visible line with its horizontal [x0-x1] placement, plus active chat header detection
        static void WalkChat(AutomationElement root, CacheRequest cache, bool browser, double winL, double winT, double winW, double winH, IngatSnapshot snap)
        {
            var lines = new List<PlacedLine>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sw = Stopwatch.StartNew();
            var mainQueue = new Queue<AutomationElement>();
            var sideQueue = new Queue<AutomationElement>();
            mainQueue.Enqueue(root);
            int nodes = 0, sideNodes = 0, chars = 0;
            string headerGuess = "";
            double headerTop = double.MaxValue;
            double minHdrX = winW < 720 ? 6.0 : 24.0;
            var walker = TreeWalker.ControlViewWalker;

            while ((mainQueue.Count > 0 || (sideQueue.Count > 0 && sideNodes < 220)) &&
                   nodes < ChatMaxNodes && (nodes <= 1 || sw.ElapsedMilliseconds < ChatBudgetMs) && chars < ChatMaxChars)
            {
                bool fromSide = mainQueue.Count == 0;
                var el = fromSide ? sideQueue.Dequeue() : mainQueue.Dequeue();
                nodes++;
                if (fromSide) sideNodes++;
                try
                {
                    if (IsPassword(el)) { snap.Secret = true; return; }
                    var type = CtrlType(el);
                    if (type == null || type == ControlType.ScrollBar || type == ControlType.TitleBar || type == ControlType.MenuBar) continue;

                    bool offscreen = IsOffscreen(el);
                    if (offscreen && !(browser && snap.Url.Length == 0 && (type == ControlType.Edit || type == ControlType.ToolBar || type == ControlType.Pane || type == ControlType.Group)))
                        continue;

                    var r = Bounds(el);
                    bool leftList = winW >= 720 && !r.IsEmpty && ((r.Right - winL) * 100.0 / winW) <= 28;

                    if (type == ControlType.Edit)
                    {
                        string v = Value(el);
                        if (browser && snap.Url.Length == 0 && LooksLikeUrl(v)) { snap.Url = NormalizeUrl(v); continue; }
                    }
                    else if (type == ControlType.Text || type == ControlType.Hyperlink || type == ControlType.Header || type == ControlType.ListItem)
                    {
                        var firstChild = type == ControlType.ListItem ? walker.GetFirstChild(el, cache) : null;
                        if (type != ControlType.ListItem || firstChild == null)
                        {
                            string raw = (ElemName(el) ?? "").Trim();
                            if (raw.Length >= 1 && !r.IsEmpty && r.Width > 0 && seen.Add(raw + "@" + (int)r.Top))
                            {
                                string s = raw.Length > 1500 ? raw.Substring(0, 1500) : raw;
                                lines.Add(new PlacedLine { Top = r.Top, Left = r.Left, Right = r.Right, Text = s });
                                chars += s.Length;

                                // Chat header at the top of the active pane (two-pane desktop or narrow single-pane mode)
                                double x0 = (r.Left - winL) * 100.0 / winW;
                                double y0 = (r.Top - winT) * 100.0 / winH;
                                if (x0 >= minHdrX && x0 <= 76 && y0 >= 1.0 && y0 <= 15 && r.Top < headerTop)
                                {
                                    string cand = IngatStructure.CleanLine(s);
                                    if (cand.Length >= 2 && cand.Length <= 55 && IngatChat.Conversation(cand + " - Chat").Length > 0)
                                    {
                                        headerGuess = cand;
                                        headerTop = r.Top;
                                    }
                                }
                            }
                            if (type == ControlType.Text || type == ControlType.ListItem) continue;
                        }
                        else
                        {
                            // ListItem with children: walk its already-fetched firstChild without calling GetFirstChild twice
                            int liCount = 0;
                            int liCap = (fromSide || leftList) ? 45 : 350;
                            var ch = firstChild;
                            while (ch != null && liCount < liCap && sw.ElapsedMilliseconds < ChatBudgetMs)
                            {
                                liCount++;
                                if (fromSide || leftList) { if (sideQueue.Count < 400) sideQueue.Enqueue(ch); }
                                else if (mainQueue.Count < 3500) mainQueue.Enqueue(ch);
                                ch = walker.GetNextSibling(ch, cache);
                            }
                            continue;
                        }
                    }

                    int maxSib = (fromSide || leftList) ? 40 : 400;
                    int cnt = 0;
                    var child = walker.GetFirstChild(el, cache);
                    while (child != null && cnt < maxSib && (nodes == 1 && cnt < 20 || sw.ElapsedMilliseconds < ChatBudgetMs))
                    {
                        cnt++;
                        var cr = Bounds(child);
                        bool childLeft = fromSide || leftList || (winW >= 720 && !cr.IsEmpty && ((cr.Right - winL) * 100.0 / winW) <= 28);
                        if (childLeft) { if (sideQueue.Count < 400) sideQueue.Enqueue(child); }
                        else if (mainQueue.Count < 3500) mainQueue.Enqueue(child);
                        child = walker.GetNextSibling(child, cache);
                    }
                    if (nodes == 1) sw.Restart();
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }

            if (headerGuess.Length > 0 && IngatChat.Conversation(snap.Title).Length == 0)
                snap.Focus = headerGuess;

            lines.Sort((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                int x0 = (int)Math.Max(0, Math.Min(100, (l.Left - winL) * 100.0 / winW));
                int x1 = (int)Math.Max(0, Math.Min(100, (l.Right - winL) * 100.0 / winW));
                string clean = l.Text.Replace("\r", " ").Replace("\n", " / ");
                snap.MainLines.Add("[" + x0 + "-" + x1 + "] " + clean);
            }
        }

        // Plain string entry points kept for callers
        public static string Read(IntPtr hwnd, string exe, out string url, out bool secret)
        {
            var snap = ReadSnapshot(hwnd, exe, "", false);
            url = snap.Url;
            secret = snap.Secret;
            return snap.FormatText(MaxChars);
        }

        public static string ReadChat(IntPtr hwnd, string exe, out string url, out bool secret)
        {
            var snap = ReadSnapshot(hwnd, exe, "", true);
            url = snap.Url;
            secret = snap.Secret;
            return snap.FormatText(ChatMaxChars);
        }

        static int ProcId(AutomationElement el)
        {
            try { return el.Cached.ProcessId; }
            catch (InvalidOperationException) { return el.Current.ProcessId; }
        }

        static ControlType CtrlType(AutomationElement el)
        {
            try { return el.Cached.ControlType; }
            catch (InvalidOperationException) { return el.Current.ControlType; }
        }

        static string ElemName(AutomationElement el)
        {
            try { return el.Cached.Name ?? ""; }
            catch (InvalidOperationException) { return el.Current.Name ?? ""; }
        }

        static bool IsPassword(AutomationElement el)
        {
            try { return el.Cached.IsPassword; }
            catch (InvalidOperationException) { return el.Current.IsPassword; }
        }

        static bool IsOffscreen(AutomationElement el)
        {
            try { return el.Cached.IsOffscreen; }
            catch (InvalidOperationException) { return el.Current.IsOffscreen; }
        }

        static bool HasFocus(AutomationElement el)
        {
            try { return el.Cached.HasKeyboardFocus; }
            catch (InvalidOperationException) { return el.Current.HasKeyboardFocus; }
        }

        static System.Windows.Rect Bounds(AutomationElement el)
        {
            try { return el.Cached.BoundingRectangle; }
            catch (InvalidOperationException) { return el.Current.BoundingRectangle; }
        }

        static bool IsSelectedTab(AutomationElement el)
        {
            object p;
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p))
            {
                try { return ((SelectionItemPattern)p).Current.IsSelected; }
                catch (Exception) { }
            }
            return false;
        }

        static string Value(AutomationElement el)
        {
            object p;
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out p))
            {
                try { return ((ValuePattern)p).Current.Value ?? ""; }
                catch (Exception) { }
            }
            return "";
        }

        // Visible viewport ranges first so scrolled editors/docs return what is actually on screen
        static string DocumentText(AutomationElement el)
        {
            object p;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out p)) return "";
            var tp = (TextPattern)p;
            try
            {
                var ranges = tp.GetVisibleRanges();
                if (ranges != null && ranges.Length > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < ranges.Length && sb.Length < MaxChars; i++)
                    {
                        string part = ranges[i].GetText(MaxChars - sb.Length) ?? "";
                        if (part.Length == 0) continue;
                        if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
                        sb.Append(part);
                    }
                    string vis = sb.ToString().Trim();
                    if (vis.Length > 0) return vis.Length > MaxChars ? vis.Substring(0, MaxChars) : vis;
                }
            }
            catch (Exception) { }
            try { return tp.DocumentRange.GetText(MaxChars) ?? ""; }
            catch (Exception) { return ""; }
        }

        static bool LooksLikeUrl(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length > 2048 || v.IndexOf(' ') >= 0) return false;
            if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;
            int dot = v.IndexOf('.');
            return dot > 0 && dot < v.Length - 2;
        }

        static string NormalizeUrl(string v)
        {
            v = (v ?? "").Trim();
            return v.Length > 0 && !v.Contains("://") ? "https://" + v : v;
        }

        // Window capture & perceptual hashing

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

        // Fast LockBits dual-zone 64-bit dHash: full-window horizontal dHash + inner-viewport vertical dHash (catches scrolling)
        public static IngatStamp Stamp(Bitmap b)
        {
            if (b == null || b.Width < 16 || b.Height < 16) return new IngatStamp();
            using (var small = new Bitmap(17, 9, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    // HighQualityBilinear area-averages all source pixels instead of point-sampling 2x2
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(b, new Rectangle(0, 0, 9, 8), new Rectangle(0, 0, b.Width, b.Height), GraphicsUnit.Pixel);
                    int ix = (int)(b.Width * 0.15), iy = (int)(b.Height * 0.12);
                    int iw = Math.Max(8, (int)(b.Width * 0.80)), ih = Math.Max(8, (int)(b.Height * 0.80));
                    g.DrawImage(b, new Rectangle(9, 0, 8, 9), new Rectangle(ix, iy, iw, ih), GraphicsUnit.Pixel);
                }
                var data = small.LockBits(new Rectangle(0, 0, 17, 9), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    byte[] raw = new byte[stride * 9];
                    Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                    ulong full = 0, inner = 0;
                    int bit = 0;
                    for (int y = 0; y < 8; y++)
                    {
                        int row = y * stride;
                        int nextRow = (y + 1) * stride;
                        for (int x = 0; x < 8; x++, bit++)
                        {
                            int p0 = row + x * 3, p1 = p0 + 3;
                            if (raw[p0] + raw[p0 + 1] + raw[p0 + 2] > raw[p1] + raw[p1 + 1] + raw[p1 + 2])
                                full |= 1UL << bit;
                            int q0 = row + (9 + x) * 3, q1 = nextRow + (9 + x) * 3;
                            if (raw[q0] + raw[q0 + 1] + raw[q0 + 2] > raw[q1] + raw[q1 + 1] + raw[q1 + 2])
                                inner |= 1UL << bit;
                        }
                    }
                    return new IngatStamp { Full = full, Inner = inner, Valid = true };
                }
                finally { small.UnlockBits(data); }
            }
        }

        public static ulong Hash(Bitmap b) { return Stamp(b).Full; }

        public static int Distance(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int n = 0;
            while (x != 0) { x &= x - 1; n++; }
            return n;
        }

        public static bool Changed(IngatStamp prev, IngatStamp cur)
        {
            if (!prev.Valid || !cur.Valid) return true;
            return Distance(prev.Full, cur.Full) > 4 || Distance(prev.Inner, cur.Inner) > 4;
        }

        // In-memory OCR with horizontal positions: no disk files
        public static string OcrPlaced(Bitmap b)
        {
            if (b == null || !Deskmate.Ocr.Available) return "";
            try
            {
                string text = Deskmate.Ocr.ReadPlaced(b) ?? "";
                return text.Length > ChatMaxChars ? text.Substring(text.Length - ChatMaxChars) : text;
            }
            catch (Exception ex) { Log.Write("ingat ocr: " + ex.Message); return ""; }
        }

        public static string Ocr(Bitmap b)
        {
            if (b == null || !Deskmate.Ocr.Available) return "";
            try
            {
                string text = Deskmate.Ocr.Read(b) ?? "";
                return text.Length > MaxChars ? text.Substring(0, MaxChars) : text;
            }
            catch (Exception ex) { Log.Write("ingat ocr: " + ex.Message); return ""; }
        }
    }
}
