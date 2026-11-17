using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace Deskmate
{
    partial class Island
    {
        // Teleprompter may stand taller than panels
        int TallPx() { return (int)Math.Ceiling((Gap + (PrompterOn ? Math.Max(MaxH, PrompterMaxH) : MaxH) + Pad + 20) * scale) + 4; }

        bool NeedTall()
        {
            float dip = Docked ? Math.Max(0, topGap.Value - Gap) : 0;
            return current == View.Panel || expanded || DateTime.UtcNow < prefetchUntilUtc || Math.Max(sh.Target, sh.Value) + dip > CompactH;
        }

        // Grow before drawing; old view kept
        bool GrowCanvas()
        {
            if (g == null || !NeedTall() || (canvasTall && canvasH >= TallPx())) return false;
            if (snapWanted) TakeSnapshot();
            canvasTall = true;
            CreateCanvas();
            frameDirty = true;
            return true;
        }

        void FreeCanvas()
        {
            if (canvas != null)
            {
                if (g != null) g.Dispose();
                canvas.Dispose();
                canvas = null;
            }
            if (memDC != IntPtr.Zero)
            {
                Native.SelectObject(memDC, oldBitmap);
                if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
                Native.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            g = null;
            if (p != null) p.G = null;
        }

        void CreateCanvas()
        {
            FreeCanvas();            canvasHalf = WantHalf;
            canvasW = (int)Math.Ceiling(2 * (canvasHalf + Pad) * scale) + 4;
            canvasH = canvasTall ? TallPx() : (int)Math.Ceiling((Gap + CompactH + Pad + 20) * scale) + 4;
            var bmi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), biWidth = canvasW, biHeight = -canvasH, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            IntPtr bits;
            dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
            dibBits = bits;
            slotCount = 0;
            liveReady = false;
            oldBitmap = Native.SelectObject(memDC, dib);
            canvas = new Bitmap(canvasW, canvasH, canvasW * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(canvas);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.AssumeLinear;
            p.G = g;
        }

        // State

        View DesiredView()
        {
            if (approvals.Count > 0) return View.Approve;
            // Speaking: nothing else matters
            if (dictState != DictState.Off) return View.Dictate;
            // Files hovering: the island becomes a target
            if (dropActive) return View.ShelfDrop;
            if (PrompterOn) return View.Prompter;
            if (MirrorOn) return View.Mirror;
            if (ConvertOn) return View.Convert;
            if (expanded) return View.Panel;
            if (toast != null && (toast.Hold || toast.Interactive || toast.Priority >= 3)) return toast.View;
            if (ComboShowing) return View.Combo;
            // Retracted: only urgent toasts pop out
            if (MiniOn() && (toast == null || !MiniLets(toast.View, toast.Priority))) return View.Mini;
            if (toast != null) return toast.View;
            return ActivityView();
        }

        // What runs underneath toasts and panel
        View ActivityView()
        {
            if (ComboShowing) return View.Combo;
            if (RapatOn || rapatWriting) return View.Record;
            if (MeetingOn) return View.Meet;
            if (TimerOn && !MediaVisible()) return View.Timer;
            if (MediaVisible()) return LyricOnIsland() ? View.Lyric : View.Media;
            if (ActiveDownload() != null) return View.Download;
            if (BusySession() != null) return View.Agent;
            if (runs.Count > 0) return View.Run;
            if (SoonTask() != null) return View.Task;
            if (ClassNow() != null) return View.Class;
            return View.Idle;
        }

        // Activity indicators on the left of the media shell
        View BubbleKind()
        {
            // Upright capsule has no room beside it
            if ((current != View.Media && current != View.Lyric) || OnSide) return View.Idle;
            // Locks first: easy to miss while typing
            if (LockShown) return View.Locks;
            if (BusySession() != null) return View.Agent;
            if (ActiveDownload() != null) return View.Download;
            if (TimerOn) return View.Timer;
            if (SoonTask() != null) return View.Task;
            return View.Idle;
        }

        bool frameDirty;

        void Kick()
        {
            if (IsDisposed || Disposing || g == null) return;
            frameDirty = true;
            if (fpsLog) CountKick();
            View want = DesiredView();
            if (want != current)
            {
                previous = current;
                current = want;
                AnnounceView(want);
                switchedUtc = DateTime.UtcNow;
                // The panel morph uses one shell and only the incoming content.
                snapWanted = !PanelTransition;
                if (!snapWanted) FreeSnapshot();
                if (want == View.Panel) panelOpenedUtc = switchedUtc;
                else if (previous == View.Panel || previous == View.Prompter) TrimSoon();
                if (want == View.Mini) EyesShown();
            }
            if (!expanded && (ctlBtScanning || ctlWifiScanning || WifiPassOpen())) StopControlScans();
            MiniSync();
            if (current == View.Panel && page == PageMusic && mediaInfo.Present) BackdropFor();
            SizeF size = Lifted(ViewSize(current));
            if (PanelTransition)
            {
                // Retarget the shell from its current position and velocity.
                bool opening = current == View.Panel;
                Tune(sw, opening ? 420 : 500, 0.96f);
                Tune(sh, opening ? 380 : 460, 0.96f);
                Tune(sr, 300, 0.96f);
            }
            else
            {
                bool growing = size.Width > sw.Value + 1;
                float zeta = current == View.Mini ? 0.95f : 0.75f;
                Tune(sw, growing ? 300 : 190, zeta);
                Tune(sh, growing ? 190 : 300, zeta);
                sr.K = 300;
                sr.D = 26;
            }
            sw.Target = size.Width;
            sh.Target = size.Height;
            GrowCanvas();
            // Script backdrop is plain: no frost jobs
            if (GlassOn && current != View.Prompter) NeedFrost(size.Height);
            if (current != View.Panel) { DropBackdrop(); DropCorner(); }
            sr.Target = ViewRadius(current, size);
            if (Anim.Reduced && PanelTransition) { sw.Snap(sw.Target); sh.Snap(sh.Target); sr.Snap(sr.Target); }
            topGap.Target = current == View.Mini && !Docked ? 0 : Gap;
            bubble.Target = BubbleKind() != View.Idle ? 1 : 0;
            opacity.Target = (fullscreen || hiddenByUser) && current != View.Approve && current != View.Dictate ? 0 : 1;
            spectrum.SetActive(VisualizerVisible() && !spectrum.Failed);
            if (!animating)
            {
                if (!ambientTimer.Enabled) lastT = clock.Elapsed.TotalSeconds;
                animating = true;
                ScheduleMotionFrame();
            }
        }

        bool PanelTransition { get { return current == View.Panel || previous == View.Panel; } }

        // Held on the pill itself
        bool shellDown;

        // Whole pill answers the pointer
        bool Swells()
        {
            if (Anim.Reduced || expanded || !pointerInside || dragId != null || bubbleRect.Contains(mouse)) return false;
            switch (current)
            {
                case View.Panel:
                case View.Approve:
                case View.Prompter:
                case View.Mirror:
                case View.Convert:
                case View.Mini:
                case View.Dictate:
                case View.ShelfDrop:
                case View.Drive:
                case View.Otp:
                case View.Remind:
                case View.Pray:
                case View.Shot: return false;
            }
            return true;
        }

        // Pointer over swells; held tucks in
        SizeF Lifted(SizeF s)
        {
            if (!Swells()) return s;
            float along = shellDown ? -4 : 8, across = shellDown ? -2 : 3;
            return UprightNow(current) ? new SizeF(s.Width + across, s.Height + along) : new SizeF(s.Width + along, s.Height + across);
        }

        // DESKMATE_FPS=1 logs rendered frames per second
        static readonly bool fpsLog = Environment.GetEnvironmentVariable("DESKMATE_FPS") == "1";
        int fpsFrames, gcSeen;
        double fpsStart, renderMs, worstMs;
        int motionIntervals;
        double lastMotionPresent, motionIntervalMs, worstMotionIntervalMs;

        void CountMotionPresent()
        {
            if (!fpsLog || !animating) return;
            double now = clock.Elapsed.TotalMilliseconds;
            if (lastMotionPresent > 0)
            {
                double interval = now - lastMotionPresent;
                motionIntervals++;
                motionIntervalMs += interval;
                worstMotionIntervalMs = Math.Max(worstMotionIntervalMs, interval);
            }
            lastMotionPresent = now;
        }

        void LogMotionPresent()
        {
            if (!fpsLog) return;
            if (motionIntervals > 0)
                Log.Write("notch motion " + current + ": " + motionIntervals + " present intervals, mean " +
                          (motionIntervalMs / motionIntervals).ToString("0.00") + "ms worst " +
                          worstMotionIntervalMs.ToString("0.00") + "ms");
            motionIntervals = 0;
            lastMotionPresent = motionIntervalMs = worstMotionIntervalMs = 0;
        }

        void CountFrame(double t, double ms)
        {
            if (!fpsLog) return;
            fpsFrames++;
            renderMs += ms;
            worstMs = Math.Max(worstMs, ms);
            string slow = Prof.EndFrame(ms);
            int gcNow = GC.CollectionCount(0);
            if (slow != null) Log.Write("slow " + ms.ToString("0.0") + "ms " + current + (current == View.Panel ? "/" + PageNames[page] : "") + (gcNow != gcSeen ? " [GC]" : "") + ": " + slow);
            gcSeen = gcNow;
            StatsLine(t);
        }

        // One stats line every two seconds
        void StatsLine(double t)
        {
            if (t - fpsStart < 2) return;
            if (fpsStart > 0)
                Log.Write("fps " + (fpsFrames / (t - fpsStart)).ToString("0.0") + " render " + (renderMs / Math.Max(1, fpsFrames)).ToString("0.00") + "ms worst " +
                          worstMs.ToString("0.0") + "ms " + current + (Moving(DateTime.UtcNow) ? " moving " : " ") + Prof.Dump(fpsFrames) + LiveStats(t - fpsStart));
            fpsFrames = 0;
            renderMs = 0;
            worstMs = 0;
            fpsStart = t;
        }

        // Stiffness scales with response squared
        static void Tune(Spring s, float k, float zeta)
        {
            k /= Anim.Pace * Anim.Pace;
            s.K = k;
            s.D = 2 * Anim.Damping(zeta) * (float)Math.Sqrt(k);
        }

        const int Stopped = 0, Ambient = 1, Motion = 2;
        const int MotionFrameMs = 16;
        readonly WinTimer ambientTimer = new WinTimer { Interval = 40 };

        void ScheduleFrame(int delay)
        {
            if (ambientTimer.Interval != delay) ambientTimer.Interval = delay;
            ambientTimer.Start();
        }

        void ScheduleMotionFrame()
        {
            if (motionPacer == null) { ScheduleFrame(MotionFrameMs); return; }
            ambientTimer.Stop();
            motionPacer.Want();
        }

        void StopMotionPacer()
        {
            if (motionPacer == null) return;
            motionPacer.Stop();
            motionPacer = null;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            StopMotionPacer();
            base.OnHandleDestroyed(e);
        }

        void MotionFrame()
        {
            var pacer = motionPacer;
            if (pacer == null) return;
            try { if (animating) AmbientTick(); }
            finally { pacer.Done(pacer == motionPacer && animating && !IsDisposed && !Disposing); }
        }

        // Ambient work uses the timer. Motion frames are posted by the compositor pacer.
        void AmbientTick()
        {
            if (IsDisposed || Disposing) { ambientTimer.Stop(); return; }
            bool wasAnimating = animating;
            int mode = Frame();
            inMotion = animating = mode == Motion;
            if (inMotion)
            {
                lastMotionUtc = DateTime.UtcNow;
                ScheduleMotionFrame();
            }
            else if (mode == Stopped) ambientTimer.Stop();
            else ScheduleFrame(AmbientWait(wholeLive));
            if (wasAnimating && !animating)
            {
                LogMotionPresent();
                RestTimer();
            }
        }

        // Live content paces; battery saver slows
        int AmbientMs()
        {
            int ms = 50;
            if (VisualizerVisible() || current == View.Lyric || PrompterRunning() || (current == View.Panel && page == PageMusic && mediaInfo.Playing)) ms = 40;
            else if (current == View.Media && mediaInfo.Playing && !settings.GetBool("visualizer", true)) ms = 250;
            Native.SYSTEM_POWER_STATUS ps;
            if (Native.GetSystemPowerStatus(out ps) && ps.SystemStatusFlag == 1) ms = ms * 3 / 2;
            return ms;
        }

        DateTime lastMotionUtc = DateTime.UtcNow;


        int Frame()
        {
            var now = DateTime.UtcNow;
            double t = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, Math.Max(0.001, t - lastT));
            lastT = t;
            sw.Step(dt); sh.Step(dt); sr.Step(dt); bubble.Step(dt); opacity.Step(dt); topGap.Step(dt);
            StepPlace(dt);
            StepPanel(dt);
            StepPrompter(dt);
            StepViews(dt, now);
            StepSwitches(dt);
            collapse.Step(dt);
            tabNudge.Step(dt);
            float flipTarget = current == View.Approve ? 1 : 0;
            if (flipLinear != flipTarget) flipLinear = flipTarget > flipLinear ? Math.Min(1, flipLinear + dt / 0.55f) : Math.Max(0, flipLinear - dt / 0.45f);
            flip = Ease.InOutCubic(flipLinear);

            if (opacity.Target == 0 && opacity.Value < 0.01f)
            {
                if (windowShown) { Native.ShowWindow(Handle, Native.SW_HIDE); windowShown = false; }
                if (islandBackdrop != null) islandBackdrop.HideBackdrop();
                return Stopped;
            }
            // Locked or screen off: draw nothing
            if (dormant) { if (islandBackdrop != null) islandBackdrop.HideBackdrop(); frameDirty = true; return Stopped; }
            if (!windowShown)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                RaiseIsland();
                windowShown = true;
                NudgeFrost();
            }

            GrowCanvas();
            // Shrink wide canvas once shell fits
            if (!PrompterOn && canvasHalf > BaseHalf && sw.Settled && sw.Value / 2 + (bubble.Value > 0.01f ? BubbleGap + BubbleR * 2 : 0) <= BaseHalf) CreateCanvas();
            else if (canvasTall && !PrompterOn && sw.Settled && sh.Settled && snapshot == null && !FlyersMoving(now) && !NeedTall()) { canvasTall = false; CreateCanvas(); }
            bool moving = Moving(now);
            wholeLive = !moving && Live(now);
            // Prompter redraws only when its picture changes
            if (current == View.Prompter && !frameDirty && !moving && PrompterKey() == prompterDrawn) return wholeLive ? Ambient : Stopped;
            // Otherwise only due live areas redraw
            if (!frameDirty && !moving && liveReady && wholePeriod <= 0 && !wholeLive)
            {
                frameT = t;
                LiveFrame(now, t);
                DumpFrame();
                return LiveMode();
            }
            CountWhy(frameDirty ? 0 : moving ? 1 : !liveReady ? 2 : wholePeriod > 0 ? 3 : 4);
            frameDirty = false;
            liveRecord = !moving && LiveAllowed();
            frameT = t;
            if (current == View.Prompter) prompterDrawn = PrompterKey();
            try { Render(now); }
            catch (Exception ex) { liveRecord = false; slotCount = 0; Log.Write("render: " + ex); }
            // First painted frame: the startup timeline ends here
            if (!Boot.Logged) Boot.Ready();
            inRender = false;
            liveReady = liveRecord;
            if (liveRecord)
            {
                // Panel settled: bake cover backdrop now
                if (current == View.Panel && mediaInfo.Present) BackdropFor();
                CollectAtRest();
            }
            CountFrame(t, (clock.Elapsed.TotalSeconds - t) * 1000);
            PilotFrame();
            DumpFrame();
            if (Moving(now)) return Motion;
            // Just settled: record live areas next
            if (!liveRecord && liveInline > 0 && LiveAllowed()) return Motion;
            wholeLive = Live(now);
            return wholeLive || wholePeriod > 0 ? Ambient : LiveMode();
        }

        bool wholeLive;

        bool Moving(DateTime now)
        {
            return !sw.Settled || !sh.Settled || !sr.Settled || !bubble.Settled || !opacity.Settled || !topGap.Settled || PlaceMoving() ||
                   (flipLinear != 0 && flipLinear != 1) || (now - switchedUtc).TotalSeconds < (PanelTransition ? (Anim.Reduced ? 0 : 0.22) : 0.5) || snapshot != null ||
                   (toast != null && (now - toast.ShownUtc).TotalSeconds < 1.2) || PanelMoving() || ViewsMoving(now) || (current == View.Idle && clockRoller.Moving(now)) || ApproveArming(now) ||
                   ThemeMoving(now) ||
                   FlyersMoving(now) || SwitchesMoving() || prompterSizeMoved || !collapse.Settled || !tabNudge.Settled || brightRoller.Moving(now) || !brightBar.Settled || EdgeMoving(now);
        }
    }
}
