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
            float h = PanelTop + 2 + (controlMode == 1 ? CtlRowH + Ink.ModuleGap + DeviceListH() + MixerH() : CtlGridH + Ink.ModuleGap + CtlTimerH);
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
            p.Alpha = keep * RowIn(4, now, out dy);
            DrawTimerModule(new RectangleF(x0, y + CtlGridH + gap + dy, full, CtlTimerH), now, live);
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

        void DrawControlWifi(float x0, float y, float full, float h, DateTime now)
        {
            DrawControlSubHeader(x0, y, full, Ink.IcoWifi, Lang.T("Wi-Fi"), ctlWifiScanning, ctlWifiMsg, "ctlWifiRescan", actWifiRescan, "ctlWifiSettings", actWifiSettings);
            float listY = y + CtlSubHeadH;
            float listH = Math.Max(80, h - Ink.ModuleInset - listY);
            var listArea = new RectangleF(x0, listY, full, listH);
            Module(listArea, Ink.RadiusModule);

            bool noAdapter = !radios.HasWifi || ctlWifiStatus == Wifi.ScanStatus.NoAdapter;
            bool radioOff = !noAdapter && (!radios.WifiOn || ctlWifiStatus == Wifi.ScanStatus.RadioOff);
            bool denied = !noAdapter && !radioOff && ctlWifiStatus == Wifi.ScanStatus.AccessDenied;
            if (noAdapter) { DrawControlSubNotice(listArea, Ink.IcoWifi, Lang.T("Tidak ada adaptor Wi-Fi"), null, null, null); return; }
            if (radioOff) { DrawControlSubNotice(listArea, Ink.IcoWifi, Lang.T("Wi-Fi mati"), Lang.T("Nyalakan Wi-Fi"), "ctlWifiPowerOn", actWifiPowerOn); return; }
            if (denied) { DrawControlSubNotice(listArea, Ink.IcoLock, Lang.T("Perlu izin lokasi Windows"), Lang.T("Pengaturan Lokasi"), "ctlWifiLoc", actWifiLoc); return; }
            if (ctlWifiNets.Count == 0)
            {
                DrawControlSubNotice(listArea, Ink.IcoWifi, ctlWifiScanning ? Lang.T("Mencari jaringan…") : Lang.T("Tidak ada jaringan"), null, null, null);
                return;
            }

            SyncWifiActs();
            var clipArea = new RectangleF(listArea.X, listArea.Y + 2, listArea.Width, Math.Max(0, listArea.Height - 4));
            float maxScroll = Math.Max(0, ControlSubContentH() - clipArea.Height);
            if (controlScroll.Target > maxScroll) controlScroll.Target = maxScroll;

            var st = g.Save();
            g.IntersectClip(clipArea);
            var curNet = net.Current;
            for (int i = 0; i < ctlWifiNets.Count; i++)
            {
                float ry = clipArea.Y + i * CtlSubRowPitch - controlScroll.Value;
                if (ry + CtlSubRowH < clipArea.Top || ry > clipArea.Bottom) continue;
                var n = ctlWifiNets[i];
                var row = new RectangleF(listArea.X + 6, ry, listArea.Width - 12, CtlSubRowH);
                string rowId = ctlWifiRowIds[i], actId = ctlWifiPillIds[i];
                AddHit(row, rowId, ctlWifiRowActs[i]);
                bool rowHot = Hover(rowId) || Hover(actId) || (keyboardMode && (focusId == rowId || focusId == actId));
                if (rowHot) p.FillRound(row, 12, Pressed(rowId) ? p.T.FillPressed : p.T.FillHover);
                else if (n.Connected) p.FillRound(row, 12, Ink.A(p.T.Accent, 0.10f));

                float cy = row.Y + CtlSubRowH / 2;
                char bars = n.Signal < 25 ? (char)0xE872 : n.Signal < 50 ? (char)0xE873 : n.Signal < 75 ? (char)0xE874 : (char)0xE701;
                if (n.Signal < 75) p.Icon((char)0xE701, 15f, Ink.A(p.T.Text, 0.22f), row.X + 22, cy);
                p.Icon(bars, 15f, n.Connected ? p.T.Accent : p.T.Text, row.X + 22, cy);
                if (n.Secured && !n.Open) p.Icon((char)0xE72E, 8f, p.T.Sub, row.X + 30, cy + 6);

                string sub = n.Connected && curNet.Captive ? Lang.T("Perlu login")
                           : n.Connected && curNet.Any && !curNet.Online ? Lang.T("Tersambung, tanpa internet")
                           : n.Connected ? Lang.T("Tersambung")
                           : n.Known ? Lang.T("Tersimpan")
                           : n.Open ? Lang.T("Terbuka")
                           : n.Personal ? Lang.T("Butuh kata sandi")
                           : Lang.T("Masuk lewat Pengaturan");

                bool showAct = n.Connected || rowHot;
                string actLabel = n.Connected ? Lang.T("Putuskan") : Lang.T("Sambungkan");
                float pillW = showAct ? Math.Max(82, p.Measure(actLabel, Ink.SmallSemi(11.5f)) + 20) : 0;
                float textX = row.X + 42;
                float textW = Math.Max(60, (showAct ? row.Right - pillW - 16 : row.Right - 12) - textX);
                p.Text(n.Ssid, Ink.Semi(13f), p.T.Text, textX, cy - 8, textW, StringAlignment.Near);
                p.Text(sub, Ink.Small(11f), n.Connected ? p.T.Accent : p.T.Sub, textX, cy + 9, textW, StringAlignment.Near);

                if (showAct)
                {
                    var pill = new RectangleF(row.Right - pillW - 8, cy - 13, pillW, 26);
                    AddHit(pill, actId, ctlWifiPillActs[i]);
                    bool pHot = Hover(actId), pDown = Pressed(actId);
                    Color pFill = n.Connected
                        ? (pDown ? p.T.FillPressed : pHot ? p.T.FillHover : p.T.Fill)
                        : (pDown ? Ink.Mix(p.T.Accent, p.T.OnAccent, 0.2f) : pHot ? Ink.Mix(p.T.Accent, p.T.OnAccent, 0.1f) : p.T.Accent);
                    Color pInk = n.Connected ? (pHot ? p.T.Text : p.T.Sub) : p.T.OnAccent;
                    p.FillRound(pill, 13, pFill);
                    p.TextCenter(actLabel, Ink.SmallSemi(11.5f), pInk, pill.X + pill.Width / 2, cy);
                }
            }
            g.Restore(st);
        }

        void DrawControlBluetooth(float x0, float y, float full, float h, DateTime now)
        {
            DrawControlSubHeader(x0, y, full, Ink.IcoBluetooth, Lang.T("Bluetooth"), ctlBtScanning, ctlBtMsg, "ctlBtRescan", actBtRescan, "ctlBtSettings", actBtSettings);
            float listY = y + CtlSubHeadH;
            float listH = Math.Max(80, h - Ink.ModuleInset - listY);
            var listArea = new RectangleF(x0, listY, full, listH);
            Module(listArea, Ink.RadiusModule);

            bool noAdapter = !radios.HasBluetooth || ctlBtStatusCode == "no_adapter";
            bool radioOff = !noAdapter && (!radios.BluetoothOn || ctlBtStatusCode == "off");
            bool denied = !noAdapter && !radioOff && ctlBtStatusCode == "denied" && ctlBtDevices.Count == 0;
            if (noAdapter) { DrawControlSubNotice(listArea, Ink.IcoBluetooth, Lang.T("Tidak ada adaptor Bluetooth"), null, null, null); return; }
            if (radioOff) { DrawControlSubNotice(listArea, Ink.IcoBluetooth, Lang.T("Bluetooth mati"), Lang.T("Nyalakan Bluetooth"), "ctlBtPowerOn", actBtPowerOn); return; }
            if (denied) { DrawControlSubNotice(listArea, Ink.IcoLock, Lang.T("Akses Bluetooth ditolak"), Lang.T("Pengaturan Bluetooth"), "ctlBtDeniedSettings", actBtSettings); return; }
            if (ctlBtDevices.Count == 0)
            {
                DrawControlSubNotice(listArea, Ink.IcoBluetooth, ctlBtScanning ? Lang.T("Memindai perangkat…") : Lang.T("Tidak ada perangkat ditemukan"), null, null, null);
                return;
            }

            SyncBtActs();
            var clipArea = new RectangleF(listArea.X, listArea.Y + 2, listArea.Width, Math.Max(0, listArea.Height - 4));
            float maxScroll = Math.Max(0, ControlSubContentH() - clipArea.Height);
            if (controlScroll.Target > maxScroll) controlScroll.Target = maxScroll;

            var st = g.Save();
            g.IntersectClip(clipArea);
            for (int i = 0; i < ctlBtDevices.Count; i++)
            {
                float ry = clipArea.Y + i * CtlSubRowPitch - controlScroll.Value;
                if (ry + CtlSubRowH < clipArea.Top || ry > clipArea.Bottom) continue;
                var d = ctlBtDevices[i];
                var row = new RectangleF(listArea.X + 6, ry, listArea.Width - 12, CtlSubRowH);
                string rowId = ctlBtRowIds[i], actId = ctlBtPillIds[i];
                Action menuAct = d.Paired ? ctlBtMenuActs[i] : null;
                AddHit(row, rowId, ctlBtRowActs[i], null, menuAct);
                bool rowHot = Hover(rowId) || Hover(actId) || (keyboardMode && (focusId == rowId || focusId == actId));
                if (rowHot) p.FillRound(row, 12, Pressed(rowId) ? p.T.FillPressed : p.T.FillHover);
                else if (d.Connected) p.FillRound(row, 12, Ink.A(p.T.Accent, 0.10f));

                float cy = row.Y + CtlSubRowH / 2;
                var tile = new RectangleF(row.X + 8, cy - 14, 28, 28);
                p.Module(tile, 8, d.Connected ? Ink.A(p.T.Accent, 0.18f) : p.T.Fill);
                char glyph = d.IsAudio ? (char)0xE7F6 : (char)0xE702;
                p.Icon(glyph, 14f, d.Connected ? p.T.Accent : p.T.Text, tile.X + 14, cy);

                string actLabel = d.Connected ? Lang.T("Putuskan") : d.Paired ? Lang.T("Sambungkan") : Lang.T("Sandingkan");
                float pillW = Math.Max(82, p.Measure(actLabel, Ink.SmallSemi(11.5f)) + 20);
                var pill = new RectangleF(row.Right - pillW - 8, cy - 13, pillW, 26);
                float textX = tile.Right + 10;
                float textW = Math.Max(60, pill.X - textX - 8);

                string devName = string.IsNullOrEmpty(d.Name) ? Lang.T("Perangkat Bluetooth") : d.Name;
                p.Text(devName, Ink.Semi(13f), p.T.Text, textX, cy - 8, textW, StringAlignment.Near);

                string sub;
                if (d.Connected) sub = d.Battery >= 0 ? Lang.T("Terhubung") + " · " + Ink.Percent(d.Battery) : Lang.T("Terhubung");
                else if (d.Paired) sub = Lang.T("Tersanding");
                else sub = Lang.T("Tersedia");
                p.Text(sub, Ink.Small(11f), d.Connected ? p.T.Accent : p.T.Sub, textX, cy + 9, textW, StringAlignment.Near);

                AddHit(pill, actId, ctlBtPillActs[i], null, menuAct);
                bool pHot = Hover(actId), pDown = Pressed(actId);
                Color pFill = pDown ? p.T.FillPressed : pHot ? p.T.FillHover : (rowHot ? p.T.Fill : Ink.A(p.T.Fill, 0.8f));
                p.FillRound(pill, 13, pFill);
                p.TextCenter(actLabel, Ink.SmallSemi(11.5f), pHot ? p.T.Text : p.T.Sub, pill.X + pill.Width / 2, cy);
            }
            g.Restore(st);
        }

        void StartControlWifiScan()
        {
            if (ctlWifiScanning) return;
            ctlWifiScanning = true;
            Kick();
            var t = new Thread(delegate ()
            {
                List<Wifi.Net> quick = null, full = null;
                Wifi.ScanStatus stQuick = Wifi.ScanStatus.Ok, stFull = Wifi.ScanStatus.Ok;
                try
                {
                    quick = Wifi.List(out stQuick);
                    Post(delegate
                    {
                        if (IsDisposed) return;
                        if (quick != null) ctlWifiNets = quick;
                        ctlWifiStatus = stQuick;
                        Kick();
                    });
                    if (stQuick != Wifi.ScanStatus.NoAdapter && stQuick != Wifi.ScanStatus.RadioOff && stQuick != Wifi.ScanStatus.AccessDenied)
                    {
                        Wifi.ScanStatus scanSt = Wifi.ScanAndWait(4000);
                        Wifi.ScanStatus stList;
                        full = Wifi.List(out stList);
                        if (scanSt == Wifi.ScanStatus.AccessDenied || scanSt == Wifi.ScanStatus.RadioOff || scanSt == Wifi.ScanStatus.NoAdapter)
                            stFull = scanSt;
                        else if (stList != Wifi.ScanStatus.Ok)
                            stFull = stList;
                        else
                            stFull = scanSt == Wifi.ScanStatus.Failed && (full == null || full.Count == 0) ? Wifi.ScanStatus.Failed : Wifi.ScanStatus.Ok;
                    }
                    else
                    {
                        stFull = stQuick;
                    }
                }
                catch (Exception ex) { Log.Write("ctl wifi scan: " + ex.Message); stFull = Wifi.ScanStatus.Failed; }
                Post(delegate
                {
                    if (IsDisposed) return;
                    if (full != null) ctlWifiNets = full;
                    ctlWifiStatus = stFull;
                    ctlWifiScanning = false;
                    Kick();
                });
            }) { IsBackground = true, Name = "ctl wifi scan" };
            t.Start();
        }

        void ControlWifiJoin(Wifi.Net n, string pass)
        {
            if (n == null) return;
            ctlWifiMsg = Lang.F("Menyambungkan ke {0}…", n.Ssid);
            Kick();
            var t = new Thread(delegate ()
            {
                string err = null;
                try { err = Wifi.Connect(n, pass); }
                catch (Exception ex) { err = ex.Message; }
                if (err != null)
                {
                    Log.Write("ctl wifi join: " + err);
                    List<Wifi.Net> errList = null;
                    Wifi.ScanStatus errSt;
                    try { errList = Wifi.List(out errSt); } catch (Exception) { }
                    Post(delegate
                    {
                        if (IsDisposed) return;
                        if (errList != null) ctlWifiNets = errList;
                        ctlWifiMsg = err.Length > 0 ? Lang.F("Gagal: {0}", err) : Lang.F("Gagal menyambung ke {0}", n.Ssid);
                        Kick();
                    });
                    return;
                }
                List<Wifi.Net> list = null;
                bool ok = false;
                for (int k = 0; k < 15; k++)
                {
                    Thread.Sleep(400);
                    if (IsDisposed) return;
                    Wifi.ScanStatus st;
                    try { list = Wifi.List(out st); } catch (Exception) { list = null; }
                    if (list != null && list.Exists(x => x.Ssid == n.Ssid && x.Connected))
                    {
                        ok = true;
                        break;
                    }
                }
                Post(delegate
                {
                    if (IsDisposed) return;
                    if (list != null) ctlWifiNets = list;
                    ctlWifiMsg = ok ? Lang.F("Tersambung ke {0}", n.Ssid) : Lang.F("Gagal menyambung ke {0}", n.Ssid);
                    Kick();
                });
            }) { IsBackground = true, Name = "ctl wifi join" };
            t.Start();
        }

        void ControlWifiDisconnect(Wifi.Net n)
        {
            if (n == null) return;
            ctlWifiMsg = Lang.F("Memutus {0}…", n.Ssid);
            Kick();
            var t = new Thread(delegate ()
            {
                try { Wifi.Disconnect(n); } catch (Exception ex) { Log.Write("ctl wifi disc: " + ex.Message); }
                Thread.Sleep(1500);
                Post(delegate
                {
                    if (IsDisposed) return;
                    ctlWifiMsg = null;
                    StartControlWifiScan();
                });
            }) { IsBackground = true, Name = "ctl wifi disc" };
            t.Start();
        }

        void OpenWifiPassBox(Wifi.Net n)
        {
            if (n == null) return;
            CloseWifiPassBox();
            var box = new WifiPassBox(n, ControlWifiJoin);
            wifiPassBox = box;
            box.FormClosed += delegate
            {
                if (ReferenceEquals(wifiPassBox, box)) wifiPassBox = null;
                if (!IsDisposed && !pointerInside && expanded)
                {
                    leaveTimer.Stop();
                    leaveTimer.Start();
                }
            };
            var area = Screen.FromHandle(Handle).WorkingArea;
            box.StartPosition = FormStartPosition.Manual;
            box.Show();
            int cx = Left + (int)Math.Round((shell.X + shell.Width / 2) * scale);
            int by = Top + (int)Math.Round((shell.Y + shell.Height) * scale);
            int x = Math.Max(area.X + 8, Math.Min(area.Right - box.Width - 8, cx - box.Width / 2));
            int y = Math.Min(area.Bottom - box.Height - 8, by + (int)(8 * scale));
            box.Location = new Point(x, y);
            Native.SetForegroundWindow(box.Handle);
        }

        void StartControlBtScan()
        {
            ctlBtMsg = null;
            ctlBtScanning = true;
            ctlBtScanner.StartScan(true);
            Kick();
        }

        static List<BtDevice> SortBtDevices(List<BtDevice> list)
        {
            var sorted = new List<BtDevice>();
            if (list == null) return sorted;
            sorted.AddRange(list);
            sorted.Sort(delegate (BtDevice a, BtDevice b)
            {
                int ga = a.Connected ? 0 : a.Paired ? 1 : 2;
                int gb = b.Connected ? 0 : b.Paired ? 1 : 2;
                if (ga != gb) return ga.CompareTo(gb);
                int sa = a.Signal, sb = b.Signal;
                if (sa != sb) return sb.CompareTo(sa);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return sorted;
        }

        void ControlBtAction(BtDevice d, string kind)
        {
            if (ctlBtBusy || d == null) return;
            ctlBtBusy = true;
            if (kind == "pair") ctlBtMsg = Lang.T("Menyandingkan…");
            else if (kind == "connect") ctlBtMsg = Lang.T("Menghubungkan…");
            else ctlBtMsg = Lang.T("Memutuskan…");
            Kick();
            var t = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    if (kind == "pair") err = BtScanner.Pair(d);
                    else if (kind == "unpair") err = BtScanner.Unpair(d);
                    else if (kind == "connect") err = BtScanner.Connect(d);
                    else err = BtScanner.Disconnect(d);
                }
                catch (Exception ex) { err = ex.Message; }
                if (err != null) Log.Write("ctl bt " + kind + ": " + err);
                Post(delegate
                {
                    if (IsDisposed) return;
                    ctlBtBusy = false;
                    if (kind == "pair")
                        ctlBtMsg = err == null ? Lang.T("Berhasil tersanding") : (err.Length > 0 ? Lang.T("Gagal menyandingkan") + " (" + err + ")" : Lang.T("Gagal menyandingkan"));
                    else if (kind == "connect")
                        ctlBtMsg = err == null ? Lang.T("Terhubung") : (err.Length > 0 ? Lang.T("Gagal menghubungkan") + " (" + err + ")" : Lang.T("Gagal menghubungkan"));
                    else
                        ctlBtMsg = err == null ? null : Lang.F("Gagal: {0}", err);
                    if (controlMode == 3 && expanded && page == PageControl) ctlBtScanner.StartScan(true);
                    Kick();
                });
            }) { IsBackground = true, Name = "ctl bt " + kind };
            t.Start();
        }

        void ControlBtMenu(BtDevice d)
        {
            if (d == null || !d.Paired) return;
            var m = new ContextMenuStrip();
            if (d.Connected) m.Items.Add(Lang.T("Putuskan"), null, delegate { ControlBtAction(d, "disconnect"); });
            else m.Items.Add(Lang.T("Sambungkan"), null, delegate { ControlBtAction(d, "connect"); });
            m.Items.Add(Lang.T("Lepas sandi"), null, delegate { ControlBtAction(d, "unpair"); });
            ShowIslandMenu(m);
        }

        void SoundSlider(RectangleF r)
        {
            BigSlider(r, "ctlVol", muted ? 0 : volBar.Value, VolumeGlyph(), Lang.T("Suara"),
                      muted ? Lang.T("Bisu") : Ink.Percent((int)Math.Round(vol * 100)), setVol, actVolStep, "ctlMute", actMute);
        }

        // "Speakers (Realtek Audio)" as two lines
        void SplitDevice(string name)
        {
            if (name == devSplitFor) return;
            devSplitFor = name;
            int i = name.IndexOf(" (", StringComparison.Ordinal);
            if (i > 0 && name.EndsWith(")", StringComparison.Ordinal) && name.Length - i > 3)
            {
                devTitle = name.Substring(0, i);
                devSub = name.Substring(i + 2, name.Length - i - 3);
            }
            else { devTitle = name; devSub = ""; }
        }

        // Output device; opens the list
        void DeviceChip(RectangleF r)
        {
            var dev = DefaultDevice();
            SplitDevice(dev != null ? dev.Name : Lang.T("Perangkat output"));
            AddHit(r, "ctlDevices", actDevices);
            float s = Squish("ctlDevices", r, 0.97f), cy = r.Y + r.Height / 2, tx = r.X + 46;
            bool hot = Hover("ctlDevices"), down = Pressed("ctlDevices");
            p.Module(r, Ink.RadiusModule, devicesOpen && !down ? p.T.FillHover : FillFor("ctlDevices"));
            p.Icon(DeviceGlyph(dev != null ? dev.Form : 1), 17f, p.T.Text, r.X + 25, cy);

            float chevX = r.Right - 20;
            string batName;
            int batPct = dev != null ? BatteryFor(dev.Name, out batName) : -1;
            float rightBound = chevX - 14;
            if (batPct >= 0)
            {
                string batText = Ink.Percent(batPct);
                float bw = Math.Max(32, p.Measure(batText, Ink.SmallSemi(10.5f)) + 12);
                float bx = chevX - 15 - bw;
                var badge = new RectangleF(bx, cy - 10, bw, 20);
                bool low = batPct <= 20;
                p.FillRound(badge, 10, low ? Ink.A(p.T.Negative, 0.22f) : Ink.A(p.T.Text, 0.09f));
                p.TextCenter(batText, Ink.SmallSemi(10.5f), low ? p.T.Negative : p.T.Sub, badge.X + badge.Width / 2, cy);
                rightBound = bx - 8;
            }
            float room = Math.Max(40, rightBound - tx);
            if (devSub.Length == 0) p.Text(devTitle, Ink.Semi(13.5f), p.T.Text, tx, cy, room, StringAlignment.Near);
            else
            {
                p.Text(devTitle, Ink.Semi(13.5f), p.T.Text, tx, cy - 8, room, StringAlignment.Near);
                p.Text(devSub, Ink.Caption, p.T.Sub, tx, cy + 9, room, StringAlignment.Near);
            }
            if (hot || devicesOpen) p.Circle(chevX, cy, 12f, Ink.A(p.T.Text, down ? 0.14f : 0.09f));
            p.Symbol(devicesOpen ? Sym.ChevronUp : Sym.ChevronDown, chevX, cy, 11f, hot || devicesOpen ? p.T.Text : p.T.Sub);
            Unsquish(r, s);
        }

        void DrawDeviceList(RectangleF r)
        {
            DeviceActs();
            Module(r, Ink.RadiusModule);
            int n = Math.Min(devActs.Length, Math.Min(5, audioDevices.Count));
            if (n == 0)
            {
                p.TextCenter(Lang.T("Tidak ada perangkat output"), Ink.Callout, p.T.Sub, r.X + r.Width / 2, r.Y + r.Height / 2);
                return;
            }
            for (int i = 0; i < n; i++)
            {
                var d = audioDevices[i];
                var row = new RectangleF(r.X + 6, r.Y + 6 + i * CtlDeviceRow, r.Width - 12, CtlDeviceRow);
                ListRow(row, DeviceGlyph(d.Form), d.Name, null, null, d.Id == audioDefault, DevIds[i], devActs[i], i < n - 1);
            }
        }

        static string ClipShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 10 ? s.Substring(0, 9) + "…" : s;
        }

        // Radios, mic, quiet, mini, hide, dark, awake
        void DrawToggles(RectangleF r)
        {
            Module(r, Ink.RadiusModule);
            const int n = 8;
            float cx, cy, d = ToggleD(n), hw = r.Width / 4;
            var curNet = net.Current;
            string wifiLabel = curNet.Wifi && curNet.Name.Length > 0 ? ClipShort(curNet.Name) : Lang.T("Wi-Fi");
            ToggleSlot(r, 0, n, out cx, out cy);
            CircleToggle(cx, cy, d, Ink.IcoWifi, radios.WifiOn, radios.HasWifi, wifiLabel, "tileWifi", actWifi, actWifiMenu, hw, actWifiMenu, "tileWifiMore");
            ToggleSlot(r, 1, n, out cx, out cy);
            CircleToggle(cx, cy, d, Ink.IcoBluetooth, radios.BluetoothOn, radios.HasBluetooth, Lang.T("Bluetooth"), "tileBt", actBt, actBtMenu, hw, actBtMenu, "tileBtMore");
            // Long labels sit at row ends
            ToggleSlot(r, 2, n, out cx, out cy);
            CircleToggle(cx, cy, d, Ink.IcoQuiet, QuietSwitch, true, Lang.T("Senyap"), "tileQuiet", actQuiet, null, hw);
            ToggleSlot(r, 3, n, out cx, out cy);
            CircleToggle(cx, cy, d, micMuted ? IcoMicOff : Ink.IcoMic, micPresent && !micMuted, micPresent, Lang.T("Mikrofon"), "tileMic", actMic, null, hw);
            ToggleSlot(r, 4, n, out cx, out cy);
            CircleToggle(cx, cy, d, IcoMini, MiniWanted(), true, Lang.T("Mini"), "tileMini", MiniAct(), null, hw);
            ToggleSlot(r, 5, n, out cx, out cy);
            CircleToggle(cx, cy, d, IcoDarkMode, winDark, true, Lang.T("Gelap"), "tileDark", actDark, null, hw);
            ToggleSlot(r, 6, n, out cx, out cy);
            CircleToggle(cx, cy, d, IcoAwake, AwakeOn, true, AwakeLabel(), "tileAwake", actAwake, actAwakeMenu, hw, actAwakeMenu, "tileAwakeMore");
            ToggleSlot(r, 7, n, out cx, out cy);
            CircleToggle(cx, cy, d, Ink.IcoHide, false, true, Lang.T("Sembunyi"), "tileHide", actHide, null, hw);
        }

        // Four across past six tiles
        static float ToggleD(int n) { return n > 6 ? 44 : CtlToggleD; }

        // 2x2 to four, 3x2 to six, 4x2 to eight
        static void ToggleSlot(RectangleF r, int i, int n, out float cx, out float cy)
        {
            float d = ToggleD(n), block = d + 17;
            int cols = n <= 4 ? 2 : n <= 6 ? 3 : 4, rows = (n + cols - 1) / cols, row = i / cols, inRow = Math.Min(cols, n - row * cols);
            float gx = (r.Width - cols * d) / (cols + 1), gy = (r.Height - rows * block) / (rows + 1);
            // Short last row stays centred
            float start = r.X + (r.Width - inRow * d - (inRow - 1) * gx) / 2;
            cx = start + (i % cols) * (d + gx) + d / 2;
            cy = r.Y + gy * (row + 1) + block * row + d / 2;
        }

        void OpenSettings(string uri)
        {
            try { Process.Start(uri); } catch (Exception ex) { Log.Write("settings: " + ex.Message); }
        }

        // Timer: presets idle, countdown when running
        void DrawTimerModule(RectangleF r, DateTime now, bool live)
        {
            Module(r, Ink.RadiusModule);
            float pad = Ink.ModulePad;
            if (!TimerOn)
            {
                p.Icon(Ink.IcoTimer, 15f, p.T.Sub, r.X + 25, r.Y + 26);
                p.Text(Lang.T("Timer"), Ink.Headline, p.T.Text, r.X + 46, r.Y + 26, 120, StringAlignment.Near);
                string taskChip = FocusTaskTitle.Length > 0 ? FocusTaskTitle : Lang.T("Pilih tugas fokus…");
                float chipW = Math.Min(r.Width - 130, Math.Max(128, p.Measure(taskChip, Ink.Caption) + 34));
                var chipRect = new RectangleF(r.Right - pad - chipW, r.Y + 14, chipW, 24);
                AddHit(chipRect, "tmPickTask", PickFocusTaskMenu);
                p.Module(chipRect, 12, Hover("tmPickTask") ? p.T.FillHover : p.T.Fill);
                p.Icon('\uE762', 11f, FocusTaskTitle.Length > 0 ? p.T.Accent : p.T.Sub, chipRect.X + 14, chipRect.Y + 12);
                p.Text(taskChip, Ink.Caption, FocusTaskTitle.Length > 0 ? p.T.Text : p.T.Sub, chipRect.X + 24, chipRect.Y + 12, chipW - 34, StringAlignment.Near);
                // Presets share the width evenly
                int n = ChipNames.Length;
                float gap = 8, cw = (r.Width - 2 * pad - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    var pr = new RectangleF(r.X + pad + i * (cw + gap), r.Bottom - pad - 32, cw, 32);
                    Pill(pr, ChipIds[i], Lang.T(ChipNames[i]), PillKind.Secondary, chipActs[i]);
                    p.StrokeRound(pr, pr.Height / 2, p.T.Line, 1f);
                }
                return;
            }
            float cy = r.Y + r.Height / 2;
            ctlRingX = r.X + pad + 26;
            ctlRingY = cy;
            ctlRingC = timerPaused ? p.T.Faint : timerKind == TimerKind.Stopwatch || timerKind == TimerKind.Break ? p.T.Sub : p.T.Accent;
            if (live) LiveRect(new RectangleF(ctlRingX - 27, cy - 27, 54, 54), timerPaused ? 0 : TickFps, ctlRingPaint);
            else ctlRingPaint(now);
            int key = (int)timerKind * 2 + (timerPaused ? 1 : 0);
            if (key != tmLabelKey) { tmLabelKey = key; tmLabel = timerPaused ? TimerLabel() + Lang.T(" · dijeda") : TimerLabel(); }
            float tx = ctlRingX + 24 + pad;
            p.Text(tmLabel, Ink.Callout, p.T.Sub, tx, cy - 19, 220, StringAlignment.Near);
            float lw = p.Measure(tmLabel, Ink.Callout);
            float rightLimit = r.Right - pad - 148;
            bool showEnd = (timerKind == TimerKind.Focus || timerKind == TimerKind.Countdown) && !timerPaused;
            string est = showEnd ? Lang.F("Selesai {0}", Fmt.Hm(timerEndUtc.ToLocalTime())) : null;
            float estW = est != null ? p.Measure(est, Ink.Caption) : 0;
            bool hasTask = (timerKind == TimerKind.Focus || timerKind == TimerKind.Break) && FocusTaskTitle.Length > 0;
            if (hasTask)
            {
                float maxTaskW = Math.Max(40, rightLimit - (estW > 0 ? estW + 10 : 0) - (tx + lw + 8));
                if (maxTaskW > 40)
                {
                    var taskRect = new RectangleF(tx + lw + 8, cy - 28, maxTaskW, 18);
                    AddHit(taskRect, "tmPickTask", PickFocusTaskMenu);
                    p.Text("· " + FocusTaskTitle, Ink.Caption, Hover("tmPickTask") ? p.T.Text : p.T.Sub, taskRect.X, cy - 19, maxTaskW, StringAlignment.Near);
                }
                if (est != null && estW > 0)
                    p.Text(est, Ink.Caption, p.T.Faint, rightLimit, cy - 19, estW + 4, StringAlignment.Far);
            }
            else if (laps.Count > 0)
            {
                if (laps.Count != tmLapCount) { tmLapCount = laps.Count; tmLap = Lang.F("Putaran {0} · {1}", laps.Count, FormatSpan(laps[0])); }
                p.Text(tmLap, Ink.Caption, p.T.Faint, tx + lw + 10, cy - 19, 160, StringAlignment.Near);
            }
            else if (est != null && estW > 0)
            {
                float maxEstW = Math.Max(0, rightLimit - (tx + lw + 10));
                if (maxEstW > 30) p.Text(est, Ink.Caption, p.T.Faint, tx + lw + 10, cy - 19, maxEstW, StringAlignment.Near);
            }
            // Countdown redraws alone each second
            ctlClockX = tx - 1;
            ctlClockY = cy + 10;
            ctlClockC = timerPaused ? p.T.Sub : p.T.Text;
            if (live) LiveRect(new RectangleF(tx - 3, ctlClockY - 28, 180, 56), ctlClockPaint);
            else ctlClockPaint(now);
            // Right to left; pause carries accent
            float bx = r.Right - pad - 20;
            IconButton(bx, cy, 40, "tmStop", Sym.Stop, actTmStop, false);
            bx -= 48;
            if (timerKind == TimerKind.Stopwatch) IconButton(bx, cy, 40, "tmLap", '', actTmLap, false);
            else IconButton(bx, cy, 40, "tmAdd", Sym.Plus, actTmAdd, false);
            bx -= 50;
            IconButton(bx, cy, 44, "tmPause", timerPaused ? Sym.Play : Sym.Pause, actTmPause, true);
        }

        static string FormatSpan(TimeSpan t) { return ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00") + "." + (t.Milliseconds / 100); }

        // Small secondary pill
        void Chip(RectangleF r, string id, string label, Action act) { Pill(r, id, label, PillKind.Secondary, act); }

        // iOS switch with a sliding knob
        void Switch(RectangleF r, string id, bool on, Action toggle)
        {
            Spring s;
            if (!switches.TryGetValue(id, out s)) { s = new Spring(on ? 1 : 0, 520, 30, 0.004f); switches[id] = s; }
            s.Target = on ? 1 : 0;
            AddHit(RectangleF.Inflate(r, 4, 4), id, toggle);
            var track = new RectangleF(r.X, r.Y + r.Height / 2 - 11, 38, 22);
            p.FillRound(track, 11, Ink.Mix(p.T.Quaternary, p.T.Accent, s.Value));
            float k = Ease.Clamp01(s.Value);
            p.Circle(track.X + 11 + k * 16, track.Y + 11, Pressed(id) ? 8.2f : 9, Ink.Mix(p.T.Text, p.T.OnAccent, k));
        }

        bool SwitchesMoving()
        {
            foreach (var s in switches.Values) if (!s.Settled) return true;
            return PartsMoving() || (DateTime.UtcNow - devicesUtc).TotalSeconds < 0.32;
        }

        void StepSwitches(float dt)
        {
            foreach (var s in switches.Values) s.Step(dt);
            brightBar.Step(dt);
            StepParts(dt);
        }

        // Compact HUDs

        void DrawBrightness(float w, float h, DateTime now) { LevelHud(w, h, Ink.IcoSun, brightBar.Value, brightRoller, now); }

        float AudioDeviceWidth()
        {
            var d = toast != null ? toast.Data as AudioDevice : null;
            if (d == null || p.G == null) return 280;
            return Clamp(p.Measure(d.Name, Ink.Headline) + 90, 240, 440);
        }

        void DrawAudioDevice(float w, float h)
        {
            var d = toast != null ? toast.Data as AudioDevice : null;
            if (d == null) return;
            float cy = h / 2;
            p.Module(new RectangleF(11, cy - 15, 30, 30), 15, p.T.Fill);
            p.Icon(DeviceGlyph(d.Form), 15f, p.T.Text, 26, cy);
            p.Text(d.Name, Ink.Headline, p.T.Text, 52, cy - 8, w - 52 - 20, StringAlignment.Near);
            p.Text(Lang.T("Suara keluar di sini") + BatteryTail(d.Name), Ink.Caption, p.T.Sub, 52, cy + 10, w - 52 - 20, StringAlignment.Near);
        }

        sealed class WifiPassBox : KitWindow
        {
            readonly Wifi.Net net;
            readonly Action<Wifi.Net, string> done;
            readonly KitText box = new KitText();
            readonly KitButton eye = new KitButton();
            readonly KitButton join = new KitButton { Primary = true };
            bool revealed;
            Font fBox, fJoin, fHead, fHint;

            public WifiPassBox(Wifi.Net net, Action<Wifi.Net, string> done)
            {
                this.net = net;
                this.done = done;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                KeyPreview = true;
                Text = net.Ssid;
                box.BorderStyle = BorderStyle.None;
                box.Multiline = false;
                box.UseSystemPasswordChar = true;
                box.AccessibleName = Lang.T("Kata sandi");
                join.Text = Lang.T("Sambungkan");
                join.Enabled = false;
                UpdateEye();
                eye.Click += delegate
                {
                    revealed = !revealed;
                    box.UseSystemPasswordChar = !revealed;
                    UpdateEye();
                    box.Focus();
                };
                box.TextChanged += delegate { join.Enabled = box.Text.Length > 0; };
                join.Click += delegate { Commit(); };
                box.KeyDown += delegate (object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
                };
                KeyDown += delegate (object sender, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
                };
                Controls.Add(box);
                Controls.Add(eye);
                Controls.Add(join);
                Deactivate += delegate { if (!IsDisposed) Close(); };
            }

            void UpdateEye()
            {
                eye.Text = revealed ? "\uED1A" : "\uE7B3";
                eye.AccessibleName = Lang.T(revealed ? "Sembunyikan kata sandi" : "Tampilkan kata sandi");
            }

            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Relayout();
            }

            public override void Relayout()
            {
                float s = Ui;
                BackColor = KitInk.Window;
                ClientSize = new Size((int)(360 * s), (int)(128 * s));
                DisposeFonts();
                fBox = UiFont(14f);
                fJoin = UiFont(12.5f);
                fHead = UiFont(13f);
                fHint = UiFont(11.5f);
                box.Font = fBox;
                box.BackColor = KitInk.Group;
                box.ForeColor = KitInk.Text;
                box.SetBounds((int)(28 * s), (int)(48 * s), ClientSize.Width - (int)(86 * s), (int)(22 * s));
                Native.SetCue(box.Handle, Lang.T("Kata sandi"));
                eye.Font = Ink.Icons(14f * s);
                eye.SetBounds(ClientSize.Width - (int)(50 * s), (int)(44 * s), (int)(30 * s), (int)(30 * s));
                join.Font = fJoin;
                int jw = TextRenderer.MeasureText(join.Text, fJoin).Width + (int)(32 * s);
                join.Size = new Size(Math.Max((int)(104 * s), jw), (int)(30 * s));
                join.Location = new Point(ClientSize.Width - join.Width - (int)(16 * s), ClientSize.Height - (int)(42 * s));
                Invalidate();
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                box.Focus();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(KitInk.Window);
                KitInk.Smooth(g);
                float s = Ui;
                KitInk.Draw(g, Ink.Str(Ink.IcoLock), Ink.Icons(12.5f * s), KitInk.Sub, new RectangleF(16 * s, 12 * s, 18 * s, 20 * s), ContentAlignment.MiddleCenter);
                if (fHead != null)
                    KitInk.Draw(g, net.Ssid, fHead, KitInk.Text, new RectangleF(38 * s, 12 * s, ClientSize.Width - 54 * s, 20 * s), ContentAlignment.MiddleLeft);
                var well = new RectangleF(16 * s, 38 * s, ClientSize.Width - 32 * s, 42 * s);
                using (var path = Ink.Round(well, 12 * s))
                {
                    using (var b = new SolidBrush(KitInk.Group)) g.FillPath(b, path);
                    using (var pen = new Pen(KitInk.Faint, 1.2f)) g.DrawPath(pen, path);
                }
                if (fHint != null)
                    KitInk.Draw(g, Lang.T("Esc batal"), fHint, KitInk.Sub, new RectangleF(18 * s, ClientSize.Height - 42 * s, 160 * s, 30 * s), ContentAlignment.MiddleLeft);
            }

            void Commit()
            {
                if (box.Text.Length == 0) return;
                string pass = box.Text;
                Close();
                done(net, pass);
            }

            void DisposeFonts()
            {
                if (fBox != null) { fBox.Dispose(); fBox = null; }
                if (fJoin != null) { fJoin.Dispose(); fJoin = null; }
                if (fHead != null) { fHead.Dispose(); fHead = null; }
                if (fHint != null) { fHint.Dispose(); fHint = null; }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) DisposeFonts();
                base.Dispose(disposing);
            }
        }
    }
}
