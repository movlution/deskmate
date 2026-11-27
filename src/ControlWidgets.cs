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
    partial class Island
    {
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

        const float PomodoroStatsH = 68;

        float PomodoroHeight()
        {
            return Math.Min(MaxH, PanelTop + 4 + CtlTimerH + Ink.ModuleGap + PomodoroStatsH + Ink.ModuleInset);
        }

        void DrawPomodoroPage(float w, float h, DateTime now)
        {
            ControlActs();
            float x0 = Ink.ModuleInset, full = w - 2 * x0, y = PanelTop + 4, keep = p.Alpha, dy;
            p.Alpha = keep * RowIn(0, now, out dy);
            DrawTimerModule(new RectangleF(x0, y + dy, full, CtlTimerH), now, true);
            p.Alpha = keep * RowIn(1, now, out dy);
            DrawPomodoroStats(new RectangleF(x0, y + CtlTimerH + Ink.ModuleGap + dy, full, PomodoroStatsH));
            p.Alpha = keep;
        }

        void DrawPomodoroStats(RectangleF r)
        {
            Module(r, Ink.RadiusModule);
            float pad = Ink.ModulePad;
            float leftX = r.X + pad;
            float topY = r.Y + 21;
            float botY = r.Bottom - 20;

            // Left: today's sessions + 4-dot Pomodoro cycle indicator
            int sessions = FocusSessionsToday;
            string todayLine = Lang.F("{0} sesi · {1} mnt hari ini", sessions, FocusMinToday);
            p.Icon('\uE916', 13f, p.T.Accent, leftX + 6, topY);
            p.Text(todayLine, Ink.Semi(12f), p.T.Text, leftX + 20, topY, r.Width * 0.56f, StringAlignment.Near);

            int cycleFilled = sessions == 0 ? 0 : ((sessions - 1) % 4) + 1;
            float dotX = leftX + 4;
            for (int i = 0; i < 4; i++)
            {
                bool lit = i < cycleFilled;
                p.Circle(dotX + i * 13, botY, lit ? 4f : 3.5f, lit ? p.T.Accent : Ink.A(p.T.Text, 0.14f));
            }
            int rest = Math.Max(1, settings.GetInt("focus.break", 5));
            string sub = FocusStreakDays > 0
                ? Lang.F("Streak {0} hari", FocusStreakDays) + " · " + Lang.F("Istirahat {0} mnt", rest)
                : Lang.F("Istirahat {0} mnt", rest);
            p.Text(sub, Ink.Caption, FocusStreakDays > 0 ? p.T.Accent : p.T.Sub, dotX + 56, botY, r.Width * 0.45f, StringAlignment.Near);

            // Right: 7-day focus sparkline bars
            int[] bars = FocusWeekBars();
            int maxMin = 25;
            for (int i = 0; i < bars.Length; i++) if (bars[i] > maxMin) maxMin = bars[i];
            const float bw = 7f, bgap = 5f, maxH = 26f;
            float totalW = bars.Length * bw + (bars.Length - 1) * bgap;
            float bx0 = r.Right - pad - totalW;
            float baseY = r.Bottom - 14;
            for (int i = 0; i < bars.Length; i++)
            {
                float frac = bars[i] <= 0 ? 0f : Math.Max(0.18f, Math.Min(1f, (float)bars[i] / maxMin));
                float bh = bars[i] <= 0 ? 4f : frac * maxH;
                var br = new RectangleF(bx0 + i * (bw + bgap), baseY - bh, bw, bh);
                bool isToday = i == bars.Length - 1;
                Color bc = bars[i] <= 0
                    ? Ink.A(p.T.Text, 0.10f)
                    : isToday ? p.T.Accent : Ink.Mix(p.T.FillHover, p.T.Text, 0.28f);
                p.FillRound(br, bw / 2f, bc);
            }
            string wkText = FocusWeekTotalMin > 0
                ? Lang.T("Minggu ini") + " · " + FocusWeekTotalMin + "m"
                : Lang.T("Minggu ini");
            p.Text(wkText, Ink.Small(10.5f), p.T.Faint, r.Right - pad, r.Y + 14, totalW + 44, StringAlignment.Far);
        }

        // Timer: presets idle, countdown when running
        void DrawTimerModule(RectangleF r, DateTime now, bool live)
        {
            Module(r, Ink.RadiusModule);
            float pad = Ink.ModulePad;
            if (!TimerOn)
            {
                p.Icon(Ink.IcoTimer, 15f, p.T.Accent, r.X + 25, r.Y + 26);
                p.Text(Lang.T("Pomodoro"), Ink.Headline, p.T.Text, r.X + 46, r.Y + 26, 120, StringAlignment.Near);
                string taskChip = FocusTaskTitle.Length > 0 ? FocusTaskTitle : Lang.T("Pilih tugas fokus…");
                float chipW = Math.Min(r.Width - 130, Math.Max(128, p.Measure(taskChip, Ink.Caption) + 34));
                var chipRect = new RectangleF(r.Right - pad - chipW, r.Y + 14, chipW, 24);
                AddHit(chipRect, "tmPickTask", PickFocusTaskMenu);
                float csq = Squish("tmPickTask", chipRect, 0.95f);
                p.Module(chipRect, 12, Pressed("tmPickTask") ? p.T.FillPressed : Hover("tmPickTask") ? p.T.FillHover : p.T.Fill);
                p.StrokeRound(chipRect, 12, FocusTaskTitle.Length > 0 ? Ink.A(p.T.Accent, 0.4f) : p.T.Line, 1f);
                p.Icon('\uE762', 11f, FocusTaskTitle.Length > 0 ? p.T.Accent : p.T.Sub, chipRect.X + 14, chipRect.Y + 12);
                p.Text(taskChip, Ink.Caption, FocusTaskTitle.Length > 0 ? p.T.Text : p.T.Sub, chipRect.X + 24, chipRect.Y + 12, chipW - 34, StringAlignment.Near);
                Unsquish(chipRect, csq);
                // Presets share the width evenly; Fokus 25 leads with accent
                int n = ChipNames.Length;
                float gap = 8, cw = (r.Width - 2 * pad - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    var pr = new RectangleF(r.X + pad + i * (cw + gap), r.Bottom - pad - 32, cw, 32);
                    Pill(pr, ChipIds[i], Lang.T(ChipNames[i]), i == 0 ? PillKind.Primary : PillKind.Secondary, chipActs[i]);
                    if (i != 0) p.StrokeRound(pr, pr.Height / 2, p.T.Line, 1f);
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

        // Switch on the Kit spec: quiet tinted track and Sub knob off, accent track and
        // OnAccent knob on, knob lifted by the same two-step contact shadow
        void Switch(RectangleF r, string id, bool on, Action toggle)
        {
            Spring s;
            if (!switches.TryGetValue(id, out s)) { s = new Spring(on ? 1 : 0, 520, 30, 0.004f); switches[id] = s; }
            s.Target = on ? 1 : 0;
            AddHit(RectangleF.Inflate(r, 4, 4), id, toggle);
            bool hover = Hover(id);
            var track = new RectangleF(r.X, r.Y + r.Height / 2 - 11, 38, 22);
            float k = Ease.Clamp01(s.Value);
            var bg = Color.FromArgb(255, p.T.Bg);
            Color off = Ink.Mix(bg, p.T.Text, hover ? 0.19f : 0.12f);
            p.FillRound(track, 11, Ink.Mix(off, p.T.Accent, k));
            float kr = Pressed(id) ? 7.5f : hover ? 8.5f : 8f, cx = track.X + 11 + k * 16, cy = track.Y + 11;
            var knob = new RectangleF(cx - kr, cy - kr, kr * 2, kr * 2);
            bool light = !p.T.IsDark;
            int a = light ? 26 : 90;
            p.FillRound(new RectangleF(knob.X - 0.3f, knob.Y + 0.9f, knob.Width + 0.6f, knob.Height + 0.3f), kr, Ink.Scrim(a / 2));
            p.FillRound(new RectangleF(knob.X, knob.Y + 0.45f, knob.Width, knob.Height), kr, Ink.Scrim(a));
            Color sub = Ink.Over(p.T.Sub, bg);
            p.Circle(cx, cy, kr, Ink.Mix(hover ? Ink.Mix(sub, p.T.Text, 0.35f) : sub, p.T.OnAccent, k));
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

    }
}
