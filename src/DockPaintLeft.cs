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
            // Live widgets in the left capsule
            void DrawLeft()
            {
                if (leftPill.IsEmpty) return;
                float x = leftPill.X;
                var want = LeftWidgets();
                foreach (var id in DockWidgetIds)
                {
                    float s = WidgetShown(id);
                    if (s < 0.01f) { DropGhost(id); continue; }
                    bool here = want.Contains(id);
                    var r = new RectangleF(x, leftPill.Y + WidgetIn, WidgetW(id), leftPill.Height - WidgetIn * 2);
                    x += (WidgetW(id) + 6) * s;
                    if (here && s > 0.995f)
                    {
                        DrawWidget(id, r);
                        widgetPx[id] = new Point((int)Math.Floor((r.X - 1 - leftPill.X) * scale) + SpanPad, (int)Math.Ceiling((r.Right + 1 - leftPill.X) * scale) + SpanPad);
                        continue;
                    }
                    // Arriving or leaving: fades while resizing
                    widgetPx.Remove(id);
                    var st = g.Save();
                    g.SetClip(new RectangleF(r.X - 1, leftPill.Y, (r.Width + 2) * s, leftPill.Height), CombineMode.Intersect);
                    if (here) { p.Alpha = s * s; DrawWidget(id, r); p.Alpha = 1; }
                    else DrawGhost(id, r, s * s);
                    g.Restore(st);
                }
            }

            // Leavers' last picture and bitmap columns
            readonly Dictionary<string, Bitmap> ghosts = new Dictionary<string, Bitmap>();
            readonly Dictionary<string, Point> widgetPx = new Dictionary<string, Point>();

            // Before a redraw, leavers keep their look
            void KeepGhosts(Bitmap old)
            {
                var want = LeftWidgets();
                foreach (var id in DockWidgetIds)
                {
                    Point cols;
                    if (want.Contains(id)) { DropGhost(id); continue; }
                    if (ghosts.ContainsKey(id) || !widgetPx.TryGetValue(id, out cols)) continue;
                    widgetPx.Remove(id);
                    var at = Rectangle.Intersect(Rectangle.FromLTRB(cols.X, 0, cols.Y, old.Height), new Rectangle(0, 0, old.Width, old.Height));
                    if (at.Width > 0 && at.Height > 0) ghosts[id] = old.Clone(at, PixelFormat.Format32bppPArgb);
                }
            }

            void DropGhost(string id)
            {
                Bitmap b;
                if (!ghosts.TryGetValue(id, out b)) return;
                b.Dispose();
                ghosts.Remove(id);
            }

            void DropGhosts()
            {
                foreach (var b in ghosts.Values) b.Dispose();
                ghosts.Clear();
                widgetPx.Clear();
            }

            void DrawGhost(string id, RectangleF r, float alpha)
            {
                Bitmap b;
                if (!ghosts.TryGetValue(id, out b)) return;
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = Ease.Clamp01(alpha) });
                    var st = g.Save();
                    g.ResetTransform();
                    int dx = (int)Math.Floor((r.X - 1 - leftPill.X) * scale) + SpanPad;
                    g.DrawImage(b, new Rectangle(dx, 0, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
                    g.Restore(st);
                }
            }

            void DrawWidget(string id, RectangleF r)
            {
                string hid = "w_" + id;
                bool hot = hoverId == hid;
                Action open = null;
                switch (id)
                {
                    case "clock": open = delegate { isle.OpenFromDock(PageHome); }; break;
                    case "media": open = delegate { isle.OpenFromDock(PageMusic); }; break;
                    case "ai": open = delegate { isle.OpenFromDock(PageAgents); }; break;
                    case "timer": open = delegate { isle.OpenFromDock(PageControl); }; break;
                    case "weather": open = delegate { ToggleSheet("weather"); }; break;
                    case "battery": open = delegate { isle.OpenFromDock(PageControl); }; break;
                    case "system": open = delegate { Open("taskmgr.exe"); }; break;
                }
                Hit(r, hid, open, null);
                if (hot && open != null) p.FillRound(r, WidgetR, pressId == hid ? p.T.FillPressed : p.T.Fill);
                float cy = r.Y + r.Height / 2;
                switch (id)
                {
                    case "clock":
                        p.Text(Fmt.Clock(DateTime.Now), Ink.Display(18f), p.T.Text, r.X + 10, cy - 7, r.Width - 14, StringAlignment.Near);
                        p.Text(DateTime.Now.ToString("ddd, d MMM", Fmt.Id), Ink.Small(11f), p.T.Sub, r.X + 10, cy + 11, r.Width - 14, StringAlignment.Near);
                        break;
                    case "media": DrawMedia(r, cy); break;
                    case "ai": DrawAi(r, cy); break;
                    case "timer":
                        Ring(r.X + 20, cy, 11, isle.TimerFraction(), isle.timerPaused);
                        p.Text(isle.TimerClock(), Ink.Semi(13.5f), isle.timerPaused ? p.T.Sub : p.T.Text, r.X + 38, cy - 6, r.Width - 42, StringAlignment.Near);
                        p.Text(isle.TimerLabel(), Ink.Small(11f), p.T.Sub, r.X + 38, cy + 10, r.Width - 42, StringAlignment.Near);
                        break;
                    case "weather":
                        WeatherIcon(p, isle.weather.Code, r.X + 19, cy, 17f, p.T.Text);
                        p.Text(isle.weather.Temp + "°", Ink.Semi(14f), p.T.Text, r.X + 35, cy - 6, r.Width - 37, StringAlignment.Near);
                        p.Text(WeatherWord(isle.weather.Code), Ink.Small(11f), p.T.Sub, r.X + 35, cy + 10, r.Width - 37, StringAlignment.Near);
                        break;
                    case "battery":
                        p.Battery(r.X + 9, cy, isle.battery / 100f, isle.battery <= 20 && !isle.onAc ? p.T.Negative : p.T.Text, isle.onAc);
                        p.Text(Ink.Percent(isle.battery), Ink.Semi(14f), isle.battery <= 20 && !isle.onAc ? p.T.Negative : p.T.Text, r.X + 44, cy - 6, r.Width - 46, StringAlignment.Near);
                        p.Text(isle.onAc ? Lang.T("mengisi daya") : Lang.T("Baterai"), Ink.Small(11f), p.T.Sub, r.X + 44, cy + 10, r.Width - 46, StringAlignment.Near);
                        break;
                    case "system":
                        Meter(r.X + 10, cy - 8, r.Width - 18, "CPU", cpuPct);
                        Meter(r.X + 10, cy + 10, r.Width - 18, "RAM", ramPct);
                        break;
                }
            }

            // Font has sun and cloud only
            public static void WeatherIcon(Painter p, int code, float cx, float cy, float size, Color c)
            {
                if (code == 0) { p.Icon('\uE706', size, c, cx, cy); return; }
                if (code == 45 || code == 48)
                {
                    var fog = p.P(c, Math.Max(1.2f, size / 13));
                    for (int i = 0; i < 3; i++)
                    {
                        float half = size * (i == 1 ? 0.42f : 0.32f), y = cy - size * 0.24f + i * size * 0.24f;
                        p.G.DrawLine(fog, cx - half, y, cx + half, y);
                    }
                    return;
                }
                bool storm = code >= 95, drizzle = code >= 51 && code <= 57, wet = storm || IsRain(code) || drizzle;
                if (!wet) { p.Icon('\uE753', size, c, cx, cy); return; }
                p.Icon('\uE753', size, c, cx, cy - size * 0.16f);
                var pen = p.P(c, Math.Max(1.2f, size / 13));
                float top = cy + size * 0.16f, bottom = cy + size * 0.42f;
                if (storm)
                {
                    float s = size;
                    p.G.DrawLines(pen, new[] { new PointF(cx + s * 0.06f, top), new PointF(cx - s * 0.08f, cy + s * 0.31f), new PointF(cx + s * 0.06f, cy + s * 0.31f), new PointF(cx - s * 0.08f, bottom + s * 0.04f) });
                    return;
                }
                for (int i = -1; i <= 1; i++)
                {
                    float x = cx + i * size * 0.22f;
                    if (drizzle) p.Circle(x, top + size * 0.12f, size / 18, c);
                    else p.G.DrawLine(pen, x + size * 0.05f, top, x - size * 0.05f, bottom);
                }
            }

            // Cover, title, and a play button that stays put
            void DrawMedia(RectangleF r, float cy)
            {
                var mi = isle.mediaInfo;
                var cover = new RectangleF(r.X + 6, cy - 18, 36, 36);
                if (mi.Cover != null) p.Image(mi.Cover, cover, 9);
                else p.FillRound(cover, 9, p.T.FillHover);
                float tx = cover.Right + 9, play = r.Right - 18;
                p.Text(mi.Title, Ink.Semi(12.5f), p.T.Text, tx, cy - 7, play - 18 - tx, StringAlignment.Near);
                p.Text(mi.Artist.Length > 0 ? mi.Artist : mi.App, Ink.Small(11f), p.T.Sub, tx, cy + 9, play - 18 - tx, StringAlignment.Near);
                var btn = new RectangleF(play - 14, cy - 14, 28, 28);
                Hit(btn, "w_play", delegate { isle.media.TogglePlay(); }, null);
                p.Circle(play, cy, hoverId == "w_play" ? 14 : 13, p.T.Text);
                if (mi.Playing) p.Pause(play, cy, 9.5f, p.T.Bg);
                else p.Play(play, cy, 10f, p.T.Bg);
            }

            // Highest quota as a ring; a busy agent fills its centre
            void DrawAi(RectangleF r, float cy)
            {
                float top = 0;
                string label = "";
                foreach (var q in isle.quotas)
                {
                    if (!QuotaShown(q)) continue;
                    for (int i = 0; i < q.W.Length; i++)
                        if (q.W[i].Has && q.W[i].Pct >= top) { top = q.W[i].Pct; label = WindowLabel(q.W[i], i); }
                }
                int busy = isle.BusyCount();
                Ring(r.X + 20, cy, 12, top / 100f, false);
                if (busy > 0) p.Circle(r.X + 20, cy, 4f, p.T.Accent);
                string big = label.Length > 0 ? Ink.Percent((int)Math.Floor(top)) : Lang.F("{0} agen", busy);
                p.Text(big, Ink.Semi(13.5f), p.T.Text, r.X + 40, cy - 6, r.Width - 44, StringAlignment.Near);
                string small = busy > 0 ? Lang.F("{0} bekerja", busy) : label;
                p.Text(small, Ink.Small(11f), p.T.Sub, r.X + 40, cy + 10, r.Width - 44, StringAlignment.Near);
            }

            void Ring(float cx, float cy, float rad, float f, bool quiet)
            {
                var rect = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);
                g.DrawEllipse(p.P(p.T.Line, 2.4f), rect);
                float sweep = 360f * Ease.Clamp01(f);
                if (sweep > 0.5f) g.DrawArc(p.P(quiet ? p.T.Faint : p.T.Progress, 2.4f), rect, -90, sweep);
            }

            void Meter(float x, float cy, float w, string name, int pct)
            {
                p.Text(name, Ink.SmallSemi(11f), p.T.Sub, x, cy, 30, StringAlignment.Near);
                p.TextRight(pct < 0 ? "–" : Ink.Percent(pct), Ink.SmallSemi(11f), p.T.Text, x + w, cy);
                var track = new RectangleF(x + 30, cy - 1.5f, w - 30 - 34, 3);
                p.FillRound(track, 1.5f, p.T.Line);
                if (pct > 0) p.FillRound(new RectangleF(track.X, track.Y, Math.Max(3, track.Width * pct / 100f), 3), 1.5f, pct >= 90 ? p.T.Negative : p.T.Progress);
            }
        }
    }
}
