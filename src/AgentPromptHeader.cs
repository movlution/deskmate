using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Deskmate
{
    partial class Island
    {
        // Project, state, plan and quota
        class ChatHeader : Control
        {
            public Island Isle;
            public AgentSession S;
            public float DesignH = 90;
            public RectangleF Clock;
            const float Head = 22, Tile = 48, QuotaH = 62;
            const TextFormatFlags Flat = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            public ChatHeader()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                AccessibleRole = AccessibleRole.StaticText;
            }

            float Ui { get { var w = FindForm() as KitWindow; return w != null ? w.Ui : 1f; } }

            bool HasPlan { get { return S.Plan != null && S.Plan.Count > 0; } }

            // Layout changes only with these
            public int Shape()
            {
                return (HasPlan ? 1 : 0) | (NowLine(S).Length > 0 ? 2 : 0) | (Isle.QuotaFor(S) != null ? 4 : 0);
            }

            public void Measure()
            {
                float y = Head + Tile;
                bool now = NowLine(S).Length > 0;
                if (HasPlan) y += 20 + 5 + (now ? 8 + 18 : 0);
                else if (now) y += 14 + 18;
                if (Isle.QuotaFor(S) != null) y += 18 + QuotaH;
                DesignH = y + 20;
                AccessibleName = S.Project;
                AccessibleDescription = Spoken();
            }

            static string NowLine(AgentSession s)
            {
                if ((s.State == AgentState.Done || s.State == AgentState.Ended) && s.LastMessage.Length > 0) return s.LastMessage;
                string now = PlanNow(s);
                if (now.Length > 0) return now;
                return s.State == AgentState.Working || s.State == AgentState.Waiting ? AgentLine(s) ?? "" : "";
            }

            static string StateName(AgentSession s)
            {
                switch (s.State)
                {
                    case AgentState.Working: return Lang.T("Bekerja");
                    case AgentState.Waiting: return Lang.T("Menunggu izin");
                    case AgentState.Done: return s.Failed ? Lang.T("Gagal") : Lang.T("Selesai");
                    case AgentState.Ready: return Lang.T("Siap");
                }
                return Lang.T("Ditutup");
            }

            // Turn timer while working, else age
            static string StateTime(AgentSession s)
            {
                if (s.State == AgentState.Working)
                {
                    var t = DateTime.UtcNow - (s.TurnStartedUtc > DateTime.MinValue ? s.TurnStartedUtc : s.StartedUtc);
                    if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                    return t.TotalHours >= 1 ? (int)t.TotalHours + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
                }
                if (s.State == AgentState.Done || s.State == AgentState.Ended) return Fmt.Ago(s.LastUtc);
                return "";
            }

            static string Stats(AgentSession s)
            {
                var parts = new List<string> { Lang.F("Sesi {0}", Duration(DateTime.UtcNow - s.StartedUtc)) };
                if (s.Files.Count > 0) parts.Add(s.Files.Count + Lang.T(" berkas"));
                if (s.Commands > 0) parts.Add(s.Commands + Lang.T(" perintah"));
                return string.Join(" · ", parts.ToArray());
            }

            // Whole header as one sentence for readers
            string Spoken()
            {
                var parts = new List<string> { AgentTag(S), StateName(S) + (StateTime(S).Length > 0 ? " " + StateTime(S) : ""), Stats(S) };
                if (HasPlan) parts.Add(Lang.F("Rencana {0}", PlanCount(S)));
                string now = NowLine(S);
                if (now.Length > 0) parts.Add(now);
                var q = Isle.QuotaFor(S);
                if (q != null)
                {
                    Isle.QuotaTexts();
                    for (int i = 0; i < 3; i++)
                        if (q.W[i].Has) parts.Add(q.W[i].Label + " " + (q.W[i].Stale ? q.W[i].ResetText : q.W[i].PctText + ", " + q.W[i].ResetText));
                }
                return string.Join(". ", parts.ToArray());
            }

            static float Wide(Graphics g, string t, Font f)
            {
                return t.Length == 0 ? 0 : TextRenderer.MeasureText(g, t, f, new Size(int.MaxValue, int.MaxValue), Flat).Width;
            }

            // Left-aligned text on a centre line
            static float Put(Graphics g, string t, Font f, Color c, float x, float cy, float max)
            {
                if (t.Length == 0 || max <= 0) return x;
                float w = Math.Min(max, Wide(g, t, f));
                var r = new Rectangle((int)Math.Round(x), (int)Math.Round(cy - f.Height), (int)Math.Ceiling(w) + 1, f.Height * 2);
                TextRenderer.DrawText(g, t, f, r, c, Flat | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return x + w;
            }

            static void Round(Graphics g, RectangleF r, float radius, Color c)
            {
                using (var path = Ink.Round(r, radius))
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                KitInk.Smooth(g);
                g.Clear(KitInk.Window);
                float s = Ui, m = 24 * s, right = Width - m;

                // Agent tile, project, stats
                var tile = new RectangleF(m, Head * s, Tile * s, Tile * s);
                Round(g, tile, 13 * s, KitInk.Group);
                KitInk.Draw(g, Ink.Str(Ink.IcoTerminal), Ink.Icons(20 * s), KitInk.Text, tile, ContentAlignment.MiddleCenter);
                float tx = tile.Right + 14 * s, titleCy = tile.Y + 14 * s, lineCy = tile.Y + 37 * s;
                var small = Ink.Text(12.5f * s);
                string stats = Stats(S);
                float statsW = Wide(g, stats, small);
                Put(g, stats, small, KitInk.Sub, right - statsW, titleCy + 1 * s, statsW);
                Put(g, S.Project, Ink.Text(24f * s), KitInk.Text, tx, titleCy, right - statsW - 16 * s - tx);

                // Agent, live dot, state, time
                float x = Put(g, AgentTag(S), small, KitInk.Sub, tx, lineCy, 200 * s) + 12 * s;
                Dot(g, x + 4 * s, lineCy, s);
                x += 14 * s;
                x = Put(g, StateName(S), Ink.Semi(12.5f * s), KitInk.Text, x, lineCy, right - x);
                string time = StateTime(S);
                if (time.Length > 0) x = Put(g, " · " + time, small, KitInk.Sub, x, lineCy, right - x);
                Clock = new RectangleF(tx, lineCy - 10 * s, right - tx, 20 * s);

                float y = (Head + Tile) * s;
                string line = NowLine(S);
                var lf = Ink.Text(13f * s);
                if (HasPlan)
                {
                    y += 20 * s;
                    var cf = Ink.Semi(12f * s);
                    string count = PlanCount(S);
                    float cw = Wide(g, count, cf);
                    PlanBar(g, new RectangleF(m, y, right - m - cw - 12 * s, 5 * s), s);
                    Put(g, count, cf, KitInk.Sub, right - cw, y + 2.5f * s, cw);
                    y += 5 * s;
                    if (line.Length > 0) { y += 8 * s; Put(g, line, lf, KitInk.Sub, m, y + 9 * s, right - m); y += 18 * s; }
                }
                else if (line.Length > 0) { y += 14 * s; Put(g, line, lf, KitInk.Sub, m, y + 9 * s, right - m); y += 18 * s; }

                var q = Isle.QuotaFor(S);
                if (q != null) QuotaCard(g, q, new RectangleF(m, y + 18 * s, right - m, QuotaH * s), s);
            }

            void Dot(Graphics g, float cx, float cy, float s)
            {
                Color c;
                switch (S.State)
                {
                    case AgentState.Working: c = KitInk.Accent; break;
                    case AgentState.Waiting: c = KitInk.Text; break;
                    case AgentState.Done: c = KitInk.Sub; break;
                    case AgentState.Ready:
                        using (var pen = new Pen(KitInk.Sub, 1.5f * s)) g.DrawEllipse(pen, cx - 3.5f * s, cy - 3.5f * s, 7 * s, 7 * s);
                        return;
                    default: c = KitInk.Faint; break;
                }
                // Live states get a soft halo
                if (S.State == AgentState.Working || S.State == AgentState.Waiting)
                    using (var b = new SolidBrush(Color.FromArgb(56, c))) g.FillEllipse(b, cx - 7 * s, cy - 7 * s, 14 * s, 14 * s);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 4 * s, cy - 4 * s, 8 * s, 8 * s);
            }

            // Segment per step, like the Agen page
            void PlanBar(Graphics g, RectangleF r, float s)
            {
                int n = S.Plan.Count;
                var rest = Ink.Mix(KitInk.Window, KitInk.Text, Ink.Light ? 0.12f : 0.16f);
                var live = Ink.Mix(rest, KitInk.Accent, 0.5f);
                if (n > 14)
                {
                    Round(g, r, r.Height / 2, rest);
                    Round(g, new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * PlanDone(S) / n), r.Height), r.Height / 2, KitInk.Accent);
                    return;
                }
                float gap = 3 * s, w = (r.Width - gap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    int st = S.Plan[i].Status;
                    Round(g, new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height), r.Height / 2, st == 2 ? KitInk.Accent : st == 1 ? live : rest);
                }
            }

            // Rings per window; model and freshness right
            void QuotaCard(Graphics g, QuotaInfo q, RectangleF card, float s)
            {
                Isle.QuotaTexts();
                Round(g, card, 12 * s, KitInk.Group);
                float cy = card.Y + card.Height / 2, x = card.X + 18 * s, top = cy - 9 * s, low = cy + 10 * s;
                var lf = Ink.Text(13f * s);
                var pf = Ink.Semi(13f * s);
                var rf = Ink.Text(11.5f * s);
                var track = Ink.Mix(KitInk.Group, KitInk.Text, Ink.Light ? 0.12f : 0.16f);
                for (int i = 0; i < 3; i++)
                {
                    var w = q.W[i];
                    if (!w.Has) continue;
                    float r = 11 * s, rx = x + r;
                    using (var pen = new Pen(track, 3 * s)) g.DrawEllipse(pen, rx - r, cy - r, r * 2, r * 2);
                    if (!w.Stale && w.Pct > 0)
                        using (var pen = new Pen(KitInk.Accent, 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawArc(pen, rx - r, cy - r, r * 2, r * 2, -90, Math.Max(2f, 360f * Math.Min(1f, w.Pct / 100f)));
                    bool hot = !w.Stale && w.Pct >= 80;
                    float lx = x + 2 * r + 12 * s;
                    float ex = Put(g, w.Label, lf, hot ? KitInk.Text : KitInk.Sub, lx, top, 120 * s);
                    if (!w.Stale) ex = Put(g, w.PctText, pf, KitInk.Text, ex + 6 * s, top, 60 * s);
                    float ex2 = Put(g, w.ResetText, rf, KitInk.Faint, lx, low, 160 * s);
                    x = Math.Max(ex, ex2) + 28 * s;
                }
                float edge = card.Right - 18 * s, room = edge - x;
                float mw = Wide(g, q.Model, Ink.Text(12f * s)), tw = Wide(g, q.Tail, rf);
                if (q.Model.Length > 0 && mw <= room) Put(g, q.Model, Ink.Text(12f * s), KitInk.Sub, edge - mw, top, mw);
                if (tw <= room) Put(g, q.Tail, rf, KitInk.Faint, edge - tw, low, tw);
            }
        }
    }
}
