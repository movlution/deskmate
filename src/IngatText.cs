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
        static readonly string[] Terminals = { "windowsterminal", "wt", "powershell", "pwsh", "cmd", "wezterm-gui", "alacritty", "mintty", "git-bash" };
        static readonly string[] CodeEditors = { "code", "code - insiders", "cursor", "windsurf", "zed", "devenv", "notepad", "notepad++", "sublime_text" };

        public static bool IsBrowser(string exe) { return Array.IndexOf(Browsers, (exe ?? "").ToLowerInvariant()) >= 0; }
        static bool IsTerminal(string exe) { return Array.IndexOf(Terminals, (exe ?? "").ToLowerInvariant()) >= 0; }
        static bool IsCodeEditor(string exe) { return Array.IndexOf(CodeEditors, (exe ?? "").ToLowerInvariant()) >= 0; }

        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr hwnd);

        // Chromium keeps Incognito and Guest out of the window title ("Page - Google Chrome") but names its
        // BrowserRootView pane "Page - Google Chrome (Incognito)" / "(Guest)", in the UI language. Other profiles
        // use " - Person 1" instead, so a parenthesized tail the title lacks means a private window.
        // A window never changes mode, so the answer is kept per handle.
        static readonly Dictionary<IntPtr, bool> chromiumPrivate = new Dictionary<IntPtr, bool>();

        public static bool ChromiumPrivate(IntPtr hwnd, string title, string exe)
        {
            if (hwnd == IntPtr.Zero) return false;
            // Check window class before cache so a closed non-Chrome window's reused HWND never masks a new Incognito window
            var cls = new StringBuilder(64);
            Native.GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() != "Chrome_WidgetWin_1") return false;
            lock (chromiumPrivate)
            {
                bool known;
                if (chromiumPrivate.TryGetValue(hwnd, out known)) return known;
            }
            if (IsHungAppWindow(hwnd)) return false;
            string rootName;
            bool settled;
            try
            {
                var win = AutomationElement.FromHandle(hwnd);
                if (win == null) return false;
                var view = win.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "BrowserRootView"));
                rootName = view == null ? "" : view.Current.Name ?? "";
                // Electron apps share the window class but never have a browser root view; a browser window that has
                // not named its view yet is asked again on the next look instead of being remembered as not private
                settled = view == null ? !IsBrowser(exe) : rootName.Length > 0;
            }
            catch (Exception) { return false; }
            bool result = PrivateRootName(rootName, title);
            if (!settled) return result;
            lock (chromiumPrivate)
            {
                if (chromiumPrivate.Count > 256) chromiumPrivate.Clear();
                chromiumPrivate[hwnd] = result;
            }
            return result;
        }

        // "Page - Google Chrome (Incognito)" against the title "Page - Google Chrome"
        public static bool PrivateRootName(string rootName, string title)
        {
            string r = (rootName ?? "").TrimEnd(), t = (title ?? "").TrimEnd();
            if (!r.EndsWith(")", StringComparison.Ordinal) || t.EndsWith(")", StringComparison.Ordinal)) return false;
            int open = r.LastIndexOf(" (", StringComparison.Ordinal);
            return open > 0 && r.Length - open <= 40;
        }

        struct PlacedLine
        {
            public double Top;
            public double Left;
            public double Right;
            public string Text;
            public bool Inline;
        }

        static CacheRequest NewCache()
        {
            // Element-only scope with lean core properties: avoids per-node pattern provider probes on every container/button
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

        // Shallow container cache: fetches a Window/Pane and its direct children in ONE batched COM call
        static CacheRequest NewChildrenCache()
        {
            var cr = NewCache();
            cr.TreeScope = TreeScope.Element | TreeScope.Children;
            return cr;
        }

        // Single-element cache for FocusedElement: fetches draft/value patterns in one IPC without taxing tree walks
        static CacheRequest NewFocusedCache()
        {
            var cr = NewCache();
            cr.Add(AutomationElement.IsKeyboardFocusableProperty);
            cr.Add(AutomationElement.RuntimeIdProperty);
            cr.Add(ValuePattern.Pattern);
            cr.Add(ValuePattern.ValueProperty);
            cr.Add(ValuePattern.IsReadOnlyProperty);
            return cr;
        }

        static bool IsLeafDecorative(ControlType type, System.Windows.Rect r)
        {
            if (type == ControlType.Image || type == ControlType.Separator || type == ControlType.Thumb ||
                type == ControlType.ProgressBar || type == ControlType.Slider || type == ControlType.Spinner ||
                type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.ToolTip)
                return true;
            // Small icon/toolbar buttons rarely hold distinct document text; large card buttons are still walked
            if (type == ControlType.Button && !r.IsEmpty && r.Width <= 140 && r.Height <= 48)
                return true;
            return false;
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
            var childrenCache = NewChildrenCache();
            AutomationElement root;
            try
            {
                using (childrenCache.Activate()) root = AutomationElement.FromHandle(hwnd);
            }
            catch (Exception) { snap.Source = "window"; return snap; }
            if (root == null) { snap.Source = "window"; return snap; }

            bool browser = IsBrowser(exe);
            bool terminal = IsTerminal(exe);
            bool fastDocApp = terminal || IsCodeEditor(exe);

            // Focused control first: instant password check, draft read, and active editor/terminal/document text
            string focusedDoc = "";
            try
            {
                AutomationElement focused;
                using (NewFocusedCache().Activate()) focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    if (IsPassword(focused)) { snap.ClearSecret(); return snap; }
                    snap.HasDraftCheck = true;
                    if (ProcId(focused) == winPid)
                    {
                        var ftype = CtrlType(focused);
                        string rawFname = ElemName(focused);
                        string fname = IngatStructure.CleanLine(rawFname);
                        // In chat mode, WalkChat detects the conversation header; avoid treating the message box label as Focus
                        if (!chatMode && fname.Length >= 2 && fname.Length <= 140 && !LooksLikeUrl(fname, false))
                            snap.Focus = fname;
                        if (browser && snap.Url.Length == 0 && ftype == ControlType.Document)
                        {
                            string dv = Value(focused);
                            if (LooksLikeUrl(dv, true)) snap.Url = NormalizeUrl(dv);
                        }
                        if (!chatMode && (ftype == ControlType.Document || ftype == ControlType.Edit || ftype == ControlType.Text || ftype == ControlType.Custom))
                        {
                            string lowName = rawFname.ToLowerInvariant();
                            bool isSearchOrUrl = lowName.Contains("address") || lowName.Contains("alamat") || lowName.Contains("omnibox");
                            if (!isSearchOrUrl) focusedDoc = DocumentText(focused);
                        }
                        // Capture active draft from the same focused element so Capture skips a second FocusedElement IPC
                        bool draftSecret;
                        string draftField;
                        string draft = IngatChat.DraftFromElement(focused, winPid, out draftSecret, out draftField);
                        if (draftSecret) { snap.ClearSecret(); return snap; }
                        snap.Draft = draft;
                        snap.DraftField = draftField;
                    }
                }
            }
            catch (Exception) { }

            if (chatMode)
            {
                WalkChat(root, cache, childrenCache, browser, winL, winT, winW, winH, snap);
                if (snap.Secret) snap.ClearSecret();
                return snap;
            }

            WalkStructured(root, cache, childrenCache, browser, terminal, fastDocApp, hasRect, winL, winT, winW, winH, focusedDoc, snap);
            if (snap.Secret) { snap.ClearSecret(); return snap; }
            string mainText = snap.Excerpt(3500);
            snap.Kind = IngatStructure.Classify(exe, snap.Url, title, mainText.Length > 1200 ? mainText.Substring(0, 1200) : mainText);
            var anchors = IngatStructure.ExtractAnchors(title, snap.Url, (snap.Focus.Length > 0 ? snap.Focus + "\n" : "") + mainText, 12);
            for (int i = 0; i < anchors.Count; i++) IngatContext.AddUnique(snap.Anchors, anchors[i], 12);
            return snap;
        }

        static bool LooksLikeAddressBar(AutomationElement el, double y0Pct)
        {
            if (y0Pct <= 18) return true;
            string n = ElemName(el).ToLowerInvariant();
            return n.Contains("address") || n.Contains("alamat") || n.Contains("url") || n.Contains("omnibox") || n.Contains("location");
        }

        static AutomationElementCollection BatchChildren(AutomationElement el, bool alreadyCached, CacheRequest childrenCache)
        {
            try
            {
                if (alreadyCached) return el.CachedChildren;
                return el.GetUpdatedCache(childrenCache).CachedChildren;
            }
            catch (Exception) { return null; }
        }

        static void WalkStructured(AutomationElement root, CacheRequest cache, CacheRequest childrenCache, bool browser, bool terminal, bool fastDocApp, bool hasRect,
            double winL, double winT, double winW, double winH, string focusedDoc, IngatSnapshot snap)
        {
            var mainPlaced = new List<PlacedLine>();
            var sidePlaced = new List<PlacedLine>();
            var docChunks = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (focusedDoc.Length > 0) AddDocLines(docChunks, seen, focusedDoc);

            // Fast-path: when a focused editor/terminal already delivered rich live text, trim subtree walk to grab tabs/headings quickly
            int nodeLimit = fastDocApp && focusedDoc.Length >= 400 ? 75 : MaxNodes;
            int budgetMs = fastDocApp && focusedDoc.Length >= 400 ? 110 : BudgetMs;

            var sw = Stopwatch.StartNew();
            // Main viewport nodes are always drained before sidebars/trees/tabs so large file trees never starve content
            var mainQueue = new Queue<AutomationElement>();
            var navQueue = new Queue<AutomationElement>();
            mainQueue.Enqueue(root);
            int nodes = 0, navNodes = 0, chars = focusedDoc.Length;
            var walker = TreeWalker.ControlViewWalker;

            bool navWanted = true;
            while ((mainQueue.Count > 0 || (navWanted && navQueue.Count > 0 && navNodes < MaxNavNodes)) &&
                   nodes < nodeLimit && (nodes <= 2 || sw.ElapsedMilliseconds < budgetMs) && chars < MaxChars)
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
                    if (IsLeafDecorative(type, r)) continue;

                    double x0Pct = hasRect && !r.IsEmpty ? (r.Left - winL) * 100.0 / winW : 50;
                    double x1Pct = hasRect && !r.IsEmpty ? (r.Right - winL) * 100.0 / winW : 50;
                    double y0Pct = hasRect && !r.IsEmpty ? (r.Top - winT) * 100.0 / winH : 50;
                    bool sideArea = hasRect && !r.IsEmpty && x1Pct <= 20 && (x1Pct - x0Pct) <= 19 && y0Pct > 10;

                    if (type == ControlType.Edit)
                    {
                        string v = Value(el);
                        if (browser && snap.Url.Length == 0 && LooksLikeUrl(v, LooksLikeAddressBar(el, y0Pct))) { snap.Url = NormalizeUrl(v); continue; }
                        string cv = IngatStructure.CleanLine(v);
                        if (cv.Length >= 2 && seen.Add(cv))
                        {
                            mainPlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Right = r.IsEmpty ? 0 : r.Right, Text = cv });
                            chars += cv.Length;
                        }
                    }
                    else if (type == ControlType.Document)
                    {
                        if (browser && snap.Url.Length == 0)
                        {
                            string dv = Value(el);
                            if (LooksLikeUrl(dv, true)) snap.Url = NormalizeUrl(dv);
                        }
                        string doc = DocumentText(el);
                        if (doc.Length > 0)
                        {
                            chars += AddDocLines(docChunks, seen, doc);
                            continue;
                        }
                        string dn = IngatStructure.CleanLine(ElemName(el));
                        if (dn.Length >= 2 && seen.Add(dn))
                        {
                            mainPlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Right = r.IsEmpty ? 0 : r.Right, Text = dn });
                            chars += dn.Length;
                        }
                    }
                    else if (terminal && docChunks.Count == 0 && (type == ControlType.Custom || type == ControlType.Pane))
                    {
                        string tdoc = DocumentText(el);
                        if (tdoc.Length > 0)
                        {
                            chars += AddDocLines(docChunks, seen, tdoc);
                            continue;
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
                                    sidePlaced.Add(new PlacedLine { Top = r.IsEmpty ? 1e6 + nodes : r.Top, Left = r.IsEmpty ? 0 : r.Left, Right = r.IsEmpty ? 0 : r.Right, Text = capped });
                            }
                            else
                            {
                                if (y0Pct >= 4 && y0Pct <= 18 && s.Length <= 110 && snap.Headings.Count < 6 && type == ControlType.Text)
                                    IngatContext.AddUnique(snap.Headings, s, 8);
                                mainPlaced.Add(new PlacedLine
                                {
                                    Top = r.IsEmpty ? 1e6 + nodes : r.Top,
                                    Left = r.IsEmpty ? 0 : r.Left,
                                    Right = r.IsEmpty ? 0 : r.Right,
                                    Text = capped,
                                    Inline = type == ControlType.Text || type == ControlType.Hyperlink,
                                });
                                chars += capped.Length;
                            }
                        }
                        if (type == ControlType.Text) continue;
                    }

                    if (type == ControlType.ToolBar && (!browser || snap.Url.Length > 0)) continue;
                    bool navParent = fromNav || sideArea || type == ControlType.Tree || type == ControlType.Tab || type == ControlType.StatusBar;
                    int maxSiblings = navParent ? 24 : 320;
                    // Batch-read direct children of root and shallow Panes in one COM call instead of N sequential GetNextSibling IPCs
                    AutomationElementCollection batched = null;
                    if (nodes == 1) batched = BatchChildren(el, true, childrenCache);
                    else if (!navParent && type == ControlType.Pane && nodes <= 6) batched = BatchChildren(el, false, childrenCache);

                    if (batched != null)
                    {
                        if (nodes == 1) sw.Restart();
                        int lim = Math.Min(batched.Count, maxSiblings);
                        for (int ci = 0; ci < lim; ci++)
                        {
                            var child = batched[ci];
                            if (IsPassword(child)) { snap.Secret = true; return; }
                            var ct = CtrlType(child);
                            var cr = Bounds(child);
                            if (IsLeafDecorative(ct, cr)) continue;
                            bool childToNav = navParent || ct == ControlType.Tree || ct == ControlType.TreeItem || ct == ControlType.Tab || ct == ControlType.TabItem || ct == ControlType.StatusBar;
                            if (!childToNav && hasRect && !cr.IsEmpty && ct != ControlType.Document && ct != ControlType.Edit)
                            {
                                double cx0 = (cr.Left - winL) * 100.0 / winW, cx1 = (cr.Right - winL) * 100.0 / winW, cy0 = (cr.Top - winT) * 100.0 / winH;
                                if (cx1 <= 20 && (cx1 - cx0) <= 19 && cy0 > 10) childToNav = true;
                            }
                            if (childToNav)
                            {
                                if (navQueue.Count < MaxNavNodes * 2) navQueue.Enqueue(child);
                            }
                            else if (mainQueue.Count < MaxNodes * 2)
                            {
                                mainQueue.Enqueue(child);
                            }
                        }
                    }
                    else
                    {
                        int count = 0;
                        var child = walker.GetFirstChild(el, cache);
                        // Cold UIA provider handshake happens on root's first child fetch; start the subtree budget from here
                        if (nodes == 1) sw.Restart();
                        while (child != null && count < maxSiblings && sw.ElapsedMilliseconds < BudgetMs)
                        {
                            count++;
                            var ct = CtrlType(child);
                            var cr = Bounds(child);
                            if (!IsLeafDecorative(ct, cr))
                            {
                                bool childToNav = navParent || ct == ControlType.Tree || ct == ControlType.TreeItem || ct == ControlType.Tab || ct == ControlType.TabItem || ct == ControlType.StatusBar;
                                if (!childToNav && hasRect && !cr.IsEmpty && ct != ControlType.Document && ct != ControlType.Edit)
                                {
                                    double cx0 = (cr.Left - winL) * 100.0 / winW, cx1 = (cr.Right - winL) * 100.0 / winW, cy0 = (cr.Top - winT) * 100.0 / winH;
                                    if (cx1 <= 20 && (cx1 - cx0) <= 19 && cy0 > 10) childToNav = true;
                                }
                                if (childToNav)
                                {
                                    if (navQueue.Count < MaxNavNodes * 2) navQueue.Enqueue(child);
                                }
                                else if (mainQueue.Count < MaxNodes * 2)
                                {
                                    mainQueue.Enqueue(child);
                                }
                            }
                            child = walker.GetNextSibling(child, cache);
                        }
                    }
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
            // Join tight inline HTML/Electron spans on the same line (gap <= 14px) while keeping separate columns/panes intact
            for (int i = 0; i < mainPlaced.Count && snap.MainLines.Count < 260; i++)
            {
                var cur = mainPlaced[i];
                if (cur.Inline && cur.Right > cur.Left)
                {
                    var line = new StringBuilder(cur.Text);
                    double right = cur.Right;
                    double top = cur.Top;
                    while (i + 1 < mainPlaced.Count)
                    {
                        var nxt = mainPlaced[i + 1];
                        if (!nxt.Inline || nxt.Right <= nxt.Left || Math.Abs(nxt.Top - top) > 6.0) break;
                        double gap = nxt.Left - right;
                        if (gap < -4.0 || gap > 14.0 || line.Length + 1 + nxt.Text.Length > 400) break;
                        line.Append(' ').Append(nxt.Text);
                        right = Math.Max(right, nxt.Right);
                        i++;
                    }
                    snap.MainLines.Add(line.ToString());
                }
                else
                {
                    snap.MainLines.Add(cur.Text);
                }
            }
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
        static void WalkChat(AutomationElement root, CacheRequest cache, CacheRequest childrenCache, bool browser, double winL, double winT, double winW, double winH, IngatSnapshot snap)
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
                    if (IsLeafDecorative(type, r)) continue;
                    bool leftList = winW >= 720 && !r.IsEmpty && ((r.Right - winL) * 100.0 / winW) <= 28;
                    double y0Pct = !r.IsEmpty ? (r.Top - winT) * 100.0 / winH : 50;

                    if (type == ControlType.Edit)
                    {
                        string v = Value(el);
                        if (browser && snap.Url.Length == 0 && LooksLikeUrl(v, LooksLikeAddressBar(el, y0Pct))) { snap.Url = NormalizeUrl(v); continue; }
                    }
                    else if (type == ControlType.Document)
                    {
                        if (browser && snap.Url.Length == 0)
                        {
                            string dv = Value(el);
                            if (LooksLikeUrl(dv, true)) snap.Url = NormalizeUrl(dv);
                        }
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
                    AutomationElementCollection batched = nodes == 1 ? BatchChildren(el, true, childrenCache) : null;
                    if (batched != null)
                    {
                        sw.Restart();
                        int lim = Math.Min(batched.Count, maxSib);
                        for (int ci = 0; ci < lim; ci++)
                        {
                            var child = batched[ci];
                            if (IsPassword(child)) { snap.Secret = true; return; }
                            var ct = CtrlType(child);
                            var cr = Bounds(child);
                            if (IsLeafDecorative(ct, cr)) continue;
                            bool childLeft = fromSide || leftList || (winW >= 720 && !cr.IsEmpty && ((cr.Right - winL) * 100.0 / winW) <= 28);
                            if (childLeft) { if (sideQueue.Count < 400) sideQueue.Enqueue(child); }
                            else if (mainQueue.Count < 3500) mainQueue.Enqueue(child);
                        }
                    }
                    else
                    {
                        int cnt = 0;
                        var child = walker.GetFirstChild(el, cache);
                        if (nodes == 1) sw.Restart();
                        while (child != null && cnt < maxSib && sw.ElapsedMilliseconds < ChatBudgetMs)
                        {
                            cnt++;
                            var ct = CtrlType(child);
                            var cr = Bounds(child);
                            if (!IsLeafDecorative(ct, cr))
                            {
                                bool childLeft = fromSide || leftList || (winW >= 720 && !cr.IsEmpty && ((cr.Right - winL) * 100.0 / winW) <= 28);
                                if (childLeft) { if (sideQueue.Count < 400) sideQueue.Enqueue(child); }
                                else if (mainQueue.Count < 3500) mainQueue.Enqueue(child);
                            }
                            child = walker.GetNextSibling(child, cache);
                        }
                    }
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
            if (el.TryGetCachedPattern(SelectionItemPattern.Pattern, out p))
            {
                try { return ((SelectionItemPattern)p).Cached.IsSelected; }
                catch (Exception) { }
            }
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
            if (el.TryGetCachedPattern(ValuePattern.Pattern, out p))
            {
                try { return ((ValuePattern)p).Cached.Value ?? ""; }
                catch (Exception) { }
            }
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out p))
            {
                try { return ((ValuePattern)p).Current.Value ?? ""; }
                catch (Exception) { }
            }
            return "";
        }

        // Visible viewport ranges first, then caret/selection window, then document head so scrolled editors return active code
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
            try
            {
                var sel = tp.GetSelection();
                if (sel != null && sel.Length > 0 && sel[0] != null)
                {
                    var win = sel[0].Clone();
                    win.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start, System.Windows.Automation.Text.TextUnit.Line, -25);
                    win.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, System.Windows.Automation.Text.TextUnit.Line, 35);
                    string around = (win.GetText(MaxChars) ?? "").Trim();
                    if (around.Length > 0) return around.Length > MaxChars ? around.Substring(0, MaxChars) : around;
                }
            }
            catch (Exception) { }
            try { return tp.DocumentRange.GetText(MaxChars) ?? ""; }
            catch (Exception) { return ""; }
        }

        static readonly HashSet<string> FileExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs", "ts", "tsx", "js", "jsx", "py", "rs", "go", "java", "kt", "cpp", "c", "h", "hpp",
            "json", "yaml", "yml", "toml", "xml", "css", "scss", "sql", "ps1", "sh", "cmd", "bat",
            "md", "txt", "log", "pdf", "docx", "xlsx", "pptx", "png", "jpg", "jpeg", "gif", "svg", "exe", "dll",
        };

        public static bool LooksLikeUrl(string v, bool allowBareHost)
        {
            if (string.IsNullOrEmpty(v) || v.Length > 2048 || v.IndexOf(' ') >= 0 || v.IndexOf('\n') >= 0) return false;
            if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                v.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;
            if (!allowBareHost) return false;
            if (v.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) &&
                (v.Length == 9 || v[9] == ':' || v[9] == '/'))
                return true;
            int slash = v.IndexOf('/');
            string host = slash > 0 ? v.Substring(0, slash) : v;
            int colon = host.IndexOf(':');
            if (colon > 0) host = host.Substring(0, colon);
            int dot = host.LastIndexOf('.');
            if (dot <= 0 || dot >= host.Length - 2) return false;
            string tld = host.Substring(dot + 1);
            if (slash < 0 && FileExts.Contains(tld)) return false;
            for (int i = 0; i < tld.Length; i++) if (!char.IsLetterOrDigit(tld[i])) return false;
            return true;
        }

        static string NormalizeUrl(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length == 0 || v.Contains("://")) return v;
            return v.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || v.StartsWith("127.0.0.1", StringComparison.Ordinal)
                ? "http://" + v : "https://" + v;
        }

        // Window capture & perceptual hashing

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT val, int size);

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

        static readonly object stampGate = new object();
        static Bitmap stampScratch;
        static Bitmap screenThumb;
        static byte[] stampRaw;

        // Fast LockBits dual-zone 64-bit dHash: full-window horizontal dHash + inner-viewport vertical dHash (catches scrolling)
        public static IngatStamp Stamp(Bitmap b)
        {
            if (b == null || b.Width < 16 || b.Height < 16) return new IngatStamp();
            lock (stampGate)
            {
                if (stampScratch == null) stampScratch = new Bitmap(17, 9, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(stampScratch))
                {
                    // HighQualityBilinear area-averages all source pixels instead of point-sampling 2x2
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(b, new Rectangle(0, 0, 9, 8), new Rectangle(0, 0, b.Width, b.Height), GraphicsUnit.Pixel);
                    int ix = (int)(b.Width * 0.15), iy = (int)(b.Height * 0.12);
                    int iw = Math.Max(8, (int)(b.Width * 0.80)), ih = Math.Max(8, (int)(b.Height * 0.80));
                    g.DrawImage(b, new Rectangle(9, 0, 8, 9), new Rectangle(ix, iy, iw, ih), GraphicsUnit.Pixel);
                }
                var data = stampScratch.LockBits(new Rectangle(0, 0, 17, 9), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = Math.Abs(data.Stride);
                    int need = stride * 9;
                    if (stampRaw == null || stampRaw.Length < need) stampRaw = new byte[need];
                    Marshal.Copy(data.Scan0, stampRaw, 0, need);
                    ulong full = 0, inner = 0;
                    int bit = 0;
                    for (int y = 0; y < 8; y++)
                    {
                        int row = y * stride;
                        int nextRow = (y + 1) * stride;
                        for (int x = 0; x < 8; x++, bit++)
                        {
                            int p0 = row + x * 3, p1 = p0 + 3;
                            if (stampRaw[p0] + stampRaw[p0 + 1] + stampRaw[p0 + 2] > stampRaw[p1] + stampRaw[p1 + 1] + stampRaw[p1 + 2])
                                full |= 1UL << bit;
                            int q0 = row + (9 + x) * 3, q1 = nextRow + (9 + x) * 3;
                            if (stampRaw[q0] + stampRaw[q0 + 1] + stampRaw[q0 + 2] > stampRaw[q1] + stampRaw[q1 + 1] + stampRaw[q1 + 2])
                                inner |= 1UL << bit;
                        }
                    }
                    return new IngatStamp { Full = full, Inner = inner, Valid = true };
                }
                finally { stampScratch.UnlockBits(data); }
            }
        }

        // Same fingerprint for the foreground window, read off the composed screen into a reusable 160x90 thumbnail:
        // no window-sized bitmap and no PrintWindow re-render of the target app on every look.
        // Invalid when the window is not in front (something else may cover it) or is partly off-screen.
        public static IngatStamp ScreenStamp(IntPtr hwnd)
        {
            RECT r;
            if (hwnd == IntPtr.Zero || hwnd != Native.GetForegroundWindow() || IsIconic(hwnd)) return new IngatStamp();
            // Visible frame (DWMWA_EXTENDED_FRAME_BOUNDS): GetWindowRect adds invisible resize borders that
            // stick out past the screen on maximized windows
            if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(hwnd, out r)) return new IngatStamp();
            var bounds = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
            if (bounds.Width < 200 || bounds.Height < 120 || !System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds)) return new IngatStamp();
            lock (stampGate)
            {
                if (screenThumb == null) screenThumb = new Bitmap(160, 90, PixelFormat.Format24bppRgb);
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) return new IngatStamp();
                bool ok;
                try
                {
                    using (var g = Graphics.FromImage(screenThumb))
                    {
                        IntPtr dc = g.GetHdc();
                        try
                        {
                            // COLORONCOLOR samples instead of averaging: measured ~19 ms vs ~25 ms for HALFTONE at 1080p;
                            // the 160x90 grid is then area-averaged by Stamp, as with a full shot
                            Native.SetStretchBltMode(dc, 3);
                            ok = Native.StretchBlt(dc, 0, 0, 160, 90, screen, r.L, r.T, bounds.Width, bounds.Height, 0x00CC0020);
                        }
                        finally { g.ReleaseHdc(dc); }
                    }
                }
                finally { Native.ReleaseDC(IntPtr.Zero, screen); }
                return ok ? Stamp(screenThumb) : new IngatStamp();
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
            return Distance(prev.Full, cur.Full) > 4 || Distance(prev.Inner, cur.Inner) > 3;
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

        // Viewport-cropped OCR for hybrid apps whose UIA exposed sidebar/toolbar chrome but an empty/canvas main area
        public static string OcrViewport(Bitmap b, bool cropSidebar)
        {
            if (b == null || !Deskmate.Ocr.Available) return "";
            if (!cropSidebar || b.Width < 500 || b.Height < 300) return OcrPlaced(b);
            int x = (int)(b.Width * 0.18), y = (int)(b.Height * 0.08);
            int w = b.Width - x, h = b.Height - y;
            if (w < 240 || h < 160) return OcrPlaced(b);
            try
            {
                using (var crop = b.Clone(new Rectangle(x, y, w, h), b.PixelFormat))
                    return OcrPlaced(crop);
            }
            catch (Exception) { return OcrPlaced(b); }
        }

        // True when another window is visibly side-by-side with the active window (split-screen, <55% occluded)
        public static bool IsSideBySide(Rectangle active, Rectangle other)
        {
            if (active.Width < 200 || active.Height < 120 || other.Width < 320 || other.Height < 240) return false;
            var inter = Rectangle.Intersect(active, other);
            long otherArea = (long)other.Width * other.Height;
            long overlap = inter.IsEmpty ? 0L : (long)inter.Width * inter.Height;
            return overlap * 100L < otherArea * 55L;
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
