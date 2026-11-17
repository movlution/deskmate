using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Deskmate
{
    // Menus opened from Deskmate's own windows never take focus: the dock, island and
    // sheets are WS_EX_NOACTIVATE and answer WM_MOUSEACTIVATE with MA_NOACTIVATE.
    // WinForms closes a drop-down when it loses activation, so with no activation
    // there is nothing to lose and the menu would sit there forever. While such a
    // menu is open a low-level mouse hook watches for a click landing outside it and
    // closes it. The click is not swallowed, so the window under it still gets it.
    static class MenuGuard
    {
        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207, WM_NCLBUTTONDOWN = 0xA1;

        delegate IntPtr HookProc(int code, IntPtr msg, IntPtr info);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public POINT Pt; public int MouseData; public int Flags; public int Time; public IntPtr Extra; }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr msg, IntPtr info);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        static IntPtr hook = IntPtr.Zero;
        static HookProc proc;
        static ToolStripDropDown watched;
        static bool closing;
        static Form watchedPopup;
        static Func<Point, bool> popupInside;
        static Action popupClose;
        static bool popupClosing;
        static bool popupDry;

        static void EnsureHook()
        {
            if (hook != IntPtr.Zero || (watched == null && popupDry)) return;
            if (proc == null) proc = Callback;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Log.Write("menu guard: hook failed, menu may stay open");
        }

        static void ReleaseHookIfIdle()
        {
            if (watched != null || (watchedPopup != null && !popupDry)) return;
            proc = null;
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        // Only one of Deskmate's menus is ever up, so a new one replaces the last
        internal static void Watch(ToolStripDropDown menu)
        {
            StopMenu();
            if (menu == null) { ReleaseHookIfIdle(); return; }
            watched = menu;
            menu.Closed += MenuClosed;
            EnsureHook();
        }

        static void MenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (ReferenceEquals(watched, sender)) { StopMenu(); ReleaseHookIfIdle(); }
        }

        internal static void Watch(Form popup, Func<Point, bool> inside, Action close, bool dry)
        {
            StopPopup();
            if (popup == null || close == null) { ReleaseHookIfIdle(); return; }
            watchedPopup = popup;
            popupInside = inside;
            popupClose = close;
            popupDry = dry;
            popup.FormClosed += delegate { if (ReferenceEquals(watchedPopup, popup)) Unwatch(popup); };
            EnsureHook();
        }

        internal static void Unwatch(Form popup)
        {
            if (!ReferenceEquals(watchedPopup, popup)) return;
            StopPopup();
            ReleaseHookIfIdle();
        }

        static void StopMenu()
        {
            if (watched != null) watched.Closed -= MenuClosed;
            watched = null;
            closing = false;
        }

        static void StopPopup()
        {
            watchedPopup = null;
            popupInside = null;
            popupClose = null;
            popupClosing = false;
            popupDry = false;
        }

        static IntPtr Callback(int code, IntPtr msg, IntPtr info)
        {
            if (code >= 0 && (watched != null || watchedPopup != null))
            {
                int id = msg.ToInt32();
                if (id == WM_LBUTTONDOWN || id == WM_RBUTTONDOWN || id == WM_MBUTTONDOWN || id == WM_NCLBUTTONDOWN)
                {
                    var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(info, typeof(MSLLHOOKSTRUCT));
                    Down(new Point(data.Pt.X, data.Pt.Y));
                }
            }
            return CallNextHookEx(hook, code, msg, info);
        }

        static bool MenuContains(ToolStripDropDown menu, Point at)
        {
            if (menu.Bounds.Contains(at)) return true;
            foreach (ToolStripItem item in menu.Items)
            {
                var sub = item as ToolStripDropDownItem;
                if (sub != null && sub.HasDropDownItems && sub.DropDown.Visible && MenuContains(sub.DropDown, at))
                    return true;
            }
            return false;
        }

        // A press outside the menu or popup closes it; a press inside is its own business
        internal static void Down(Point at)
        {
            var m = watched;
            if (m != null && !closing && !m.IsDisposed && m.Visible && !MenuContains(m, at))
            {
                closing = true;
                try
                {
                    m.BeginInvoke(new Action(delegate
                    {
                        try { if (!m.IsDisposed) m.Close(ToolStripDropDownCloseReason.AppClicked); }
                        catch (Exception ex) { Log.Write("menu guard close: " + ex.Message); }
                    }));
                }
                catch (Exception ex) { closing = false; Log.Write("menu guard: " + ex.Message); }
            }
            var p = watchedPopup;
            var keep = popupInside;
            var closeAct = popupClose;
            bool dry = popupDry;
            if (p != null && closeAct != null && !popupClosing && !p.IsDisposed && p.Visible)
            {
                bool inside = false;
                try { inside = keep != null ? keep(at) : p.Bounds.Contains(at); }
                catch (Exception) { inside = false; }
                if (!inside)
                {
                    popupClosing = true;
                    try
                    {
                        if (dry && !p.InvokeRequired) closeAct();
                        else p.BeginInvoke(new Action(delegate { try { closeAct(); } catch (Exception ex) { Log.Write("popup guard close: " + ex.Message); } }));
                    }
                    catch (Exception ex) { popupClosing = false; Log.Write("popup guard: " + ex.Message); }
                }
            }
        }
    }
    sealed class MenuItemMeta
    {
        public char Glyph;
        public bool Danger = false;
        public bool Header;
    }

    // Menus in island colours, rounded corners
    class MenuLook : ToolStripProfessionalRenderer
    {
        static Color Bg { get { return Opaque(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.06f)); } }
        static Color Text { get { return Opaque(Ink.Base.Text); } }
        static Color Sub { get { return Opaque(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, Ink.Light ? 0.65f : 0.62f)); } }
        static Color Hover { get { return Opaque(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.16f)); } }
        static Color Line { get { return Opaque(Ink.Mix(Ink.Base.Bg, Ink.Base.Text, 0.14f)); } }

        static Color Opaque(Color c) { return Color.FromArgb(255, c.R, c.G, c.B); }

        public MenuLook() : base(new Colors()) { RoundedEdges = false; }

        public static readonly Font MenuFont = new Font(Ink.MenuFamily, 9.5f);
        static readonly Font HeaderFont = new Font(Ink.MenuFamily, 9.5f, FontStyle.Bold);
        static readonly Font IconFont = new Font(Ink.IconFamily, 12.5f);
        static StringFormat glyphFormat;

        static StringFormat GlyphFormat
        {
            get
            {
                if (glyphFormat == null)
                {
                    glyphFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
                    glyphFormat.FormatFlags |= StringFormatFlags.NoWrap;
                    glyphFormat.Alignment = StringAlignment.Center;
                    glyphFormat.LineAlignment = StringAlignment.Center;
                }
                return glyphFormat;
            }
        }


        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Bg); }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            var meta = e.Item.Tag as MenuItemMeta;
            Color fill = (meta != null && meta.Danger) ? Color.FromArgb(36, Ink.Base.Negative) : Hover;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ink.Round(r, 6))
            using (var b = new SolidBrush(fill)) e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var meta = e.Item.Tag as MenuItemMeta;
            if (meta != null && meta.Header)
            {
                e.TextColor = Text;
                e.TextFont = HeaderFont;
            }
            else if (meta != null && meta.Danger && e.Item.Enabled)
            {
                e.TextColor = Ink.Base.Negative;
                e.TextFont = MenuFont;
            }
            else
            {
                e.TextColor = e.Item.Enabled ? Text : Sub;
                e.TextFont = MenuFont;
            }

            if (e.Item.Padding.Left >= 24 && e.TextRectangle.X < 28)
            {
                var tr = e.TextRectangle;
                e.TextRectangle = new Rectangle(30, tr.Y, tr.Width, tr.Height);
            }

            if (e.Item.Image != null)
            {
                var prevSmooth = e.Graphics.SmoothingMode;
                var prevInterp = e.Graphics.InterpolationMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(e.Item.Image, new Rectangle(10, (e.Item.Height - 16) / 2, 16, 16));
                e.Graphics.SmoothingMode = prevSmooth;
                e.Graphics.InterpolationMode = prevInterp;
            }
            else if (meta != null && meta.Glyph != '\0')
            {
                Color gc = (meta.Danger && e.Item.Enabled) ? Ink.Base.Negative : (e.Item.Enabled ? Text : Sub);
                var prevHint = e.Graphics.TextRenderingHint;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var b = new SolidBrush(gc))
                    e.Graphics.DrawString(meta.Glyph.ToString(), IconFont, b, 18f, e.Item.Height / 2f, GlyphFormat);
                e.Graphics.TextRenderingHint = prevHint;
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var meta = e.Item.Tag as MenuItemMeta;
            if (e.Item.Image != null || (meta != null && meta.Glyph != '\0')) return;
            var r = e.Item.Padding.Left >= 24
                ? new Rectangle(12, (e.Item.Height - 12) / 2, 12, 12)
                : e.ImageRectangle;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, s = Math.Min(r.Width, r.Height) * 0.7f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Text, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                e.Graphics.DrawLines(pen, new[] { new PointF(cx - s * 0.42f, cy), new PointF(cx - s * 0.12f, cy + s * 0.3f), new PointF(cx + s * 0.45f, cy - s * 0.32f) });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Sub;
            base.OnRenderArrow(e);
        }

        class Colors : ProfessionalColorTable
        {
            public override Color MenuBorder { get { return Line; } }
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Bg; } }
        }

        // Same look down every submenu
        public static void Apply(ToolStripDropDownMenu menu)
        {
            menu.Renderer = new MenuLook();
            menu.Font = MenuFont;
            menu.Padding = new Padding(2, 6, 2, 6);
            menu.HandleCreated += delegate { Native.RoundCorners(menu.Handle); };
            bool hasIcons = false;
            foreach (ToolStripItem item in menu.Items)
            {
                var sub = item as ToolStripMenuItem;
                if (sub == null) continue;
                if (item.Image != null || (item.Tag is MenuItemMeta && ((MenuItemMeta)item.Tag).Glyph != '\0'))
                {
                    hasIcons = true;
                    break;
                }
            }
            var pad = hasIcons ? new Padding(26, 4, 10, 4) : new Padding(4, 3, 4, 3);
            foreach (ToolStripItem item in menu.Items)
            {
                var sub = item as ToolStripMenuItem;
                if (sub == null) continue;
                item.Padding = pad;
                var meta = item.Tag as MenuItemMeta;
                if (meta != null && meta.Header) item.Font = HeaderFont;
                if (sub.HasDropDownItems || sub.DropDown != null)
                {
                    var dd = sub.DropDown as ToolStripDropDownMenu;
                    if (dd != null && !(dd.Renderer is MenuLook)) Apply(dd);
                }
            }
        }
    }
}
