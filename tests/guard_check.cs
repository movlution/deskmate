using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Deskmate
{
    // Penjaga beban: when it speaks, process snapshot parsing, and efficiency mode on a real process
    public static class GuardCheck
    {
        static void Assert(bool ok, string msg)
        {
            if (!ok) throw new Exception("Assertion failed: " + msg);
        }

        static GuardLoad L(string name, double cpu, long ram = 100L << 20)
        {
            var l = new GuardLoad { Name = name, Cpu = cpu, Ram = ram };
            l.Pids.Add(1234);
            return l;
        }

        public static string Run()
        {
            TestCpuNeedsTwoMinutes();
            TestHysteresis();
            TestForegroundIsSpared();
            TestQuietAndIgnore();
            TestSystemSkipped();
            TestRam();
            TestSnapshot();
            TestEcoMode();
            return "guard ok";
        }

        static void TestCpuNeedsTwoMinutes()
        {
            var g = new LoadGuard();
            var t0 = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);
            for (int s = 0; s < 115; s += 5)
                Assert(g.Feed(t0.AddSeconds(s), new[] { L("chrome.exe", 40) }, "", 50, 16L << 30) == null, "quiet before 2 minutes (" + s + "s)");
            var a = g.Feed(t0.AddSeconds(120), new[] { L("chrome.exe", 40) }, "", 50, 16L << 30);
            Assert(a != null && a.Name == "chrome.exe" && !a.Ram && a.Pids.Length == 1, "alert at 2 minutes");
        }

        static void TestHysteresis()
        {
            var g = new LoadGuard();
            var t0 = DateTime.UtcNow;
            g.Feed(t0, new[] { L("app.exe", 30) }, "", 50, 16L << 30);
            // A dip between cool and hot keeps the clock running
            g.Feed(t0.AddSeconds(60), new[] { L("app.exe", 20) }, "", 50, 16L << 30);
            Assert(g.Feed(t0.AddSeconds(120), new[] { L("app.exe", 30) }, "", 50, 16L << 30) != null, "dip above cool keeps counting");

            var h = new LoadGuard();
            h.Feed(t0, new[] { L("app.exe", 30) }, "", 50, 16L << 30);
            h.Feed(t0.AddSeconds(60), new[] { L("app.exe", 5) }, "", 50, 16L << 30);
            Assert(h.Feed(t0.AddSeconds(120), new[] { L("app.exe", 30) }, "", 50, 16L << 30) == null, "cooling resets");
        }

        static void TestForegroundIsSpared()
        {
            var g = new LoadGuard();
            var t0 = DateTime.UtcNow;
            for (int s = 0; s <= 300; s += 5)
                Assert(g.Feed(t0.AddSeconds(s), new[] { L("game.exe", 90) }, "GAME.EXE", 50, 16L << 30) == null, "foreground app never alerts");
            // Leaving it starts the clock fresh
            Assert(g.Feed(t0.AddSeconds(305), new[] { L("game.exe", 90) }, "", 50, 16L << 30) == null, "fresh clock after switching away");
        }

        static void TestQuietAndIgnore()
        {
            var g = new LoadGuard();
            var t0 = DateTime.UtcNow;
            g.Feed(t0, new[] { L("a.exe", 50) }, "", 50, 16L << 30);
            Assert(g.Feed(t0.AddMinutes(2), new[] { L("a.exe", 50) }, "", 50, 16L << 30) != null, "first alert");
            Assert(g.Feed(t0.AddMinutes(10), new[] { L("a.exe", 50) }, "", 50, 16L << 30) == null, "quiet for 30 minutes");
            Assert(g.Feed(t0.AddMinutes(33), new[] { L("a.exe", 50) }, "", 50, 16L << 30) != null, "speaks again after 30 minutes");

            var h = new LoadGuard();
            h.Ignore("b.exe");
            h.Feed(t0, new[] { L("b.exe", 50) }, "", 50, 16L << 30);
            Assert(h.Feed(t0.AddMinutes(5), new[] { L("b.exe", 50) }, "", 50, 16L << 30) == null, "ignored app stays quiet");
        }

        static void TestSystemSkipped()
        {
            var g = new LoadGuard();
            var t0 = DateTime.UtcNow;
            g.Feed(t0, new[] { L("dwm.exe", 60), L("Explorer.EXE", 60) }, "", 50, 16L << 30);
            Assert(g.Feed(t0.AddMinutes(3), new[] { L("dwm.exe", 60), L("Explorer.EXE", 60) }, "", 50, 16L << 30) == null, "shell processes are skipped");
            Assert(LoadGuard.Skips("svchost.exe") && !LoadGuard.Skips("chrome.exe"), "skip list");
        }

        static void TestRam()
        {
            var g = new LoadGuard();
            var t0 = DateTime.UtcNow;
            long total = 16L << 30;
            // Big but the system has room: fine
            g.Feed(t0, new[] { L("fat.exe", 1, 6L << 30) }, "", 70, total);
            Assert(g.Feed(t0.AddMinutes(2), new[] { L("fat.exe", 1, 6L << 30) }, "", 70, total) == null, "room left, no alert");
            g.Feed(t0.AddMinutes(3), new[] { L("fat.exe", 1, 6L << 30) }, "", 92, total);
            var a = g.Feed(t0.AddMinutes(4), new[] { L("fat.exe", 1, 6L << 30), L("hot.exe", 30) }, "", 92, total);
            Assert(a != null && a.Ram && a.Name == "fat.exe", "memory pressure alert");
        }

        static void TestSnapshot()
        {
            var me = Process.GetCurrentProcess();
            var list = GuardNative.Snapshot();
            Assert(list.Count > 20, "snapshot lists processes: " + list.Count);
            bool found = false;
            foreach (var p in list)
            {
                if (p.Pid != me.Id) continue;
                found = true;
                Assert(string.Equals(p.Name, me.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase), "own name parsed: " + p.Name);
                Assert(p.Ram > 0 && p.Cpu > 0, "own ram and cpu parsed");
                Assert(p.Session == GuardNative.Session(), "own session parsed");
            }
            Assert(found, "own pid present");
            int load; long total;
            GuardNative.Memory(out load, out total);
            Assert(load > 0 && load <= 100 && total > (1L << 30), "memory status");
        }

        static void TestEcoMode()
        {
            var psi = new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var child = Process.Start(psi))
            {
                try
                {
                    // The child inherits this process's priority (Below Normal on CI runners),
                    // so the promise checked is "restores what it was", not "restores Normal"
                    var start = child.PriorityClass;
                    uint was = GuardNative.SetEco(child.Id, true, 0);
                    Assert(was == (uint)start, "eco mode applied, returns the starting priority " + (uint)start + ": " + was);
                    child.Refresh();
                    Assert(child.PriorityClass == ProcessPriorityClass.Idle, "idle priority while in eco mode");
                    Assert(GuardNative.SetEco(child.Id, false, was) != 0, "eco mode removed");
                    child.Refresh();
                    Assert(child.PriorityClass == start, "starting priority restored: " + child.PriorityClass);
                    Assert(GuardNative.Friendly(child.Id, "PING.EXE").Length > 0, "friendly name");
                }
                finally { try { child.Kill(); } catch (Exception) { } }
            }
        }
    }
}
