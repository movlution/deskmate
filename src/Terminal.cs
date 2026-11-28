using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskmate
{
    // Window hosting an agent, found from its hook connection
    static class TerminalFinder
    {
        [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool altTab);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        static readonly object sync = new object();
        static readonly Dictionary<int, IntPtr> byPid = new Dictionary<int, IntPtr>();

        public static IntPtr ForConnection(IPEndPoint client, int serverPort, out int pid)
        {
            pid = PidOf(client.Port, serverPort);
            if (pid <= 0) return IntPtr.Zero;
            lock (sync)
            {
                IntPtr h;
                if (byPid.TryGetValue(pid, out h) && IsWindow(h)) return h;
                int hostPid;
                h = WindowFor(pid, out hostPid);
                if (hostPid > 0) pid = hostPid;
                if (h != IntPtr.Zero && hostPid > 0)
                {
                    if (byPid.Count > 64) byPid.Clear();
                    byPid[hostPid] = h;
                }
                return h;
            }
        }

        // Loopback client port -> owning process (MIB_TCPROW_OWNER_PID rows)
        static int PidOf(int clientPort, int serverPort)
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
            if (size <= 0) return 0;
            // Slack + retry: table can grow between size query and read
            size += 4096;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    uint rc = GetExtendedTcpTable(buf, ref size, false, 2, 5, 0);
                    if (rc == 122) { size += 4096; continue; }
                    if (rc != 0) return 0;
                    int n = Marshal.ReadInt32(buf);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr row = new IntPtr(buf.ToInt64() + 4 + i * 24);
                        if (Port(Marshal.ReadInt32(row, 8)) == clientPort && Port(Marshal.ReadInt32(row, 16)) == serverPort)
                            return Marshal.ReadInt32(row, 20);
                    }
                    return 0;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return 0;
        }

        static int Port(int raw) { return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF); }

        struct ProcInfo
        {
            public uint Parent;
            public string Exe;
        }

        internal static bool IsEphemeralHelper(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            switch (exe.ToLowerInvariant())
            {
                case "deskmate.exe":
                case "curl.exe":
                case "cmd.exe":
                case "bash.exe":
                case "sh.exe":
                case "conhost.exe":
                    return true;
            }
            return false;
        }

        // Walk up past short-lived hook helpers so cache and s.Pid bind to the persistent agent/terminal
        static IntPtr WindowFor(int pid, out int hostPid)
        {
            hostPid = pid;
            var procs = ProcessMap();
            int self = Process.GetCurrentProcess().Id, cur = pid;
            for (int depth = 0; depth < 16 && cur > 4; depth++)
            {
                ProcInfo info;
                bool hasInfo = procs.TryGetValue(cur, out info);
                // Step past ephemeral relay/curl/shell wrappers while a live parent exists
                if (hasInfo && IsEphemeralHelper(info.Exe) && info.Parent > 4 && info.Parent != cur && procs.ContainsKey((int)info.Parent))
                {
                    cur = (int)info.Parent;
                    continue;
                }
                IntPtr cached;
                if (byPid.TryGetValue(cur, out cached) && IsWindow(cached))
                {
                    hostPid = cur;
                    return cached;
                }
                IntPtr con = ConsoleOwner(cur);
                if (con != IntPtr.Zero) { hostPid = cur; return con; }
                try
                {
                    using (var p = Process.GetProcessById(cur))
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name == "explorer" || cur == self) break;
                        IntPtr h = p.MainWindowHandle;
                        if (h != IntPtr.Zero && IsWindowVisible(h)) { hostPid = cur; return h; }
                    }
                }
                catch (Exception) { }
                if (!hasInfo || info.Parent == cur) break;
                cur = (int)info.Parent;
            }
            // Fallback: inspect original pid directly if parent chain ended early
            IntPtr fallback = ConsoleOwner(pid);
            if (fallback != IntPtr.Zero) { hostPid = pid; return fallback; }
            return IntPtr.Zero;
        }

        delegate bool CtrlHandler(int type);
        [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(CtrlHandler handler, bool add);
        // Borrowed console's Ctrl+C must not kill Deskmate
        static readonly CtrlHandler ignoreCtrl = delegate (int type) { return type == 0 || type == 1; };
        static bool ctrlGuarded;

        // ConPTY: the stand-in window is owned by the real terminal
        static IntPtr ConsoleOwner(int pid)
        {
            if (!ctrlGuarded) ctrlGuarded = SetConsoleCtrlHandler(ignoreCtrl, true);
            FreeConsole();
            if (!AttachConsole((uint)pid)) return IntPtr.Zero;
            IntPtr c = GetConsoleWindow();
            FreeConsole();
            if (c == IntPtr.Zero) return IntPtr.Zero;
            var cls = new StringBuilder(64);
            GetClassName(c, cls, 64);
            IntPtr owner = cls.ToString() == "PseudoConsoleWindow" ? GetWindow(c, 4) : c;
            return owner != IntPtr.Zero && IsWindowVisible(owner) ? owner : IntPtr.Zero;
        }

        static Dictionary<int, ProcInfo> ProcessMap()
        {
            var map = new Dictionary<int, ProcInfo>();
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (Process32FirstW(snap, ref e))
                    do map[(int)e.th32ProcessID] = new ProcInfo { Parent = e.th32ParentProcessID, Exe = e.szExeFile ?? "" }; while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return map;
        }

        // Second chance at click time
        public static IntPtr ForPid(int pid)
        {
            if (pid <= 0) return IntPtr.Zero;
            lock (sync)
            {
                int hostPid;
                return WindowFor(pid, out hostPid);
            }
        }

        public static bool Focus(IntPtr h)
        {
            if (h == IntPtr.Zero || !IsWindow(h)) return false;
            if (IsIconic(h)) ShowWindow(h, 9);
            SwitchToThisWindow(h, true);
            return true;
        }
    }
}
