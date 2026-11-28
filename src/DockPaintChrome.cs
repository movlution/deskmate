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
                        return s.Id == "shelf" ? ShelfLabel() : s.Id == "work" ? Lang.T("Tugas dan catatan") : s.Id == "suruh" ? Lang.T("Suruh Deskmate") : s.Id == "start" ? "Start" : Lang.T("Semua jendela");
                    }
                foreach (var kv in hits)
                    if (kv.Value == id && id.StartsWith("tray:", StringComparison.Ordinal))
                    {
                        var ic = trayIcons.Find(x => "tray:" + x.Key == id);
                        if (ic != null && (ic.Version < 4 || ic.ShowTip)) { anchor = kv.Key; return FirstLine(ic.Tip); }
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
                var f = Ink.Semi(12f);
                float w = Math.Min(300, p.Measure(text, f) + 22), h = 26;
                float cx = Math.Max(w / 2 + 4, Math.Min(cw / scale - w / 2 - 4, anchor.X + anchor.Width / 2));
                float bottom = anchor.Y - 9 + (1 - t) * 5;
                var r = new RectangleF(cx - w / 2, bottom - h, w, h);
                p.Alpha = t;
                p.FillRound(r, 9, Ink.A(Ink.Base.Bg, 0.9f));
                p.StrokeRound(r, 9, p.T.Line, 1f);
                p.Text(text, f, p.T.Text, cx, r.Y + h / 2, w - 22, StringAlignment.Center);
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
                    DrawTrayIcon(ic, new RectangleF(x, top, 26, PlateH));
                    x += 26;
                }
                if (OverflowTray().Count > 0)
                {
                    var cr = new RectangleF(x, top, 24, PlateH);
                    bool hot = hoverId == "chev" || TrayExpanded;
                    Hit(cr, "chev", ToggleTrayCard, null);
                    if (hot) p.FillRound(cr, PlateR, TrayExpanded ? Ink.Mix(p.T.FillHover, p.T.Accent, 0.2f) :
                        pressId == "chev" ? p.T.FillPressed : p.T.FillHover);
                    // Points up while closed, down while the grid is open
                    p.Icon(TrayExpanded ? '\uE70D' : '\uE70E', 10f, hot ? p.T.Text : p.T.Sub, cr.X + cr.Width / 2, cr.Y + cr.Height / 2);
                    x += 24;
                }
                DrawStatus(new RectangleF(x, top, StatusWidth(), PlateH));
                x += StatusWidth() + 4;
                DrawClock(new RectangleF(x, top, ClockWidth(), PlateH));
                // Show desktop: the bar's far end, padding included
                var desk = new RectangleF(r.Right - 10, r.Y, 10 + padR, r.Height);
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
                float span = isle.hasBattery ? 66 : 36.5f;
                float cx = r.X + (r.Width - span) / 2 + 7.25f, cy = r.Y + r.Height / 2;
                var net = isle.net.Current;
                char netGlyph = !net.Any ? Ink.IcoNoNet : net.Wifi ? Ink.IcoWifi : '\uE839';
                p.Icon(netGlyph, 14.5f, net.Online || !net.Any ? p.T.Text : p.T.Sub, cx, cy);
                cx += 22;
                char volGlyph = isle.muted ? Ink.IcoMute : isle.vol < 0.01f ? Ink.IcoVol0 : isle.vol < 0.34f ? Ink.IcoVol1 : isle.vol < 0.67f ? Ink.IcoVol2 : Ink.IcoVol3;
                p.Icon(volGlyph, 14.5f, p.T.Text, cx, cy);
                if (isle.hasBattery)
                {
                    cx += 24;
                    p.Battery(cx - 9, cy, isle.battery / 100f, isle.battery <= 20 && !isle.onAc ? p.T.Negative : isle.onAc ? p.T.Positive : p.T.Text, isle.onAc);
                }
            }

            void DrawClock(RectangleF r)
            {
                bool hot = hoverId == "clock";
                Hit(r, "clock", delegate { ToggleSheet("cal"); }, ClockMenu);
                if (hot) p.FillRound(r, PlateR, pressId == "clock" ? p.T.FillPressed : p.T.FillHover);
                var now = DateTime.Now;
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                p.TextCenter(Fmt.Clock(now), Ink.Semi(12.5f), p.T.Text, cx, cy - 7);
                p.TextCenter(now.ToString("d MMM yyyy", Fmt.Id), Ink.Small(11f), p.T.Sub, cx, cy + 8);
                // Messages waiting: a count, like the app badges
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
