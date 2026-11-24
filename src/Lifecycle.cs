using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // The installer's side of Deskmate: close the running island, then take over
    // or let go of everything Deskmate wrote outside %APPDATA%\Deskmate.
    //   Deskmate.exe --quit       exit 0 once no island or Ingatan service is left
    //   Deskmate.exe --adopt      after install: autostart and agent hooks point at this copy
    //   Deskmate.exe --uninstall  closes Deskmate, gives the taskbar back, removes hooks, statusline, autostart
    static class Lifecycle
    {
        // Gemini CLI hooks are dropped on start (AgentSetup.Refresh), so adopt leaves them alone
        static readonly string[] AgentIds = { "codex", "gemini", "copilot", "antigravity" };
        public const int StillRunning = 2;

        public static int Quit(string[] args)
        {
            return CloseRunning(30000) ? 0 : StillRunning;
        }

        public static int Adopt(string[] args)
        {
            Step("autostart", delegate { if (AutoStart.Enabled) AutoStart.Enabled = true; });
            foreach (var id in AgentIds)
            {
                if (id == "gemini") continue;
                string agent = id;
                Step(agent + " hooks", delegate
                {
                    if (!Linked(agent)) return;
                    string err = AgentSetup.Install(agent);
                    if (err != null) throw new InvalidOperationException(err);
                });
            }
            Step("statusline", delegate
            {
                if (!StatusSetup.Linked()) return;
                string err = StatusSetup.Install();
                if (err != null) throw new InvalidOperationException(err);
            });
            return 0;
        }

        public static int Uninstall(string[] args)
        {
            if (!CloseRunning(30000)) return StillRunning;
            var s = new Settings();
            // The island gives the taskbar back when it closes; this covers a copy that crashed with it hidden
            Step("taskbar", delegate { Taskbar.Restore(s); });
            Step("claude hooks", delegate { HookSetup.Remove(s.GetInt("agent.port", 47821)); });
            Step("statusline", StatusSetup.Remove);
            foreach (var id in AgentIds)
            {
                string agent = id;
                Step(agent + " hooks", delegate { if (Linked(agent)) AgentSetup.Remove(agent); });
            }
            Step("antigravity statusline", StatusSetup.AgyRemove);
            Step("autostart", delegate { AutoStart.Enabled = false; });
            Step("start menu", RemoveOwnShortcut);
            return 0;
        }

        static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Write("lifecycle " + name + ": " + ex.Message); }
        }

        // Any handler of ours, whatever path it was written with
        static bool Linked(string id)
        {
            string file = AgentSetup.ConfigFile(id);
            try { return File.Exists(file) && File.ReadAllText(file).Contains(" --relay " + id); }
            catch (IOException) { return false; }
        }

        // Only the island's own shortcut; the installer removes the one it made
        static void RemoveOwnShortcut()
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Deskmate.lnk");
            if (!File.Exists(lnk)) return;
            string target = ShellApp.ReadLink(lnk).Target ?? "";
            if (string.Equals(target, System.Windows.Forms.Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) File.Delete(lnk);
        }

        // Same as the island's Quit item: WM_CLOSE to its main window, so docks close and the taskbar comes back
        static bool CloseRunning(int timeoutMs)
        {
            // Test runs must not close the user's real island
            if (Settings.TestMode) { Log.Write("test mode: running islands untouched"); return true; }
            int self = Process.GetCurrentProcess().Id;
            var islands = new List<int>();
            foreach (var w in IslandWindows(self))
            {
                PostMessage(w.Key, 0x0010, IntPtr.Zero, IntPtr.Zero);
                if (!islands.Contains(w.Value)) islands.Add(w.Value);
            }
            var clock = Stopwatch.StartNew();
            foreach (int pid in islands)
            {
                try
                {
                    using (var p = Process.GetProcessById(pid))
                        if (!p.WaitForExit((int)Math.Max(0, timeoutMs - clock.ElapsedMilliseconds))) return false;
                }
                catch (ArgumentException) { }
            }
            // Ingatan follows its parent within a few seconds; it holds Deskmate.exe open
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                if (IngatPids(self).Count == 0) return true;
                Thread.Sleep(250);
            }
            return IngatPids(self).Count == 0;
        }

        static List<KeyValuePair<IntPtr, int>> IslandWindows(int self)
        {
            var found = new List<KeyValuePair<IntPtr, int>>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == 0 || pid == self) return true;
                var title = new StringBuilder(64);
                var cls = new StringBuilder(128);
                GetWindowText(h, title, title.Capacity);
                GetClassName(h, cls, cls.Capacity);
                if (title.ToString() != "Deskmate" || !cls.ToString().StartsWith("WindowsForms10.Window", StringComparison.Ordinal)) return true;
                try
                {
                    using (var p = Process.GetProcessById((int)pid))
                        if (string.Equals(p.ProcessName, "Deskmate", StringComparison.OrdinalIgnoreCase)) found.Add(new KeyValuePair<IntPtr, int>(h, (int)pid));
                }
                catch (ArgumentException) { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static List<int> IngatPids(int self)
        {
            var list = new List<int>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'Deskmate.exe'"))
                using (var results = q.Get())
                    foreach (ManagementObject o in results)
                        using (o)
                        {
                            int pid = Convert.ToInt32(o["ProcessId"]);
                            string cmd = Convert.ToString(o["CommandLine"]) ?? "";
                            if (pid != self && cmd.Contains(" --ingat")) list.Add(pid);
                        }
            }
            catch (ManagementException ex) { Log.Write("lifecycle ingat scan: " + ex.Message); }
            return list;
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    }
}
