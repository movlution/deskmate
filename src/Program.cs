using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Agent hook bridge: no window, no mutex
            if (args.Length > 0 && args[0] == "--relay") return Relay.Run(args);
            if (args.Length > 0 && args[0] == "--notify") return NotifyCommand.Run(args);
            if (args.Length > 0 && args[0] == "--run") return RunCommand.Run(args);
            if (args.Length > 0 && args[0] == "--statusline") return StatusLine.Run(args);
            // Ingatan: the memory service, and its MCP face for other assistants
            if (args.Length > 0 && args[0] == "--ingat") return IngatService.Run(args);
            if (args.Length > 0 && args[0] == "--mcp") return IngatMcp.Run(args);
            // Installer: close, take over, let go
            if (args.Length > 0 && args[0] == "--quit") return Lifecycle.Quit(args);
            if (args.Length > 0 && args[0] == "--adopt") return Lifecycle.Adopt(args);
            if (args.Length > 0 && args[0] == "--uninstall") return Lifecycle.Uninstall(args);
            return RunIsland();
        }

        // Old copy may still be closing after an update
        static bool Take(Mutex m)
        {
            try { return m.WaitOne(20000); }
            catch (AbandonedMutexException) { return true; }
        }

        // Relay path never loads WinForms
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RunIsland()
        {
            // Timeline starts here, before any window exists
            Boot.Mark("entry");
            // After an update: wait for the old copy to quit
            int old = 0;
            bool after = Environment.GetCommandLineArgs().Length > 2 && Environment.GetCommandLineArgs()[1] == "--after" && int.TryParse(Environment.GetCommandLineArgs()[2], out old);
            if (after)
            {
                try { using (var prev = System.Diagnostics.Process.GetProcessById(old)) prev.WaitForExit(15000); }
                catch (ArgumentException) { }
            }
            bool created;
            using (var mutex = new Mutex(true, Settings.MutexName, out created))
            {
                if (!created && !(after && Take(mutex))) return 0;
                try { Settings.MigrateLegacyDirectory(); }
                catch (IOException ex)
                {
                    MessageBox.Show(ex.Message, "Deskmate", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                // Task Scheduler launches at BelowNormal; a UI shell must not lose the CPU to normal apps
                try
                {
                    using (var self = System.Diagnostics.Process.GetCurrentProcess())
                        if (self.PriorityClass == System.Diagnostics.ProcessPriorityClass.BelowNormal || self.PriorityClass == System.Diagnostics.ProcessPriorityClass.Idle)
                            self.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
                }
                catch (Exception) { }
                // Full collections must not block a frame
                System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                Application.ThreadException += (s, e) => Log.Write("ui: " + e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Log.Write("fatal: " + e.ExceptionObject);
                    // Never leave the user without a taskbar
                    try { Taskbar.Restore(new Settings()); } catch (Exception) { }
                };
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // Opt in to .NET 4.8 accessibility, for screen reader notifications
                AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures", false);
                AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures.2", false);
                AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures.3", false);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Island());
            }
            return 0;
        }
    }
}
