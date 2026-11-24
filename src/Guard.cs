using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Deskmate
{
    // One app's load in a sample: all its processes summed
    class GuardLoad
    {
        public string Name = "";
        public double Cpu;
        public long Ram;
        public List<int> Pids = new List<int>();
    }

    class GuardAlert
    {
        public string Name = "", Title = "";
        public bool Ram;
        public double Cpu;
        public long Bytes;
        public TimeSpan For;
        public int[] Pids = new int[0];
    }

    // Decides when a background app has been heavy long enough to say so
    class LoadGuard
    {
        public const double HotPct = 25, CoolPct = 15;
        public const double RamShare = 0.30;
        public const int RamLoadPct = 88;
        public static readonly TimeSpan HotFor = TimeSpan.FromMinutes(2), FatFor = TimeSpan.FromMinutes(1), Quiet = TimeSpan.FromMinutes(30);

        // Shell, system, and tools whose load isn't the user's to manage
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "memcompression", "secure system", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe",
            "lsass.exe", "svchost.exe", "dwm.exe", "explorer.exe", "sihost.exe", "ctfmon.exe", "fontdrvhost.exe", "conhost.exe", "dllhost.exe",
            "runtimebroker.exe", "searchhost.exe", "searchindexer.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe", "textinputhost.exe",
            "taskmgr.exe", "audiodg.exe", "msmpeng.exe", "mpdefendercoreservice.exe", "smartscreen.exe", "securityhealthsystray.exe", "lockapp.exe",
            "applicationframehost.exe", "widgets.exe", "widgetservice.exe", "phoneexperiencehost.exe", "trustedinstaller.exe", "tiworker.exe",
        };

        class Track { public DateTime HotSince = DateTime.MinValue, FatSince = DateTime.MinValue; }

        readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> quietUntil = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly object gate = new object();

        public static bool Skips(string name) { return Skip.Contains(name); }

        // Until Deskmate restarts
        public void Ignore(string name) { lock (gate) ignored.Add(name); }

        // Lets one app speak again (tests, or after its window closes)
        public void Forget(string name) { lock (gate) { quietUntil.Remove(name); tracks.Remove(name); } }

        // ramLoad is the system's memory load in percent; ramTotal in bytes
        public GuardAlert Feed(DateTime now, IList<GuardLoad> loads, string foreground, int ramLoad, long ramTotal)
        {
            lock (gate)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                GuardAlert best = null;
                foreach (var l in loads)
                {
                    if (string.IsNullOrEmpty(l.Name) || Skip.Contains(l.Name)) continue;
                    seen.Add(l.Name);
                    Track t;
                    if (!tracks.TryGetValue(l.Name, out t)) tracks[l.Name] = t = new Track();
                    // In use: its load is what the user wants
                    bool front = string.Equals(l.Name, foreground, StringComparison.OrdinalIgnoreCase);
                    if (front) { t.HotSince = t.FatSince = DateTime.MinValue; continue; }

                    if (l.Cpu >= HotPct) { if (t.HotSince == DateTime.MinValue) t.HotSince = now; }
                    else if (l.Cpu < CoolPct) t.HotSince = DateTime.MinValue;

                    bool fat = ramTotal > 0 && ramLoad >= RamLoadPct && l.Ram >= (long)(ramTotal * RamShare);
                    if (fat) { if (t.FatSince == DateTime.MinValue) t.FatSince = now; }
                    else t.FatSince = DateTime.MinValue;

                    if (ignored.Contains(l.Name)) continue;
                    DateTime until;
                    if (quietUntil.TryGetValue(l.Name, out until) && now < until) continue;

                    GuardAlert a = null;
                    if (t.HotSince != DateTime.MinValue && now - t.HotSince >= HotFor)
                        a = new GuardAlert { Name = l.Name, Cpu = l.Cpu, Bytes = l.Ram, For = now - t.HotSince };
                    else if (t.FatSince != DateTime.MinValue && now - t.FatSince >= FatFor)
                        a = new GuardAlert { Name = l.Name, Ram = true, Cpu = l.Cpu, Bytes = l.Ram, For = now - t.FatSince };
                    if (a == null) continue;
                    a.Pids = l.Pids.ToArray();
                    if (best == null || Weight(a, ramTotal) > Weight(best, ramTotal)) best = a;
                }
                var gone = new List<string>();
                foreach (var k in tracks.Keys) if (!seen.Contains(k)) gone.Add(k);
                foreach (var k in gone) tracks.Remove(k);
                if (best != null)
                {
                    quietUntil[best.Name] = now + Quiet;
                    Track t = tracks[best.Name];
                    t.HotSince = t.FatSince = DateTime.MinValue;
                }
                return best;
            }
        }

        static double Weight(GuardAlert a, long ramTotal)
        {
            return a.Ram ? 100.0 * a.Bytes / Math.Max(1, ramTotal) : a.Cpu;
        }
    }

    // Raw process times in one call: cheaper than Process.GetProcesses
    static class GuardNative
    {
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int needed);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern uint GetPriorityClass(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool SetPriorityClass(IntPtr h, uint cls);
        [DllImport("kernel32.dll")] public static extern bool SetProcessInformation(IntPtr h, int cls, ref Throttle info, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus { public uint Length, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Throttle { public uint Version, ControlMask, StateMask; }

        public const uint QueryLimited = 0x1000, SetInfo = 0x0200, SetLimited = 0x2000;
        public const uint Idle = 0x40, Normal = 0x20;

        public struct Proc { public int Pid, Parent; public uint Session; public long Cpu, Ram; public string Name; }

        static IntPtr buf = IntPtr.Zero;
        static int bufLen;

        public static uint Session()
        {
            uint s;
            return ProcessIdToSessionId((uint)Process.GetCurrentProcess().Id, out s) ? s : 0;
        }

        public static void Memory(out int load, out long total)
        {
            var m = new MemStatus { Length = (uint)Marshal.SizeOf(typeof(MemStatus)) };
            if (GlobalMemoryStatusEx(ref m)) { load = (int)m.Load; total = (long)m.Total; }
            else { load = 0; total = 0; }
        }

        // SYSTEM_PROCESS_INFORMATION, x64 offsets
        public static List<Proc> Snapshot()
        {
            var list = new List<Proc>();
            if (IntPtr.Size != 8) return list;
            if (buf == IntPtr.Zero) { bufLen = 1 << 20; buf = Marshal.AllocHGlobal(bufLen); }
            int needed, status;
            while ((status = NtQuerySystemInformation(5, buf, bufLen, out needed)) == unchecked((int)0xC0000004))
            {
                Marshal.FreeHGlobal(buf);
                bufLen = Math.Max(bufLen * 2, needed + 65536);
                buf = Marshal.AllocHGlobal(bufLen);
            }
            if (status != 0) return list;
            int off = 0;
            while (true)
            {
                IntPtr e = buf + off;
                int next = Marshal.ReadInt32(e, 0);
                var p = new Proc
                {
                    Ram = Marshal.ReadInt64(e, 0x08),
                    Cpu = Marshal.ReadInt64(e, 0x28) + Marshal.ReadInt64(e, 0x30),
                    Pid = (int)Marshal.ReadInt64(e, 0x50),
                    Parent = (int)Marshal.ReadInt64(e, 0x58),
                    Session = (uint)Marshal.ReadInt32(e, 0x64),
                };
                int len = Marshal.ReadInt16(e, 0x38) & 0xFFFF;
                IntPtr name = Marshal.ReadIntPtr(e, 0x40);
                p.Name = len > 0 && name != IntPtr.Zero ? Marshal.PtrToStringUni(name, len / 2) : "";
                list.Add(p);
                if (next == 0 || off + next >= bufLen) break;
                off += next;
            }
            return list;
        }

        // File description, like the taskbar shows
        public static string Friendly(int pid, string fallback)
        {
            string plain = fallback.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fallback.Substring(0, fallback.Length - 4) : fallback;
            IntPtr h = OpenProcess(QueryLimited, false, pid);
            if (h == IntPtr.Zero) return plain;
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return plain;
                string d = (FileVersionInfo.GetVersionInfo(sb.ToString()).FileDescription ?? "").Trim();
                return d.Length > 1 && d.Length <= 40 ? d : plain;
            }
            catch (Exception) { return plain; }
            finally { CloseHandle(h); }
        }

        // Task Manager's efficiency mode: EcoQoS plus idle priority. Returns the old priority, 0 on failure
        public static uint SetEco(int pid, bool on, uint restore)
        {
            IntPtr h = OpenProcess(SetInfo | QueryLimited, false, pid);
            if (h == IntPtr.Zero) return 0;
            try
            {
                uint was = GetPriorityClass(h);
                var t = new Throttle { Version = 1, ControlMask = 1, StateMask = on ? 1u : 0u };
                bool ok = SetProcessInformation(h, 4, ref t, Marshal.SizeOf(typeof(Throttle)));
                ok = SetPriorityClass(h, on ? Idle : (restore != 0 ? restore : Normal)) || ok;
                return ok ? (was != 0 ? was : Normal) : 0;
            }
            finally { CloseHandle(h); }
        }
    }

    // Penjaga beban: a background app heavy for minutes gets a toast with Mode hemat, Tutup, Abaikan
    partial class Island
    {
        const char IcoGuard = '';
        const float GuardBtnH = 30;
        readonly LoadGuard loadGuard = new LoadGuard();
        readonly Dictionary<int, long> guardCpu = new Dictionary<int, long>();
        // Throttled pid → its priority before
        readonly Dictionary<int, uint> guardThrottled = new Dictionary<int, uint>();
        readonly Dictionary<int, string> guardThrottledName = new Dictionary<int, string>();
        long guardTicks;
        int guardBusy;
        DateTime guardNextUtc;
        uint guardSession = uint.MaxValue;

        bool GuardOn { get { return settings.GetBool("guard", true); } }

        // From LifeTick, every second; samples every 5
        void TickGuard(DateTime nowUtc)
        {
            if (nowUtc < guardNextUtc) return;
            guardNextUtc = nowUtc.AddSeconds(5);
            bool on = GuardOn;
            if (!on && guardThrottled.Count == 0) return;
            if (Interlocked.Exchange(ref guardBusy, 1) == 1) return;
            IntPtr fg = Native.GetForegroundWindow();
            int fgPid = 0;
            if (fg != IntPtr.Zero) Native.GetWindowThreadProcessId(fg, out fgPid);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { GuardSample(on, fgPid); }
                catch (Exception ex) { Log.Write("guard: " + ex.Message); }
                finally { Interlocked.Exchange(ref guardBusy, 0); }
            });
        }

        // Pool thread; one at a time
        void GuardSample(bool on, int fgPid)
        {
            if (guardSession == uint.MaxValue) guardSession = GuardNative.Session();
            int self = Process.GetCurrentProcess().Id;
            long ticks = DateTime.UtcNow.Ticks;
            var procs = GuardNative.Snapshot();
            if (procs.Count == 0) return;
            string fgName = "";
            foreach (var pr in procs) if (pr.Pid == fgPid) { fgName = pr.Name; break; }
            RestoreForeground(fgName, procs);
            if (!on) return;

            long span = guardTicks == 0 ? 0 : ticks - guardTicks;
            guardTicks = ticks;
            double scale = span > 0 ? 100.0 / ((double)span * Environment.ProcessorCount) : 0;
            var byName = new Dictionary<string, GuardLoad>(StringComparer.OrdinalIgnoreCase);
            var alive = new HashSet<int>();
            foreach (var pr in procs)
            {
                if (pr.Pid == 0 || pr.Pid == self || pr.Parent == self || pr.Session != guardSession || pr.Name.Length == 0) continue;
                alive.Add(pr.Pid);
                long before;
                bool known = guardCpu.TryGetValue(pr.Pid, out before);
                guardCpu[pr.Pid] = pr.Cpu;
                GuardLoad l;
                if (!byName.TryGetValue(pr.Name, out l)) byName[pr.Name] = l = new GuardLoad { Name = pr.Name };
                l.Pids.Add(pr.Pid);
                l.Ram += pr.Ram;
                if (known && scale > 0 && pr.Cpu >= before) l.Cpu += (pr.Cpu - before) * scale;
            }
            var dead = new List<int>();
            foreach (var pid in guardCpu.Keys) if (!alive.Contains(pid)) dead.Add(pid);
            foreach (var pid in dead) guardCpu.Remove(pid);
            if (span == 0) return;

            int load; long total;
            GuardNative.Memory(out load, out total);
            var alert = loadGuard.Feed(DateTime.UtcNow, new List<GuardLoad>(byName.Values), fgName, load, total);
            if (alert == null) return;
            alert.Title = GuardNative.Friendly(alert.Pids.Length > 0 ? alert.Pids[0] : 0, alert.Name);
            Log.Write("guard: " + alert.Name + (alert.Ram ? " ram " + alert.Bytes : " cpu " + alert.Cpu.ToString("0")));
            Post(() => ShowGuard(alert));
        }

        // Back to normal once the user opens it
        void RestoreForeground(string fgName, List<GuardNative.Proc> procs)
        {
            if (fgName.Length == 0) return;
            List<int> back = null;
            lock (guardThrottled)
            {
                foreach (var kv in guardThrottledName)
                    if (string.Equals(kv.Value, fgName, StringComparison.OrdinalIgnoreCase)) { if (back == null) back = new List<int>(); back.Add(kv.Key); }
                if (back == null)
                {
                    // Processes that ended
                    if (guardThrottled.Count == 0) return;
                    var alive = new HashSet<int>();
                    foreach (var pr in procs) alive.Add(pr.Pid);
                    var gone = new List<int>();
                    foreach (var pid in guardThrottled.Keys) if (!alive.Contains(pid)) gone.Add(pid);
                    foreach (var pid in gone) { guardThrottled.Remove(pid); guardThrottledName.Remove(pid); }
                    return;
                }
            }
            foreach (var pid in back)
            {
                uint was;
                lock (guardThrottled) { if (!guardThrottled.TryGetValue(pid, out was)) continue; guardThrottled.Remove(pid); guardThrottledName.Remove(pid); }
                GuardNative.SetEco(pid, false, was);
            }
            Log.Write("guard: " + fgName + " back to normal");
        }

        void ShowGuard(GuardAlert a)
        {
            var t = ShowToast(View.Guard, 14, 2, true);
            if (t != null) { t.Data = a; t.Flag = false; }
        }

        void GuardEco(GuardAlert a)
        {
            int ok = 0;
            foreach (var pid in a.Pids)
            {
                uint was = GuardNative.SetEco(pid, true, 0);
                if (was == 0) continue;
                ok++;
                lock (guardThrottled) { if (!guardThrottled.ContainsKey(pid)) guardThrottled[pid] = was; guardThrottledName[pid] = a.Name; }
            }
            DismissToast();
            if (ok == 0) ShowInfo(IcoGuard, Lang.F("{0} tidak bisa diatur", a.Title), Lang.T("Aplikasinya berjalan sebagai administrator"), true);
            else ShowInfo(IcoGuard, Lang.F("{0} dalam mode hemat", a.Title), Lang.T("Kembali normal saat kamu membukanya"), true);
        }

        void GuardClose(GuardAlert a)
        {
            DismissToast();
            // Window lookups are slow across many processes; a save prompt may follow, the app decides
            ThreadPool.QueueUserWorkItem(delegate
            {
                int asked = 0;
                foreach (var pid in a.Pids)
                {
                    try { using (var proc = Process.GetProcessById(pid)) if (proc.MainWindowHandle != IntPtr.Zero && proc.CloseMainWindow()) asked++; }
                    catch (Exception) { }
                }
                if (asked == 0) Post(() => ShowInfo(IcoGuard, Lang.F("{0} tidak punya jendela", a.Title), Lang.T("Tutup lewat aplikasinya atau Task Manager"), true));
            });
        }

        void GuardIgnore(GuardAlert a)
        {
            loadGuard.Ignore(a.Name);
            DismissToast();
        }

        string GuardTitle(GuardAlert a)
        {
            if (a.Ram) return Lang.F("{0} memakai {1} RAM", a.Title, Fmt.Bytes(a.Bytes));
            return Lang.F("{0} memakai {1}% CPU", a.Title, Math.Round(a.Cpu).ToString("0"));
        }

        string GuardDetail(GuardAlert a)
        {
            if (a.Ram) return Lang.T("Memori hampir penuh, aplikasinya di latar");
            return Lang.F("Sejak {0} menit, saat tidak dipakai", Math.Max(2, (int)Math.Round(a.For.TotalMinutes)));
        }

        float GuardWidth()
        {
            var a = toast != null ? toast.Data as GuardAlert : null;
            if (a == null || p.G == null) return 460;
            float text = Math.Max(p.Measure(GuardTitle(a), Ink.Semi(13.5f)), p.Measure(GuardDetail(a), Ink.Text(12.5f)));
            return Clamp(58 + text + 12 + GuardButtonsW(a) + 16, 400, 640);
        }

        float GuardButtonsW(GuardAlert a)
        {
            float w = GuardBtnW("Tutup", 60) + 6 + GuardBtnW("Abaikan", 70);
            return a.Ram ? w : w + 6 + GuardBtnW("Mode hemat", 92);
        }

        float GuardBtnW(string id, float min) { return Math.Max(min, p.Measure(Lang.T(id), Ink.Semi(12.5f)) + 26); }

        void DrawGuard(float w, float h)
        {
            var a = toast != null ? toast.Data as GuardAlert : null;
            if (a == null) return;
            float cy = h / 2;
            p.Circle(30, cy, 16, p.T.Fill);
            p.Icon(IcoGuard, 15f, p.T.Text, 30, cy);
            float ecoW = GuardBtnW("Mode hemat", 92), closeW = GuardBtnW("Tutup", 60), skipW = GuardBtnW("Abaikan", 70);
            float bx = w - 16 - GuardButtonsW(a);
            float x = 58, right = bx - 8;
            p.Text(GuardTitle(a), Ink.Semi(13.5f), p.T.Text, x, cy - 10, right - x, StringAlignment.Near);
            p.Text(GuardDetail(a), Ink.Text(12.5f), p.T.Sub, x, cy + 11, right - x, StringAlignment.Near);
            float y = cy - GuardBtnH / 2;
            if (!a.Ram)
            {
                Pill(new RectangleF(bx, y, ecoW, GuardBtnH), "guardEco", Lang.T("Mode hemat"), PillKind.Primary, delegate { GuardEco(a); });
                bx += ecoW + 6;
            }
            Pill(new RectangleF(bx, y, closeW, GuardBtnH), "guardClose", Lang.T("Tutup"), PillKind.Secondary, delegate { GuardClose(a); });
            Pill(new RectangleF(bx + closeW + 6, y, skipW, GuardBtnH), "guardSkip", Lang.T("Abaikan"), PillKind.Tertiary, delegate { GuardIgnore(a); });
        }

        string DescribeGuard()
        {
            var a = toast != null ? toast.Data as GuardAlert : null;
            return a == null ? "" : GuardTitle(a) + ". " + GuardDetail(a);
        }
    }
}
