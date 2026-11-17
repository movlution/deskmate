using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Deskmate
{
    // Windows taskbar out of the way while the dock replaces it; always undoable
    static class Taskbar
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);

        // Explorer sends this after it restarts
        public static readonly int Created = RegisterWindowMessage("TaskbarCreated");

        static IntPtr hook;
        static Native.WinEventProc onShow;
        // Monitors whose dock replaces the Windows taskbar. A dock on every screen
        // means every screen's bar goes; tracking one monitor left the secondary
        // taskbar drawn next to the dock.
        static readonly HashSet<IntPtr> owned = new HashSet<IntPtr>();

        static List<IntPtr> Bars()
        {
            var list = new List<IntPtr>();
            IntPtr main = ExplorerBar();
            if (main != IntPtr.Zero) list.Add(main);
            for (IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) list.Add(h);
            return list;
        }

        // Bars on a replaced screen; the others stay. Strict on purpose: an empty set
        // must mean "nothing is ours", or Rehide would hide every screen's bar.
        static List<IntPtr> Mine()
        {
            var list = new List<IntPtr>();
            if (owned.Count == 0) return list;
            foreach (var h in Bars())
                if (owned.Contains(Native.MonitorFromWindow(h, 2))) list.Add(h);
            return list;
        }

        static IntPtr ExplorerBar()
        {
            // Our tray stand-in shares the class; Explorer's is the visible-capable one
            for (IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null))
            {
                int pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid != System.Diagnostics.Process.GetCurrentProcess().Id) return h;
            }
            return IntPtr.Zero;
        }

        static int GetState(IntPtr bar)
        {
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)), hWnd = bar };
            return (int)Native.SHAppBarMessage(4, ref abd).ToInt64();
        }

        static void SetState(IntPtr bar, int state)
        {
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)), hWnd = bar, lParam = (IntPtr)state };
            Native.SHAppBarMessage(10, ref abd);
        }

        // Auto-hide frees the work area; hiding stops it peeking back
        public static void Hide(Settings s, IntPtr dockMonitor)
        {
            if (dockMonitor != IntPtr.Zero) owned.Add(dockMonitor);
            IntPtr main = ExplorerBar();
            if (main == IntPtr.Zero) return;
            if (!s.GetBool("dockbar.taskbar.hidden", false))
            {
                s.Set("dockbar.taskbar.state", GetState(main));
                s.Set("dockbar.taskbar.hidden", true);
                s.Save();
            }
            SetState(main, GetState(main) | 1);
            Apply();
            // Bars change after an Explorer restart: watch the current ones
            Unwatch();
            Watch(main);
        }

        // One dock went away: only the screens it owned come back
        public static void Release(Settings s, IntPtr dockMonitor)
        {
            if (dockMonitor == IntPtr.Zero) return;
            if (!owned.Remove(dockMonitor)) return;
            if (owned.Count == 0) { Restore(s); return; }
            Apply();
            Unwatch();
            IntPtr main = ExplorerBar();
            if (main != IntPtr.Zero) Watch(main);
        }

        // Owned bars down, every other screen's bar back up
        static void Apply()
        {
            var mine = Mine();
            foreach (var h in Bars())
                if (mine.Contains(h)) Native.ShowWindow(h, 0);
                else if (!IsWindowVisible(h)) Native.ShowWindow(h, 8);
        }

        // Explorer shows its bar for Start and flyouts: hide it again at once
        static void Watch(IntPtr main)
        {
            if (hook != IntPtr.Zero) return;
            int pid;
            Native.GetWindowThreadProcessId(main, out pid);
            var bars = Mine();
            onShow = delegate (IntPtr h, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
            {
                if (obj == 0 && bars.Contains(hwnd)) Native.ShowWindow(hwnd, 0);
            };
            hook = Native.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, onShow, (uint)pid, 0, 0);
        }

        static void Unwatch()
        {
            if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
            hook = IntPtr.Zero;
            onShow = null;
        }

        public static void Restore(Settings s)
        {
            Unwatch();
            owned.Clear();
            if (!s.GetBool("dockbar.taskbar.hidden", false)) return;
            IntPtr main = ExplorerBar();
            if (main != IntPtr.Zero) SetState(main, s.GetInt("dockbar.taskbar.state", 0));
            foreach (var h in Bars())
            {
                Native.ShowWindow(h, 8);
                Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            s.Set("dockbar.taskbar.hidden", false);
            s.Save();
        }

        // Something showed it again
        public static bool Showing()
        {
            foreach (var h in Mine()) if (IsWindowVisible(h)) return true;
            return false;
        }

        public static void Rehide() { foreach (var h in Mine()) Native.ShowWindow(h, 0); }

        // Win, alone or with a key: Start, search, quick settings
        public static void Win(byte key)
        {
            const byte LWin = 0x5B;
            keybd_event(LWin, 0, 0, IntPtr.Zero);
            if (key != 0) { keybd_event(key, 0, 0, IntPtr.Zero); keybd_event(key, 0, 2, IntPtr.Zero); }
            keybd_event(LWin, 0, 2, IntPtr.Zero);
        }
    }
}
