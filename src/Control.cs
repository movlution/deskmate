using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

namespace Deskmate
{
    // Kontrol tab: sound, screen, radios, quiet, timers
    partial class Island
    {
        readonly BrightnessService brightness = new BrightnessService();
        readonly RadioService radios = new RadioService();
        readonly BtScanner ctlBtScanner = new BtScanner();
        List<AudioDevice> audioDevices = new List<AudioDevice>();
        string audioDefault = "";
        int controlMode, prevControlMode;
        bool devicesOpen
        {
            get { return controlMode == 1; }
            set
            {
                int next = value ? 1 : 0;
                if (controlMode != next) { prevControlMode = controlMode; controlMode = next; }
            }
        }
        DateTime devicesUtc = DateTime.MinValue, audioToastUtc = DateTime.MinValue, startedUtc = DateTime.UtcNow;
        int brightLevel = -1;
        readonly Spring brightBar = new Spring(0.5f, 320, 30, 0.001f);
        readonly Roller brightRoller = new Roller();
        readonly Dictionary<string, Spring> switches = new Dictionary<string, Spring>();

        // Inline Wi-Fi & Bluetooth sub-view state
        List<Wifi.Net> ctlWifiNets = new List<Wifi.Net>();
        Wifi.ScanStatus ctlWifiStatus = Wifi.ScanStatus.Ok;
        bool ctlWifiScanning;
        string ctlWifiMsg;
        WifiPassBox wifiPassBox;
        List<BtDevice> ctlBtDevices = new List<BtDevice>();
        bool ctlBtScanning, ctlBtBusy;
        string ctlBtStatusCode = "", ctlBtMsg;

        // Quiet mode: only urgent toasts pass
        bool Quiet { get { return QuietSwitch || (settings.GetBool("dnd.follow", false) && Native.DoNotDisturb()); } }

        // Deskmate's own switch, what the tile shows
        bool QuietSwitch { get { return settings.GetBool("dnd", false); } }

        void StartControls()
        {
            brightness.Changed += (level, own) => Post(() => OnBrightness(level, own));
            radios.Changed += () => Post(Kick);
            volume.DevicesChanged += (list, def) => Post(() => { audioDevices = list; audioDefault = def; Kick(); });
            volume.DefaultChanged += d => Post(() => { TickBluetooth(true); OnAudioDevice(d); });
            ctlBtScanner.Updated += delegate (List<BtDevice> list, bool scan, string code)
            {
                Post(delegate
                {
                    if (IsDisposed) return;
                    ctlBtDevices = SortBtDevices(list);
                    ctlBtScanning = scan;
                    ctlBtStatusCode = code ?? "";
                    Kick();
                });
            };
            brightness.Start();
            radios.Start();
        }

        void StopControlScans()
        {
            ctlWifiScanning = false;
            ctlBtScanning = false;
            try { ctlBtScanner.StopScan(); } catch (Exception) { }
            CloseWifiPassBox();
        }

        void DisposeControls()
        {
            StopControlScans();
            try { ctlBtScanner.Dispose(); } catch (Exception) { }
        }

        bool WifiPassOpen()
        {
            return wifiPassBox != null && !wifiPassBox.IsDisposed && wifiPassBox.Visible;
        }

        void CloseWifiPassBox()
        {
            var box = wifiPassBox;
            wifiPassBox = null;
            if (box != null && !box.IsDisposed)
            {
                try { box.Close(); } catch (Exception) { }
            }
        }

        void SwitchControlMode(int mode)
        {
            if (controlMode == mode) return;
            prevControlMode = controlMode;
            controlMode = mode;
            devicesUtc = DateTime.UtcNow;
            controlScroll.Snap(0);
            if (mode == 2) StartControlWifiScan();
            else if (mode == 3) StartControlBtScan();
            else StopControlScans();
            if (mode != 2) CloseWifiPassBox();
            if (mode == 1)
            {
                volume.RefreshDevices();
                volume.RefreshApps();
                appsAskedTick = Environment.TickCount;
            }
            Kick();
        }

        void OnBrightness(int level, bool own)
        {
            bool first = brightLevel < 0;
            brightLevel = level;
            brightBar.Target = level / 100f;
            brightRoller.Set(level.ToString());
            if (first) { brightBar.Snap(level / 100f); return; }
            // Brightness keys: HUD like volume
            if (!own && !expanded) ShowToast(View.Brightness, 1.5, 1, false, true);
            Kick();
        }

        AudioDevice audioShown;

        void OnAudioDevice(AudioDevice d)
        {
            // Chip follows the new default
            audioDefault = d.Id;
            volume.RefreshDevices();
            // Startup and resume reshuffle devices quietly
            if ((DateTime.UtcNow - startedUtc).TotalSeconds < 8 || (DateTime.UtcNow - audioToastUtc).TotalSeconds < 2) return;
            audioToastUtc = DateTime.UtcNow;
            audioShown = d;
            var t = ShowToast(View.AudioDevice, 2.6, 2, false, true);
            if (t != null) t.Data = d;
            Kick();
        }

        static char DeviceGlyph(int form)
        {
            switch (form)
            {
                case 3: return '';  // headphones
                case 5: return '';  // headset
                case 9: return '';  // display
                default: return ''; // speakers
            }
        }

        AudioDevice DefaultDevice()
        {
            foreach (var d in audioDevices) if (d.Id == audioDefault) return d;
            return null;
        }

        string DefaultDeviceName()
        {
            var d = DefaultDevice();
            return d != null ? d.Name : "";
        }

        char VolumeGlyph() { return muted ? Ink.IcoMute : vol < 0.01f ? Ink.IcoVol0 : vol < 0.34f ? Ink.IcoVol1 : vol < 0.67f ? Ink.IcoVol2 : Ink.IcoVol3; }

        // Page

        // Grid: three slider rows beside the radios
        const float CtlRowH = 56, CtlTimerH = 100, CtlDeviceRow = 40, CtlToggleD = 50;
        const float CtlGridH = 3 * CtlRowH + 2 * Ink.ModuleGap;
        const float CtlSubHeadH = 44, CtlSubRowH = 44, CtlSubRowGap = 2, CtlSubRowPitch = 46;
        static readonly string[] DevIds = { "dev0", "dev1", "dev2", "dev3", "dev4" }, ChipIds = { "chip0", "chip1", "chip2", "chip3", "chip4" };
        static readonly string[] ChipNames = { "Fokus 25", "5 mnt", "10 mnt", "15 mnt", "Stopwatch" };
        // Delegates built once, never per frame
        Action actMute, actDevices, actWifi, actWifiMenu, actBt, actBtMenu, actQuiet, actHide, actAwake, actAwakeMenu, actDark, actVolStep, actBrightStep, actTmPause, actTmAdd, actTmLap, actTmStop;
        Action actCtlBack, actWifiRescan, actWifiSettings, actWifiPowerOn, actWifiLoc, actBtRescan, actBtSettings, actBtPowerOn;
        Action<float> setVol, setBright;
        Action[] chipActs, devActs = new Action[0];
        List<AudioDevice> devActsFor;
        List<Wifi.Net> ctlWifiActsFor;
        Action[] ctlWifiRowActs = new Action[0], ctlWifiPillActs = new Action[0];
        string[] ctlWifiRowIds = new string[0], ctlWifiPillIds = new string[0];
        List<BtDevice> ctlBtActsFor;
        Action[] ctlBtRowActs = new Action[0], ctlBtPillActs = new Action[0], ctlBtMenuActs = new Action[0];
        string[] ctlBtRowIds = new string[0], ctlBtPillIds = new string[0];
        // Timer text redone once a second
        int tmShownS = -1, tmLabelKey = -1, tmLapCount = -1;
        string tmLabel = "", tmLap = "";
        // Live ring and countdown read these
        Action<DateTime> ctlRingPaint;
        Func<DateTime, double> ctlClockPaint;
        float ctlRingX, ctlRingY, ctlClockX, ctlClockY;
        Color ctlRingC, ctlClockC;
        // Own roller: shell Moving ignores it
        readonly Roller ctlRoller = new Roller();
        // Device name split once per change
        string devSplitFor, devTitle = "", devSub = "";

        void ControlActs()
        {
            if (actMute != null) return;
            actMute = delegate { volume.SetMute(!muted); };
            actDevices = delegate { SwitchControlMode(controlMode == 1 ? 0 : 1); };
            actWifi = delegate { if (radios.Denied) OpenSettings("ms-settings:network-wifi"); else radios.Toggle(true); };
            actWifiMenu = delegate { SwitchControlMode(2); };
            actBt = delegate { if (radios.Denied) OpenSettings("ms-settings:bluetooth"); else radios.Toggle(false); };
            actBtMenu = delegate { SwitchControlMode(3); };
            actQuiet = delegate { settings.Toggle("dnd", false); Kick(); };
            actHide = delegate { ToggleHidden(); };
            actAwake = ToggleAwake;
            actAwakeMenu = AwakeMenu;
            actDark = ToggleWindowsDark;
            actCtlBack = delegate { SwitchControlMode(0); };
            actWifiRescan = delegate { ctlWifiMsg = null; StartControlWifiScan(); };
            actWifiSettings = delegate { OpenSettings("ms-settings:network-wifi"); };
            actWifiPowerOn = delegate
            {
                radios.SetPower(true, true);
                ctlWifiMsg = Lang.T("Mencari jaringan…");
                Kick();
                ControlDelay(1200, delegate
                {
                    if (controlMode == 2)
                    {
                        radios.Refresh();
                        ctlWifiMsg = null;
                        StartControlWifiScan();
                    }
                });
            };
            actWifiLoc = delegate { OpenSettings("ms-settings:privacy-location"); };
            actBtRescan = delegate { ctlBtMsg = null; StartControlBtScan(); };
            actBtSettings = delegate { OpenSettings("ms-settings:bluetooth"); };
            actBtPowerOn = delegate
            {
                radios.SetPower(false, true);
                ctlBtMsg = null;
                Kick();
                ControlDelay(1200, delegate
                {
                    if (controlMode == 3)
                    {
                        radios.Refresh();
                        StartControlBtScan();
                    }
                });
            };
            setVol = delegate (float f)
            {
                vol = f;
                volBar.Target = f;
                volBar.Snap(f);
                volume.SetLatest(f);
            };
            setBright = delegate (float f)
            {
                brightLevel = (int)Math.Round(f * 100);
                brightBar.Snap(f);
                brightness.Set(brightLevel);
            };
            actVolStep = delegate { setVol(StepUp(vol)); };
            actBrightStep = delegate { setBright(StepUp(brightBar.Target)); };
            actTmPause = delegate { ToggleTimerPause(); };
            actTmAdd = delegate { AddTimerMinute(); };
            actTmLap = delegate { Lap(); };
            actTmStop = delegate { StopTimer(); };
            chipActs = new Action[]
            {
                delegate { StartTimer(TimerKind.Focus, 25); },
                delegate { StartTimer(TimerKind.Countdown, 5); },
                delegate { StartTimer(TimerKind.Countdown, 10); },
                delegate { StartTimer(TimerKind.Countdown, 15); },
                delegate { StartTimer(TimerKind.Stopwatch, 0); },
            };
            ctlRingPaint = t => Ring(ctlRingX, ctlRingY, 24f, TimerFraction(), ctlRingC, 4f);
            ctlClockPaint = t =>
            {
                int secs = (int)(timerKind == TimerKind.Stopwatch ? Elapsed().TotalSeconds : Math.Ceiling(Remaining().TotalSeconds));
                if (secs != tmShownS) { tmShownS = secs; ctlRoller.Set(TimerClock()); }
                ctlRoller.Draw(p, Ink.Hero, ctlClockC, ctlClockX, ctlClockY, StringAlignment.Near, t);
                if (ctlRoller.Moving(t)) return 1 / 60.0;
                return TimerOn && !timerPaused ? TimerWait(t) : 0;
            };
        }

        void ControlDelay(int ms, Action act)
        {
            var t = new WinTimer { Interval = ms };
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                if (!IsDisposed) act();
            };
            t.Start();
        }

        // Enter: next tenth, wraps at full
        static float StepUp(float v) { return v >= 0.995f ? 0 : Math.Min(1, (float)Math.Floor(v * 10 + 1.0001f) / 10); }

        void DeviceActs()
        {
            if (ReferenceEquals(devActsFor, audioDevices)) return;
            devActsFor = audioDevices;
            devActs = new Action[Math.Min(5, audioDevices.Count)];
            for (int i = 0; i < devActs.Length; i++)
            {
                string devId = audioDevices[i].Id;
                devActs[i] = delegate { volume.SetDefault(devId); SwitchControlMode(0); };
            }
        }

        void SyncWifiActs()
        {
            if (ReferenceEquals(ctlWifiActsFor, ctlWifiNets)) return;
            ctlWifiActsFor = ctlWifiNets;
            int n = ctlWifiNets != null ? ctlWifiNets.Count : 0;
            ctlWifiRowActs = new Action[n];
            ctlWifiPillActs = new Action[n];
            ctlWifiRowIds = new string[n];
            ctlWifiPillIds = new string[n];
            for (int i = 0; i < n; i++)
            {
                var item = ctlWifiNets[i];
                ctlWifiRowIds[i] = "ctlW" + i;
                ctlWifiPillIds[i] = "ctlWAct" + i;
                ctlWifiRowActs[i] = delegate
                {
                    if (item.Connected)
                    {
                        if (net.Current.Captive) OpenSettings("http://www.msftconnecttest.com/redirect");
                        return;
                    }
                    if (item.Known || item.Open) ControlWifiJoin(item, null);
                    else if (item.Personal) OpenWifiPassBox(item);
                    else OpenSettings("ms-settings:network-wifi");
                };
                ctlWifiPillActs[i] = delegate
                {
                    if (item.Connected) ControlWifiDisconnect(item);
                    else if (item.Known || item.Open) ControlWifiJoin(item, null);
                    else if (item.Personal) OpenWifiPassBox(item);
                    else OpenSettings("ms-settings:network-wifi");
                };
            }
        }

        void SyncBtActs()
        {
            if (ReferenceEquals(ctlBtActsFor, ctlBtDevices)) return;
            ctlBtActsFor = ctlBtDevices;
            int n = ctlBtDevices != null ? ctlBtDevices.Count : 0;
            ctlBtRowActs = new Action[n];
            ctlBtPillActs = new Action[n];
            ctlBtMenuActs = new Action[n];
            ctlBtRowIds = new string[n];
            ctlBtPillIds = new string[n];
            for (int i = 0; i < n; i++)
            {
                var item = ctlBtDevices[i];
                ctlBtRowIds[i] = "ctlBt" + i;
                ctlBtPillIds[i] = "ctlBtAct" + i;
                ctlBtRowActs[i] = delegate
                {
                    if (!item.Connected) ControlBtAction(item, item.Paired ? "connect" : "pair");
                };
                ctlBtPillActs[i] = delegate
                {
                    if (item.Connected) ControlBtAction(item, "disconnect");
                    else ControlBtAction(item, item.Paired ? "connect" : "pair");
                };
                ctlBtMenuActs[i] = delegate { ControlBtMenu(item); };
            }
        }

        float DeviceListH()
        {
            int n = Math.Min(5, audioDevices.Count);
            return n == 0 ? 56 : n * CtlDeviceRow + 12;
        }

        int ControlSubRows()
        {
            if (controlMode == 2)
            {
                bool noAdapter = !radios.HasWifi || ctlWifiStatus == Wifi.ScanStatus.NoAdapter;
                bool radioOff = !noAdapter && (!radios.WifiOn || ctlWifiStatus == Wifi.ScanStatus.RadioOff);
                bool denied = !noAdapter && !radioOff && ctlWifiStatus == Wifi.ScanStatus.AccessDenied;
                if (noAdapter || radioOff || denied) return 0;
                return ctlWifiNets.Count;
            }
            if (controlMode == 3)
            {
                bool noAdapter = !radios.HasBluetooth || ctlBtStatusCode == "no_adapter";
                bool radioOff = !noAdapter && (!radios.BluetoothOn || ctlBtStatusCode == "off");
                bool denied = !noAdapter && !radioOff && ctlBtStatusCode == "denied" && ctlBtDevices.Count == 0;
                if (noAdapter || radioOff || denied) return 0;
                return ctlBtDevices.Count;
            }
            return 0;
        }

        float ControlSubContentH()
        {
            int rows = ControlSubRows();
            return rows <= 0 ? 0 : rows * CtlSubRowPitch - CtlSubRowGap;
        }

        float ControlSubViewH()
        {
            float listH = ControlHeight() - (PanelTop + 2 + CtlSubHeadH) - Ink.ModuleInset;
            return Math.Max(0, listH - 4);
        }

        float ControlHeight()
        {
            if (controlMode == 2 || controlMode == 3)
                return Clamp(PanelTop + 2 + CtlSubHeadH + ControlSubRows() * CtlSubRowPitch + 14, 240, 356);
            float h = PanelTop + 2 + (controlMode == 1 ? CtlRowH + Ink.ModuleGap + DeviceListH() + MixerH() : CtlGridH);
            return Math.Min(MaxH, h + Ink.ModuleInset);
        }

        DateTime devicesAskedUtc = DateTime.MinValue;

        // Control Center: radios, sliders, output, timer, and inline Wi-Fi/BT
        void DrawControlPage(float w, float h, DateTime now)
        {
            ControlActs();
            // Chip needs the device name
            if (audioDevices.Count == 0 && (now - devicesAskedUtc).TotalSeconds > 30) { devicesAskedUtc = now; volume.RefreshDevices(); }
            float x0 = Ink.ModuleInset, full = w - 2 * x0, y = PanelTop + 2, keep = p.Alpha;
            float k = Ease.OutCubic((float)((now - devicesUtc).TotalSeconds / 0.3));
            bool hold = hitsOn;
            if (k < 1 && prevControlMode != controlMode)
            {
                hitsOn = false;
                p.Alpha = keep * (1 - Ease.Clamp01(k * 1.6f));
                DrawControlMode(prevControlMode, x0, y, full, h, now, false);
                hitsOn = hold;
            }
            p.Alpha = keep * Ease.Clamp01(k * 1.2f);
            if (k < 0.7f) hitsOn = false;
            float rise = Anim.Reduced ? 0 : (1 - k) * 8;
            DrawControlMode(controlMode, x0, y + rise, full, h, now, k >= 1);
            hitsOn = hold;
            p.Alpha = keep;
        }

        void DrawControlMode(int mode, float x0, float y, float full, float h, DateTime now, bool live)
        {
            switch (mode)
            {
                case 1: DrawDeviceMode(x0, y, full); break;
                case 2: DrawControlWifi(x0, y, full, h, now); break;
                case 3: DrawControlBluetooth(x0, y, full, h, now); break;
                default: DrawControlGrid(x0, y, full, now, live); break;
            }
        }

        // Radios left; brightness, sound, output right
        void DrawControlGrid(float x0, float y, float full, DateTime now, bool live)
        {
            float gap = Ink.ModuleGap, col = (full - gap) / 2, x1 = x0 + col + gap, keep = p.Alpha, dy, ry = y;
            p.Alpha = keep * RowIn(0, now, out dy);
            DrawToggles(new RectangleF(x0, y + dy, col, CtlGridH));
            if (brightness.Supported)
            {
                p.Alpha = keep * RowIn(1, now, out dy);
                BigSlider(new RectangleF(x1, ry + dy, col, CtlRowH), "ctlBright", brightBar.Value, Ink.IcoSun, Lang.T("Layar"),
                          Ink.Percent(Math.Max(0, brightLevel)), setBright, actBrightStep, null, null);
                ry += CtlRowH + gap;
            }
            // Sound takes the brightness row when absent
            float sh = CtlGridH - CtlRowH - gap - (ry - y);
            p.Alpha = keep * RowIn(2, now, out dy);
            SoundSlider(new RectangleF(x1, ry + dy, col, sh));
            p.Alpha = keep * RowIn(3, now, out dy);
            DeviceChip(new RectangleF(x1, y + CtlGridH - CtlRowH + dy, col, CtlRowH));
            p.Alpha = keep;
        }

        // Sound and output on top, devices then apps
        void DrawDeviceMode(float x0, float y, float full)
        {
            float gap = Ink.ModuleGap, col = (full - gap) / 2;
            SoundSlider(new RectangleF(x0, y, col, CtlRowH));
            DeviceChip(new RectangleF(x0 + col + gap, y, col, CtlRowH));
            DrawDeviceList(new RectangleF(x0, y + CtlRowH + gap, full, DeviceListH()));
            DrawMixer(x0, y + CtlRowH + gap + DeviceListH() + gap, full);
        }

        void DrawControlSubHeader(float x0, float y, float full, char icon, string title, bool scanning, string msg, string rescanId, Action rescanAct, string settingsId, Action settingsAct)
        {
            float hy = y + 4, cy = hy + 16;
            string backLabel = Lang.T("Kembali");
            float backW = Math.Max(76, p.Measure(backLabel, Ink.Semi(12.5f)) + 28);
            var backR = new RectangleF(x0, hy, backW, 32);
            AddHit(backR, "ctlBack", actCtlBack);
            float bs = Squish("ctlBack", backR, 0.94f);
            p.Module(backR, 16, Pressed("ctlBack") ? p.T.FillPressed : Hover("ctlBack") ? p.T.FillHover : p.T.Fill);
            p.StrokeRound(backR, 16, p.T.Line, 1f);
            p.Icon((char)0xE76B, 10f, Hover("ctlBack") ? p.T.Text : p.T.Sub, backR.X + 13, cy);
            p.Text(backLabel, Ink.Semi(12.5f), Hover("ctlBack") ? p.T.Text : p.T.Sub, backR.X + 23, cy, backR.Width - 30, StringAlignment.Near);
            Unsquish(backR, bs);

            float rightX = x0 + full;
            IconButton(rightX - 16, cy, 32, settingsId, '\uE713', settingsAct, false);
            IconButton(rightX - 52, cy, 32, rescanId, '\uE72C', rescanAct, false);

            float titleX = backR.Right + 12;
            p.Icon(icon, 15f, p.T.Text, titleX + 8, cy);
            p.Text(title, Ink.Headline, p.T.Text, titleX + 22, cy, 120, StringAlignment.Near);

            float titleEnd = titleX + 22 + Math.Min(120, p.Measure(title, Ink.Headline)) + 10;
            float statusRight = rightX - 72;
            float avail = statusRight - titleEnd;
            if (avail > 32)
            {
                if (scanning)
                {
                    string sText = Lang.T("Mencari…");
                    float sw = Math.Min(avail - 18, p.Measure(sText, Ink.Caption));
                    float ox = statusRight - sw - 10;
                    p.Orbit(ox, cy, 5.5f, orbitPhase, p.T.Sub);
                    p.Text(sText, Ink.Caption, p.T.Sub, ox + 10, cy, sw + 2, StringAlignment.Near);
                }
                else if (!string.IsNullOrEmpty(msg))
                {
                    p.Text(msg, Ink.Caption, p.T.Sub, statusRight, cy, avail, StringAlignment.Far);
                }
            }
        }

        void DrawControlSubNotice(RectangleF area, char icon, string title, string actionLabel, string actionId, Action actionAct)
        {
            bool hasAct = !string.IsNullOrEmpty(actionLabel) && actionAct != null;
            float block = hasAct ? 36 + 10 + 20 + 12 + 30 : 36 + 10 + 20;
            float top = area.Y + Math.Max(10, (area.Height - block) / 2);
            float cx = area.X + area.Width / 2;
            var circle = new RectangleF(cx - 18, top, 36, 36);
            p.Module(circle, 18, p.T.Fill);
            p.Icon(icon, 16f, p.T.Sub, cx, top + 18);
            p.TextCenter(title, Ink.Semi(13f), p.T.Text, cx, top + 36 + 10 + 10);
            if (hasAct)
            {
                float pw = Math.Max(130, p.Measure(actionLabel, Ink.Semi(12.5f)) + 32);
                Pill(new RectangleF(cx - pw / 2, top + block - 30, pw, 30), actionId, actionLabel, PillKind.Primary, actionAct);
            }
        }

    }
}
