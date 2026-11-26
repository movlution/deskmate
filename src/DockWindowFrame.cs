using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Deskmate
{
    static class DockWindowFrame
    {
        const int ExtendedFrameBounds = 9;

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Native.RECT value, int size);

        public static bool Update(DockWin win)
        {
            if (win == null || !ShellApp.WindowExists(win.H)) return false;
            if (ShellApp.IsMinimized(win.H))
            {
                win.FrameMinimized = true;
                return false;
            }

            Native.RECT rect;
            if (DwmGetWindowAttribute(win.H, ExtendedFrameBounds, out rect, 16) != 0 &&
                !Native.GetWindowRect(win.H, out rect)) return false;
            var frame = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (frame.Width < 100 || frame.Height < 80 || frame.Left <= -30000 || frame.Top <= -30000)
                return false;
            win.LastFrame = frame;
            win.FrameMonitor = Native.MonitorFromWindow(win.H, 2);
            win.FrameMaximized = ShellApp.IsMaximized(win.H);
            win.FrameMinimized = false;
            return true;
        }
    }
}
