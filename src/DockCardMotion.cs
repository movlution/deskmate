using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Deskmate
{
    enum DockWindowAction { Open, Launch, Minimize, Close }

    // A snapshot lets the compositor move the card after the source has gone.
    sealed class DockCardMotion : IDisposable
    {
        public const string Setting = "dockbar.cardmotion";
        const int RestoreGraceMs = 240;
        const int ExitGraceMs = 120;
        readonly IntPtr source;
        readonly Rectangle icon;
        readonly DockWindowAction action;
        Rectangle card;
        double started = -1, commandSent = -1, captureRequested;
        DockMinimizeSnapshot snapshot;
        Bitmap openingFallback;
        Action launch;
        const int SnapshotBudgetMs = 90;
        const int OpenSnapshotBudgetMs = 180;
        const float OpenDurationMs = 245f, RestoreDurationMs = 300f, LaunchDurationMs = 225f, ExitDurationMs = 285f;
        const int FocusPreviewBudgetMs = 34;
        internal const float RestoreReveal = 0.66f;
        internal const float DeckContact = 0.88f;
        bool contactReported, stagedRestore, nativeMinimize, retainNativePreview;
        float Duration
        {
            get
            {
                if (action == DockWindowAction.Launch) return Anim.Response(LaunchDurationMs);
                if (action == DockWindowAction.Open) return Anim.Response(stagedRestore ? RestoreDurationMs : OpenDurationMs);
                return Anim.Response(ExitDurationMs);
            }
        }
        public bool UsesCompositor { get { return started >= 0 && snapshot.Composited; } }
        static readonly bool logTiming = Environment.GetEnvironmentVariable("DESKMATE_FPS") == "1";
        double lastFrame, worstGap, worstPresent;
        long stepStamp;
        int frameCount;

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Native.RECT value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);

        public static bool Enabled(Settings settings)
        {
            int animations;
            return !Anim.Reduced && settings != null && settings.GetBool(Setting, true)
                && (!SystemParametersInfo(0x1042, 0, out animations, 0) || animations != 0);
        }

        public static Rectangle CardBounds(IntPtr hwnd, Rectangle monitor)
        {
            if (Native.IsFullscreenWindow(hwnd, Native.MonitorFromWindow(hwnd, 2))) return Rectangle.Empty;
            Native.RECT r;
            const int ExtendedFrameBounds = 9;
            if (DwmGetWindowAttribute(hwnd, ExtendedFrameBounds, out r, Marshal.SizeOf(typeof(Native.RECT))) < 0
                && !Native.GetWindowRect(hwnd, out r)) return Rectangle.Empty;
            return FitCardBounds(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), monitor, ShellApp.IsMaximized(hwnd));
        }

        internal static Rectangle FitCardBounds(Rectangle bounds, Rectangle monitor, bool maximized)
        {
            if (maximized && monitor.Contains(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2))
                bounds = Rectangle.Intersect(bounds, monitor); // Win32 fallback can include invisible resize borders.
            return bounds.Width >= 160 && bounds.Height >= 100 && monitor.Contains(bounds) ? bounds : Rectangle.Empty;
        }

        public DockCardMotion(IntPtr source, Rectangle icon, Rectangle card, DockWindowAction action)
        {
            this.source = source;
            this.icon = icon;
            this.card = card;
            this.action = action;
        }

        internal static bool CanProxy(IntPtr hwnd)
        {
            int cloaked;
            uint affinity, pid;
            return Native.GetWindowThreadProcessId(hwnd, out pid) != 0 && pid != 0 && !ShellApp.Elevated((int)pid)
                && !(DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0)
                && !(GetWindowDisplayAffinity(hwnd, out affinity) && affinity != 0);
        }

        public bool QueueSnapshot()
        {
            snapshot = new DockMinimizeSnapshot();
            return snapshot.Queue(source, card);
        }

        public void ReuseSnapshot(Bitmap bitmap, Rectangle bounds, double requested, bool retain = true)
        {
            snapshot = new DockMinimizeSnapshot(bitmap);
            card = bounds;
            captureRequested = requested;
            stagedRestore = action == DockWindowAction.Open;
            nativeMinimize = action == DockWindowAction.Minimize;
            retainNativePreview = retain;
        }

        public bool Handles(IntPtr window) { return source == window; }
        public bool PendingFocus { get { return action == DockWindowAction.Open && stagedRestore && commandSent < 0; } }
        public bool Launching { get { return action == DockWindowAction.Launch; } }

        public void StageFocus(Bitmap fallback, double requested, bool tryCapture)
        {
            stagedRestore = true;
            captureRequested = requested;
            if (tryCapture && QueueSnapshot()) openingFallback = fallback;
            else
            {
                if (snapshot != null) snapshot.Dispose();
                snapshot = new DockMinimizeSnapshot(fallback);
            }
        }

        public void StageLaunch(Bitmap image, Action start)
        {
            snapshot = new DockMinimizeSnapshot(image);
            launch = start;
        }

        public Bitmap TakeRestoreImage(out IntPtr window, out Rectangle bounds)
        {
            window = source;
            bounds = card;
            return action == DockWindowAction.Minimize && started >= 0 && ShellApp.IsMinimized(source)
                ? snapshot.TakeRestoreImage() : null;
        }

        public bool CancelUnsentMinimize(IntPtr hwnd)
        {
            if (action != DockWindowAction.Minimize || source != hwnd || commandSent >= 0) return false;
            commandSent = 0;
            return true;
        }

        public void FinishPendingCommand(bool commitFocus = true)
        {
            if (commandSent >= 0 || (action == DockWindowAction.Open && !stagedRestore)) return;
            commandSent = 0;
            if (action == DockWindowAction.Launch) { launch(); return; }
            if (!ShellApp.WindowExists(source)) return;
            if (stagedRestore)
            {
                if (commitFocus) ShellApp.FocusDockApp(source);
            }
            else if (nativeMinimize) return;
            else if (action == DockWindowAction.Close) ShellApp.Close(source);
            else if (!ShellApp.IsMinimized(source)) ShellApp.Minimize(source);
        }

        internal static void DisableProxyTransitions(IntPtr hwnd)
        {
            int disabled = 1;
            DwmSetWindowAttribute(hwnd, 3, ref disabled, 4);
        }

        public bool Step(double now, double requested, Rectangle monitor, float dpi, IntPtr foreground)
        {
            stepStamp = Stopwatch.GetTimestamp();
            if (Anim.Reduced) return false;
            if (action == DockWindowAction.Launch) return StepLaunch(now, requested, monitor, dpi);
            if (action != DockWindowAction.Open) return StepExit(now, requested, monitor, dpi);
            if (stagedRestore) return StepStagedRestore(now, requested, monitor, dpi);
            if (!Native.IsWindowVisible(source)) return false;
            bool minimized = ShellApp.IsMinimized(source);
            if (started < 0)
            {
                if (minimized || foreground != source) return now - requested < RestoreGraceMs;
                if (snapshot == null)
                {
                    card = CardBounds(source, monitor);
                    captureRequested = now;
                    return !card.IsEmpty && QueueSnapshot();
                }
                double captureAge = now - captureRequested;
                if (!snapshot.Ready) return captureAge <= OpenSnapshotBudgetMs || Skip("capture deadline");
                if (captureAge > OpenSnapshotBudgetMs + 40) return Skip("capture deadline");
                var current = CardBounds(source, monitor);
                if (current != card)
                {
                    snapshot.Dispose();
                    card = current;
                    return captureAge <= OpenSnapshotBudgetMs && !card.IsEmpty && QueueSnapshot();
                }
                if (!snapshot.Prepare(card, monitor)) return Skip("snapshot preparation");
                if (!StartVisible(now, monitor, dpi, false)) return Skip("snapshot show");
            }
            if (minimized || foreground != source) return false;
            return Animate(now, requested, monitor, dpi, false);
        }

        bool StepStagedRestore(double now, double requested, Rectangle monitor, float dpi)
        {
            if (!ShellApp.WindowExists(source)) return false;
            if (started < 0)
            {
                if (!snapshot.Ready && now - captureRequested < FocusPreviewBudgetMs) return true;
                if (!snapshot.Prepare(card, monitor))
                {
                    if (openingFallback == null) return Skip("restore preparation");
                    snapshot.Dispose();
                    snapshot = new DockMinimizeSnapshot(openingFallback);
                    openingFallback = null;
                    if (!snapshot.Prepare(card, monitor)) return Skip("app card preparation");
                }
                if (!StartVisible(now, monitor, dpi, false)) return Skip("restore presentation");
            }
            if (commandSent < 0 && now - started >= RestoreReveal * Duration) FinishPendingCommand();
            return Animate(now, requested, monitor, dpi, false);
        }

        bool StepLaunch(double now, double requested, Rectangle monitor, float dpi)
        {
            if (started < 0 && (!snapshot.Prepare(card, monitor) || !StartVisible(now, monitor, dpi, false)))
                return Skip("launch card preparation");
            // Give the compositor one frame before a fast app can claim the foreground.
            if (commandSent < 0 && now - started >= 24) FinishPendingCommand();
            return Animate(now, requested, monitor, dpi, false);
        }

        bool StepExit(double now, double requested, Rectangle monitor, float dpi)
        {
            bool visible = Native.IsWindowVisible(source) && !ShellApp.IsMinimized(source);
            if (nativeMinimize)
            {
                if (visible) return started < 0 && now - requested < ExitGraceMs;
                if (!ShellApp.IsMinimized(source)) return false;
                if (started < 0 && (!snapshot.Prepare(card, monitor, retainNativePreview) || !StartVisible(now, monitor, dpi, true)))
                    return Skip("native minimize preparation");
                return Animate(now, requested, monitor, dpi, true);
            }
            if (commandSent < 0)
            {
                // The caller sends the command even when capture fails or misses its budget.
                if (!visible) return false;
                if (!snapshot.Ready) return now - requested <= SnapshotBudgetMs || Skip("capture deadline");
                if (CardBounds(source, monitor) != card) return Skip("window moved during capture");
                if (!snapshot.Prepare(card, monitor, action == DockWindowAction.Minimize)) return Skip("snapshot preparation");
                if (!snapshot.Present(card, monitor, 255)) return Skip("snapshot presentation");
                if (action == DockWindowAction.Minimize && !snapshot.Show()) return Skip("snapshot show");
                FinishPendingCommand();
                commandSent = now;
                return true;
            }
            if (visible) return started < 0 && now - commandSent < ExitGraceMs;
            if (started < 0)
            {
                if (action == DockWindowAction.Close && !snapshot.Show()) return false;
                Start(now, dpi, true);
            }
            return Animate(now, commandSent, monitor, dpi, true);
        }

        bool StartVisible(double now, Rectangle monitor, float dpi, bool exiting)
        {
            byte opacity;
            var frame = Frame(card, icon, dpi, exiting, 0, out opacity, stagedRestore || Launching);
            if (!snapshot.Present(frame, monitor, opacity) || !snapshot.Show()) return false;
            Start(now, dpi, exiting);
            return true;
        }

        void Start(double now, float dpi, bool exiting)
        {
            snapshot.Play(card, icon, dpi, exiting, Duration, stagedRestore || Launching);
            started = now + (Stopwatch.GetTimestamp() - stepStamp) * 1000.0 / Stopwatch.Frequency;
        }

        public DateTime TakeDeckContact(double now)
        {
            if ((action != DockWindowAction.Minimize && action != DockWindowAction.Close) || started < 0 || contactReported)
                return DateTime.MinValue;
            double elapsedSinceContact = now - started - DeckContact * Duration;
            if (elapsedSinceContact < 0) return DateTime.MinValue;
            contactReported = true;
            return DateTime.UtcNow.AddMilliseconds(-elapsedSinceContact);
        }

        bool Animate(double now, double requested, Rectangle monitor, float dpi, bool exiting)
        {
            double elapsed = now - started;
            if (elapsed >= Duration + (snapshot.Composited ? 16f : 0f))
            {
                ReportTiming(now, requested);
                return false;
            }
            if (snapshot.Composited) return true;
            byte opacity;
            Rectangle frame = Frame(card, icon, dpi, exiting, (float)(elapsed / Duration), out opacity, stagedRestore || Launching);
            long before = logTiming ? Stopwatch.GetTimestamp() : 0;
            bool presented = snapshot.Present(frame, monitor, opacity);
            CountFrame(now, before);
            if (!presented) return Skip("snapshot presentation");
            return true;
        }

        bool Skip(string reason)
        {
            if (logTiming) Log.Write("dock " + action + " fallback: " + reason);
            return false;
        }

        void CountFrame(double now, long before)
        {
            if (!logTiming) return;
            worstPresent = Math.Max(worstPresent, (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency);
            if (frameCount > 0) worstGap = Math.Max(worstGap, now - lastFrame);
            lastFrame = now;
            frameCount++;
        }

        void ReportTiming(double now, double sent)
        {
            if (!logTiming) return;
            if (snapshot.Composited)
            {
                Log.Write(string.Format("dock {0} motion: handoff={1:0.0}ms compositor duration={2:0.0}ms{3}",
                    action, started - sent, Duration, nativeMinimize ? " native" : stagedRestore ? " staged restore" : ""));
                return;
            }
            Log.Write(string.Format("dock {0} motion: handoff={1:0.0}ms frames={2} maxGap={3:0.0}ms maxPresent={4:0.0}ms",
                action, started - sent, frameCount, Math.Max(worstGap, now - lastFrame), worstPresent));
        }

        internal static Rectangle Frame(Rectangle card, Rectangle icon, float dpi, bool lifting, float t, out byte opacity, bool fromDock = false)
        {
            t = Ease.Clamp01(t);
            float settle = 1 - Ease.InOutCubic(t / 0.70f);
            float scale = 1 + 0.012f * settle;
            float x = card.X + card.Width * 0.5f, y = card.Y + card.Height * 0.5f;
            if (lifting)
            {
                // Move at full size first. The snapshot raster is reused until the shrink starts.
                float travel = Ease.Clamp01(t / DeckContact);
                travel = travel * travel * (3 - 2 * travel);
                float shrink = Ease.Clamp01((t - 0.30f) / (DeckContact - 0.30f));
                shrink = shrink * shrink * (3 - 2 * shrink);
                float dockScale = Math.Min(1f, Math.Min(icon.Width / (float)card.Width, icon.Height / (float)card.Height));
                scale = Ease.Lerp(1, dockScale, shrink);
                x = Ease.Lerp(x, icon.X + icon.Width * 0.5f, travel);
                y = Ease.Lerp(card.Y + card.Height * 0.5f, icon.Y + icon.Height * 0.5f, travel)
                    - 80 * dpi * travel * (1 - travel);
                opacity = (byte)Math.Round(255 * (1 - Ease.InOutCubic((t - DeckContact) / (1 - DeckContact))));
            }
            else if (fromDock)
            {
                float travel = Ease.InOutCubic(t / RestoreReveal);
                float growth = Ease.InOutCubic((t - 0.04f) / (RestoreReveal - 0.04f));
                float dockScale = Math.Min(1f, Math.Min(icon.Width / (float)card.Width, icon.Height / (float)card.Height));
                scale = Ease.Lerp(dockScale, 1, growth);
                x = Ease.Lerp(icon.X + icon.Width * 0.5f, x, travel);
                y = Ease.Lerp(icon.Y + icon.Height * 0.5f, y, travel) - 65 * dpi * travel * (1 - travel);
                opacity = (byte)Math.Round(255 * (1 - Ease.InOutCubic((t - 0.82f) / 0.18f)));
            }
            else
            {
                y -= 12 * dpi * settle;
                opacity = (byte)Math.Round(218 * (1 - Ease.InOutCubic((t - 0.48f) / 0.52f)));
            }
            int w = Math.Max(1, (int)Math.Round(card.Width * scale)), h = Math.Max(1, (int)Math.Round(card.Height * scale));
            return new Rectangle((int)Math.Round(x - w * 0.5f), (int)Math.Round(y - h * 0.5f), w, h);
        }

        internal static float DeckSpread(bool exiting, float t, bool fromDock = false)
        {
            if (fromDock) return Ease.InOutCubic(t / 0.25f) * (1 - Ease.InOutCubic((t - 0.28f) / 0.38f));
            if (!exiting) return 1 - Ease.InOutCubic(t / 0.70f);
            return Ease.InOutCubic((t - 0.22f) / 0.38f) * (1 - Ease.InOutCubic((t - 0.68f) / (DeckContact - 0.68f)));
        }

        internal static float DeckTilt(int depth, bool exiting, float direction, float t, bool fromDock = false)
        {
            float fan = DeckSpread(exiting, t, fromDock);
            return direction * fan * (depth == 0 ? (exiting ? 8f : fromDock ? -4.5f : -0.8f) : -depth * (exiting ? 6f : 2.5f));
        }

        public void Dispose()
        {
            if (snapshot != null) { snapshot.Dispose(); snapshot = null; }
            if (openingFallback != null) { openingFallback.Dispose(); openingFallback = null; }
        }
    }
}
