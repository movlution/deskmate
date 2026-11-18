using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deskmate
{
    partial class Island
    {
        sealed partial class DockWindow
        {
            void DrawTop()
            {
                RectangleF anchor;
                string text = LabelFor(keyMode ? keySel : hoverId, out anchor);
                if (text.Length > 0) labelText = text;
                float t = labelShow.Value;
                if (t > 0.01f && labelText.Length > 0 && !anchor.IsEmpty) DrawLabel(labelText, anchor, t);
                if (dragApp != null) DrawDragGhost();
                DrawDockFlyers(DateTime.UtcNow);
            }

            string LabelFor(string id, out RectangleF anchor)
            {
                anchor = RectangleF.Empty;
                if (string.IsNullOrEmpty(id) || dragApp != null) return "";
                foreach (var s in slots)
                    if (s.Id == id && s.App != null) { anchor = s.Icon; return s.App.Name; }
                    else if (s.Id == id.Replace("sys:", "") && id.StartsWith("sys:", StringComparison.Ordinal))
                    {
                        anchor = s.Icon;
                        return s.Id == "work" ? Lang.T("Tugas dan catatan") : s.Id == "start" ? "Start" : Lang.T("Semua jendela");
                    }
                foreach (var kv in hits)
                {
                    if (kv.Value != id) continue;
                    anchor = kv.Key;
                    if (id.StartsWith("tray:", StringComparison.Ordinal))
                    {
                        var ic = trayIcons.Find(x => "tray:" + x.Key == id);
                        if (ic != null && (ic.Version < 4 || ic.ShowTip)) return FirstLine(ic.Tip);
                        return "";
                    }
                    var mi = isle != null ? isle.mediaInfo : MediaInfo.None;
                    var now = DateTime.Now;
                    switch (id)
                    {
                        case "w_clock":
                        case "clock":
                            return now.ToString("dddd, d MMMM yyyy", Fmt.Id) + " · " + Fmt.Clock(now);
                        case "w_media":
                            if (mi.Title.Length == 0) return Lang.T("Musik");
                            return mi.Artist.Length > 0 ? mi.Title + " — " + mi.Artist : mi.Title;
                        case "w_play":
                            return mi.Playing ? Lang.T("Jeda") : Lang.T("Putar");
                        case "w_ai":
                            int busy = isle != null ? isle.BusyCount() : 0;
                            return busy > 0 ? Lang.F("{0} agen bekerja", busy) : Lang.T("Kuota AI");
                        case "w_timer":
                            string timer = isle != null ? isle.TimerClock() : "";
                            string timerName = isle != null ? isle.TimerLabel() : "";
                            return timerName.Length > 0 ? timer + " · " + timerName : timer;
                        case "w_weather":
                            if (isle == null || isle.weather == null) return Lang.T("Cuaca");
                            return isle.weather.Temp + "° · " + WeatherWord(isle.weather.Code);
                        case "w_battery":
                            if (isle == null) return Lang.T("Baterai");
                            return Ink.Percent(isle.battery) + " · " + (isle.onAc ? Lang.T("mengisi daya") : Lang.T("Baterai"));
                        case "w_system":
                            return "CPU " + Ink.Percent(cpuPct) + " · RAM " + Ink.Percent(ramPct);
                        case "status":
                            if (isle == null) return Lang.T("Status");
                            var net = isle.net.Current;
                            var parts = new List<string>();
                            parts.Add(net.Name.Length > 0 ? net.Name : !net.Any ? Lang.T("Tidak ada jaringan") : net.Wifi ? "Wi-Fi" : Lang.T("Jaringan"));
                            parts.Add(isle.muted ? Lang.T("Senyap") : Lang.T("Volume") + " " + Ink.Percent((int)Math.Round(isle.vol * 100)));
                            if (isle.hasBattery) parts.Add(Ink.Percent(isle.battery));
                            return string.Join(" · ", parts.ToArray());
                        case "chev":
                            return TrayExpanded ? Lang.T("Sembunyikan ikon") : Lang.T("Ikon tersembunyi");
                        case "desk":
                            return Lang.T("Tampilkan desktop");
                    }
                    return "";
                }
                return "";
            }

            static string FirstLine(string s)
            {
                int n = s.IndexOfAny(new[] { '\r', '\n' });
                return (n >= 0 ? s.Substring(0, n) : s).Trim();
            }

            void DrawLabel(string text, RectangleF anchor, float t)
            {
                var f = Ink.Semi(12.5f);
                float w = Math.Min(300, p.Measure(text, f) + 24), h = 24;
                float cx = Math.Max(w / 2 + 4, Math.Min(cw / scale - w / 2 - 4, anchor.X + anchor.Width / 2));
                float bottom = anchor.Y - 8 + (1 - t) * 4;
                var r = new RectangleF(cx - w / 2, bottom - h, w, h);
                p.Alpha = t;
                // The macOS label is a quiet floating capsule: no outline,
                // just a little shadow and the theme's surface underneath.
                p.FillRound(new RectangleF(r.X, r.Y + 1.5f, r.Width, r.Height), 8, Ink.A(Color.Black, 0.22f));
                p.FillRound(r, 8, Ink.A(Ink.Base.Bg, 0.94f));
                p.Text(text, f, p.T.Text, cx, r.Y + h / 2, w - 20, StringAlignment.Center);
                p.Alpha = 1;
            }

            bool StepLabel(float dt)
            {
                labelShow.Step(dt);
                return !labelShow.Settled;
            }

            // Tray, network and sound, battery, clock, the desktop sliver
            void DrawRight()
            {
                if (rightPill.IsEmpty) return;
                var r = rightPill;
                float x = r.X, top = PlateCy(r) - PlateH / 2;
                foreach (var ic in InlineTray())
                {
                    DrawTrayIcon(ic, new RectangleF(x, top, 24, PlateH));
                    x += 24;
                }
                if (OverflowTray().Count > 0)
                {
                    var cr = new RectangleF(x, top, 22, PlateH);
                    bool hot = hoverId == "chev" || TrayExpanded;
                    Hit(cr, "chev", ToggleTrayCard, null);
                    if (hot) p.FillRound(cr, PlateR, pressId == "chev" ? p.T.FillPressed : p.T.FillHover);
                    // Points up while closed, down while the grid is open
                    p.Icon(TrayExpanded ? '\uE70D' : '\uE70E', 10f, hot ? p.T.Text : p.T.Sub, cr.X + cr.Width / 2, cr.Y + cr.Height / 2);
                    x += 22;
                }
                DrawStatus(new RectangleF(x, top, StatusWidth(), PlateH));
                x += StatusWidth() + 4;
                DrawClock(new RectangleF(x, top, ClockWidth(), PlateH));
                // Show desktop: the bar's far end, padding included
                var desk = new RectangleF(r.Right - 10, r.Y, 10 + PadX, r.Height);
                Hit(desk, "desk", ShowDesktop, null);
                if (hoverId == "desk") g.DrawLine(p.P(p.T.Sub, 1.2f), desk.X + 4.5f, r.Y + 13, desk.X + 4.5f, r.Bottom - 13);
            }

            void DrawTrayIcon(TrayIcon ic, RectangleF cell)
            {
                string id = "tray:" + ic.Key;
                Hit(cell, id, null, null);
                ic.Spot = ToScreen(cell);
                if (hoverId == id) p.FillRound(cell, PlateR, pressId == id ? p.T.FillPressed : p.T.FillHover);
                var bmp = tray != null ? tray.Picture(ic) : null;
                if (bmp == null) return;
                float s = 16;
                var dst = new RectangleF(cell.X + (cell.Width - s) / 2, cell.Y + (cell.Height - s) / 2, s, s);
                g.DrawImage(bmp, dst);
            }

            void DrawStatus(RectangleF r)
            {
                bool hot = hoverId == "status";
                Hit(r, "status", delegate { ToggleSheet("quick"); }, null);
                if (hot) p.FillRound(r, PlateR, pressId == "status" ? p.T.FillPressed : p.T.FillHover);
                // The icon group, centred: Wi-Fi to the battery's cap
                float span = isle.hasBattery ? 58f : 32f;
                float cx = r.X + (r.Width - span) / 2 + 7f, cy = r.Y + r.Height / 2;
                var net = isle.net.Current;
                char netGlyph = !net.Any ? Ink.IcoNoNet : net.Wifi ? Ink.IcoWifi : '\uE839';
                p.Icon(netGlyph, 14f, net.Online || !net.Any ? p.T.Text : p.T.Sub, cx, cy);
                cx += 20;
                char volGlyph = isle.muted ? Ink.IcoMute : isle.vol < 0.01f ? Ink.IcoVol0 : isle.vol < 0.34f ? Ink.IcoVol1 : isle.vol < 0.67f ? Ink.IcoVol2 : Ink.IcoVol3;
                p.Icon(volGlyph, 14f, p.T.Text, cx, cy);
                if (isle.hasBattery)
                {
                    cx += 21;
                    p.Battery(cx - 9, cy, isle.battery / 100f, isle.battery <= 20 && !isle.onAc ? p.T.Negative : p.T.Text, isle.onAc);
                }
            }

            void DrawClock(RectangleF r)
            {
                bool hot = hoverId == "clock";
                Hit(r, "clock", delegate { ToggleSheet("cal"); }, ClockMenu);
                if (hot) p.FillRound(r, PlateR, pressId == "clock" ? p.T.FillPressed : p.T.FillHover);
                var now = DateTime.Now;
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                p.TextCenter(Fmt.Clock(now), Ink.Semi(12.5f), p.T.Text, cx, cy);
                // The full date lives in the hover label; the dock only needs
                // the time, like a menu-bar clock rather than a taskbar block.
                if (isle.unread > 0) Badge(r, isle.unread);
            }

            static void ShowDesktop() { ShellVerb("ToggleDesktop"); }

            // Explorer's own commands, no simulated keys
            static void ShellVerb(string verb)
            {
                try
                {
                    var t = Type.GetTypeFromProgID("Shell.Application");
                    object sh = Activator.CreateInstance(t);
                    t.InvokeMember(verb, BindingFlags.InvokeMethod, null, sh, null);
                    Marshal.ReleaseComObject(sh);
                }
                catch (Exception ex) { Log.Write("dock " + verb + ": " + ex.Message); }
            }

        }
    }
}
