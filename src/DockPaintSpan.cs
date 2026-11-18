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
            // A capsule drawn once into its own bitmap, with its hit spots
            sealed class SideCache
            {
                public Bitmap Bmp;
                public string Sig = "";
                public readonly List<KeyValuePair<RectangleF, string>> Hits = new List<KeyValuePair<RectangleF, string>>();
                public readonly Dictionary<string, Action> Acts = new Dictionary<string, Action>(), Menus = new Dictionary<string, Action>();

                public void Drop()
                {
                    if (Bmp != null) Bmp.Dispose();
                    Bmp = null;
                    Sig = "";
                }
            }

            readonly SideCache leftSide = new SideCache(), rightSide = new SideCache();
            const int SpanPad = 4;
            // Pixel origin of the surface being drawn; zero for the canvas
            int offX, offY;

            // Widgets or tray, drawn once onto a clear bitmap and reused while
            // only their position changes (the bar grows under the pointer)
            void DrawSpan(RectangleF span, bool left)
            {
                if (span.IsEmpty && left) DropGhosts();
                if (span.IsEmpty) return;
                var c = left ? leftSide : rightSide;
                const int pad = SpanPad;
                int w = (int)Math.Ceiling(span.Width * scale) + pad * 2, h = (int)Math.Ceiling(span.Height * scale) + pad * 2;
                int px = (int)Math.Round(span.X * scale) - pad, py = (int)Math.Round(span.Y * scale) - pad;
                string sig = SpanSig(left) + "@" + w + "x" + h;
                if (sig != c.Sig || c.Bmp == null)
                {
                    if (left && c.Bmp != null) KeepGhosts(c.Bmp);
                    if (c.Bmp == null || c.Bmp.Width != w || c.Bmp.Height != h)
                    {
                        if (c.Bmp != null) c.Bmp.Dispose();
                        c.Bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    }
                    var main = g;
                    int from = hits.Count;
                    using (var sg = Graphics.FromImage(c.Bmp))
                    {
                        sg.Clear(Color.Transparent);
                        sg.SmoothingMode = SmoothingMode.AntiAlias;
                        sg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        // The span lands exactly on (pad, pad)
                        sg.TranslateTransform(pad - span.X * scale, pad - span.Y * scale);
                        sg.ScaleTransform(scale, scale);
                        g = sg;
                        p.G = sg;
                        offX = px;
                        offY = py;
                        try { if (left) DrawLeft(); else DrawRight(); }
                        finally
                        {
                            g = main;
                            p.G = main;
                            offX = offY = 0;
                        }
                    }
                    c.Sig = sig;
                    c.Hits.Clear();
                    c.Acts.Clear();
                    c.Menus.Clear();
                    for (int i = from; i < hits.Count; i++)
                    {
                        string id = hits[i].Value;
                        var r = hits[i].Key;
                        r.Offset(-span.X, -span.Y);
                        c.Hits.Add(new KeyValuePair<RectangleF, string>(r, id));
                        Action a;
                        if (acts.TryGetValue(id, out a)) c.Acts[id] = a;
                        if (menus.TryGetValue(id, out a)) c.Menus[id] = a;
                    }
                    hits.RemoveRange(from, hits.Count - from);
                }
                // Hit spots and tray anchors follow the span
                foreach (var kv in c.Hits)
                {
                    var r = kv.Key;
                    r.Offset(span.X, span.Y);
                    hits.Add(new KeyValuePair<RectangleF, string>(r, kv.Value));
                    if (!left && kv.Value.StartsWith("tray:", StringComparison.Ordinal))
                    {
                        var ic = TrayById(kv.Value);
                        if (ic != null) ic.Spot = ToScreen(r);
                    }
                }
                foreach (var kv in c.Acts) acts[kv.Key] = kv.Value;
                foreach (var kv in c.Menus) menus[kv.Key] = kv.Value;
                var st = g.Save();
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(c.Bmp, px, py, w, h);
                g.Restore(st);
            }

            // Thin lines between widgets, apps and the tray
            void Dividers()
            {
                if (!float.IsNaN(sepA)) g.DrawLine(p.P(Ink.A(p.T.Line, 1.6f * leftPresence), 1f), sepA, barTop + 12, sepA, barBottom - 12);
                if (!float.IsNaN(sepB)) g.DrawLine(p.P(Ink.A(p.T.Line, 1.6f), 1f), sepB, barTop + 12, sepB, barBottom - 12);
            }

            // Everything a span shows, apart from where it sits
            string SpanSig(bool left)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(Ink.Epoch).Append(scale).Append(Lang.English).Append('|');
                if (hoverId != null && Owns(hoverId, left)) sb.Append('h').Append(hoverId);
                if (pressId != null && Owns(pressId, left)) sb.Append('p').Append(pressId);
                var now = DateTime.Now;
                if (left)
                {
                    foreach (var id in LeftWidgets()) sb.Append(id).Append(',');
                    foreach (var id in DockWidgetIds) sb.Append((int)(WidgetShown(id) * 1000)).Append(',');
                    sb.Append(Fmt.Clock(now)).Append(now.Day);
                    var mi = isle.mediaInfo;
                    if (mi.Present) sb.Append(mi.Title).Append(mi.Artist).Append(mi.App).Append(mi.Playing).Append(mi.Cover != null ? mi.Cover.GetHashCode() : 0);
                    sb.Append(isle.quotaVersion).Append(isle.BusyCount());
                    if (isle.TimerOn) sb.Append(isle.TimerClock()).Append(isle.timerPaused).Append(isle.TimerLabel()).Append((int)(isle.TimerFraction() * 300));
                    if (isle.weather != null) sb.Append(isle.weather.Temp).Append(isle.weather.Code);
                    sb.Append(isle.battery).Append(isle.onAc).Append(cpuPct).Append(ramPct);
                }
                else
                {
                    foreach (var ic in InlineTray()) sb.Append(ic.Key).Append(ic.Stamp).Append(',');
                    sb.Append(OverflowTray().Count > 0).Append(TrayExpanded);
                    sb.Append(isle.net.Current.Sig).Append(Math.Round(isle.vol, 2)).Append(isle.muted);
                    sb.Append(isle.hasBattery).Append(isle.battery).Append(isle.onAc);
                    sb.Append(Fmt.Clock(now)).Append(now.Day).Append(isle.unread);
                }
                return sb.ToString();
            }

            static bool Owns(string id, bool left)
            {
                if (left) return id.StartsWith("w_", StringComparison.Ordinal);
                return id.StartsWith("tray:", StringComparison.Ordinal) || id == "chev" || id == "status" || id == "vol" || id == "clock" || id == "desk";
            }

            public void FreeCaches()
            {
                foreach (var b in restIcons.Values) if (b != null) b.Dispose();
                foreach (var b in bigIcons.Values) if (b != null) b.Dispose();
                restIcons.Clear();
                bigIcons.Clear();
                DropGhosts();
                leftSide.Drop();
                rightSide.Drop();
            }

            void DrawExact(Bitmap bmp, RectangleF r)
            {
                var st = g.Save();
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, (int)Math.Round(r.X * scale) - offX, (int)Math.Round(r.Y * scale) - offY, bmp.Width, bmp.Height);
                g.Restore(st);
            }

            // macOS-style running dot: a small circle, slightly larger for the
            // front window; accent when the app calls for attention.
            void Indicator(DockApp a, RectangleF r) { Indicator(a.Dot.Value, a.Attention, r); }

            void Indicator(float v, bool attention, RectangleF r)
            {
                if (v < 0.02f) return;
                float focus = Ease.Clamp01(v - 1), cx = r.X + r.Width / 2, cy = barBottom - 4.8f;
                float d = 3.2f + 1.4f * focus;
                Color c = attention ? p.T.Accent : Ink.Mix(Ink.A(p.T.Text, 0.66f), p.T.Text, focus);
                p.Circle(cx, cy, d / 2, Ink.A(c, Ease.Clamp01(v)));
            }

            // Unread count from Windows notifications
            void Badge(RectangleF r, int n)
            {
                string s = n > 99 ? "99+" : n.ToString();
                var f = Ink.SmallSemi(11f);
                float h = 17, w = Math.Max(h, p.Measure(s, f) + 9);
                var b = new RectangleF(r.Right - w * 0.7f, r.Top - h * 0.28f, w, h);
                p.FillRound(Inset(b, -1.2f), (h + 2.4f) / 2, Ink.A(p.T.Bg, 0.55f));
                p.FillRound(b, h / 2, p.T.Negative);
                p.TextCenter(s, f, p.T.IsDark ? p.T.Bg : Color.White, b.X + b.Width / 2, b.Y + h / 2);
            }
        }
    }
}
