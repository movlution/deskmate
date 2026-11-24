using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Claude plan usage: Agen line, glance, warnings
    partial class Island
    {
        // Toast marker: DrawInfo draws a ring
        const char IcoGauge = '';
        const float AgentQuotaPitch = 70, QuotaGlanceRowH = 32;
        static readonly string[] QuotaKeys = { "five_hour", "seven_day", "spend_limit" };
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        readonly List<QuotaInfo> quotas = new List<QuotaInfo>();
        int quotaVersion, quotaBuilt = -1, quotaSig;
        long quotaMinute;
        bool quotaEnglish;
        Action actQuotaOpen;
        float quotaToastLevel;
        // Column starts, shared by stacked lines
        readonly float[] quotaCols = new float[3];
        float quotaWhoW;
        // Pulse slots: no closure per frame
        readonly float[] pulseX = new float[6], pulseY = new float[6];
        Action<DateTime>[] pulseActs;

        void QuotaActs()
        {
            if (actQuotaOpen != null) return;
            actQuotaOpen = delegate { Show(PageAgents); };
            pulseActs = new Action<DateTime>[pulseX.Length];
            for (int i = 0; i < pulseActs.Length; i++)
            {
                int k = i;
                pulseActs[i] = t => QuotaPulse(pulseX[k], pulseY[k], t);
            }
        }

        void OnUsage(HookEvent ev)
        {
            string agent = Json.Str(ev.Data, "agent");
            if (agent != "" && agent != "codex" && agent != "antigravity" && agent != "copilot") return;
            // Claude and other agents switch apart
            if (!settings.GetBool(agent.Length > 0 ? StatusSetup.AgentsKey : "agent.quota", true)) return;
            string profile = agent.Length > 0 ? AgentAdapters.Label(agent) : StatusLine.SafeName(CleanText(Json.Str(ev.Data, "profile"), 24));
            if (agent.Length == 0 && Json.Str(ev.Data, "profile").Length == 0) profile = "";
            var q = quotas.Find(x => x.Profile == profile);
            if (q == null)
            {
                // Few profiles; oldest goes
                if (quotas.Count >= 6)
                {
                    var oldest = quotas[0];
                    foreach (var x in quotas) if (x.AtUtc < oldest.AtUtc) oldest = x;
                    quotas.Remove(oldest);
                }
                q = new QuotaInfo { Profile = profile, Agent = agent };
                quotas.Add(q);
                // Claude first, then other agents
                quotas.Sort((a, b) => a.Agent.Length != b.Agent.Length ? a.Agent.Length.CompareTo(b.Agent.Length) : string.CompareOrdinal(a.Profile, b.Profile));
            }
            var now = DateTime.UtcNow;
            q.Model = CleanText(Json.Str(ev.Data, "model"), 40);
            q.AtUtc = now;
            double lo = (now.AddDays(-8) - Epoch).TotalSeconds, hi = (now.AddDays(40) - Epoch).TotalSeconds;
            for (int i = 0; i < QuotaKeys.Length; i++)
            {
                var src = Json.Obj(ev.Data, agent.Length > 0 ? "w" + i : QuotaKeys[i]);
                q.W[i].Kind = CleanText(Json.Str(src, "kind"), 12);
                q.W[i].Minutes = (int)Math.Max(0, Math.Min(100000, Json.Num(src, "minutes", 0)));
                double pct = Json.Num(src, "used_percentage", double.NaN), at = Json.Num(src, "resets_at", double.NaN);
                var w = q.W[i];
                if (src == null || double.IsNaN(pct) || double.IsInfinity(pct) || double.IsNaN(at) || at < lo || at > hi) { w.Has = false; continue; }
                var reset = Epoch.AddSeconds(Math.Round(at));
                // A later reset is a new window
                if (!w.Has || reset > w.ResetUtc.AddMinutes(30)) w.Told = 0;
                w.Has = true;
                w.Pct = (float)Math.Max(0, Math.Min(100, pct));
                w.ResetUtc = reset;
                QuotaWarn(q, i, w);
            }
            quotaVersion++;
            int sig = QuotaSig();
            if (sig != quotaSig && expanded) Kick();
            quotaSig = sig;
        }

        int QuotaSig()
        {
            int h = quotas.Count;
            foreach (var q in quotas)
                foreach (var w in q.W) h = h * 31 + (w.Has ? (int)w.Pct * 7919 + w.ResetUtc.Minute + 1 : 0);
            return h;
        }

        // Once per window at 80 and 95
        void QuotaWarn(QuotaInfo q, int i, QuotaWindow w)
        {
            int level = w.Pct >= 95 ? 95 : w.Pct >= 80 ? 80 : 0;
            if (level <= w.Told || DateTime.UtcNow >= w.ResetUtc) return;
            w.Told = level;
            QuotaActs();
            var t = level >= 95 ? ShowToast(View.Info, 5, 3, false, true) : ShowToast(View.Info, 3.6, 2, false);
            if (t == null) return;
            if (level >= 95) EyesReact(EyeMood.Worried, 0);
            t.Glyph = IcoGauge;
            quotaToastLevel = w.Pct / 100f;
            t.Title = Lang.F("Kuota {0} {1}", QuotaWho(q) + " " + WindowLabel(w, i), Ink.Percent((int)w.Pct));
            t.Detail = QuotaReset(w.ResetUtc);
            t.Data = actQuotaOpen;
        }

        // Which AI the line belongs to
        static string QuotaWho(QuotaInfo q)
        {
            if (q.Agent.Length > 0) return q.Profile;
            return q.Profile.Length > 0 ? "Claude · " + q.Profile : "Claude";
        }

        static string QuotaLabel(int i) { return i == 0 ? Lang.T("5 jam") : i == 1 ? Lang.T("7 hari") : Lang.T("Batas"); }

        // Claude windows are fixed; others carry theirs
        static string WindowLabel(QuotaWindow w, int i)
        {
            if (w.Kind == "gemini") return "Gemini";
            if (w.Kind == "other") return Lang.T("Model lain");
            if (w.Kind == "premium") return "Premium";
            if (w.Minutes == 300) return QuotaLabel(0);
            if (w.Minutes == 10080) return QuotaLabel(1);
            if (w.Minutes > 0) return w.Minutes % 1440 == 0 ? Lang.F("{0} hari", w.Minutes / 1440) : Lang.F("{0} jam", Math.Max(1, w.Minutes / 60));
            return QuotaLabel(i);
        }

        static string QuotaReset(DateTime utc)
        {
            var local = utc.ToLocalTime();
            if (local.Date == DateTime.Today) return Lang.T("reset ") + Fmt.Hm(local);
            // Past a week a weekday misleads
            if ((local.Date - DateTime.Today).TotalDays >= 6) return Lang.T("reset ") + local.ToString("d MMM", Fmt.Id) + " " + Fmt.Hm(local);
            return Lang.T("reset ") + local.ToString("ddd", Fmt.Id) + " " + Fmt.Hm(local);
        }

        // Data older than a week hides
        static bool QuotaShown(QuotaInfo q)
        {
            if ((DateTime.UtcNow - q.AtUtc).TotalDays > 8) return false;
            foreach (var w in q.W) if (w.Has) return true;
            return false;
        }

        static float QuotaPeak(QuotaInfo q)
        {
            float top = 0;
            var now = DateTime.UtcNow;
            foreach (var w in q.W) if (w.Has && now < w.ResetUtc) top = Math.Max(top, w.Pct);
            return top;
        }

        int QuotaCount(bool notable)
        {
            int n = 0;
            foreach (var q in quotas) if (QuotaShown(q) && (!notable || QuotaPeak(q) >= 70)) n++;
            return Math.Min(4, n);
        }

        // Agen page height, 0 = none
        float QuotaBlockH() { int n = QuotaCount(false); return n == 0 ? 0 : n * AgentQuotaPitch + 28; }

        float QuotaGlanceH() { int n = QuotaCount(true); return n == 0 ? 0 : n * QuotaGlanceRowH + HomeGap - 2; }

        // Texts rebuilt on change or minute
        void QuotaTexts()
        {
            long minute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
            if (quotaBuilt == quotaVersion && quotaMinute == minute && quotaEnglish == Lang.English) return;
            quotaBuilt = quotaVersion;
            quotaMinute = minute;
            quotaEnglish = Lang.English;
            var now = DateTime.UtcNow;
            foreach (var q in quotas)
            {
                q.Who = QuotaWho(q);
                for (int i = 0; i < q.W.Length; i++)
                {
                    var w = q.W[i];
                    if (!w.Has) continue;
                    w.Label = WindowLabel(w, i);
                    w.Stale = now >= w.ResetUtc;
                    w.PctText = Ink.Percent((int)Math.Floor(w.Pct));
                    w.ResetText = w.Stale ? Lang.T("sudah reset") : QuotaReset(w.ResetUtc);
                }
                q.Tail = Lang.T("diperbarui ") + Fmt.Ago(q.AtUtc);
            }
        }

        // Agent page: fixed-width columns inside each profile card.
        float DrawQuotaBlock(float w, float top)
        {
            float h = QuotaBlockH();
            if (h <= 0) return 0;
            QuotaTexts();
            p.Text(Lang.T("Kuota agent"), Ink.SmallSemi(12), p.T.Text, 26, top + 10, w - 52, StringAlignment.Near);
            int shown = 0;
            foreach (var q in quotas)
            {
                if (!QuotaShown(q) || shown >= 4) continue;
                var card = new RectangleF(18, top + 22 + shown++ * AgentQuotaPitch, w - 36, AgentQuotaPitch - 6);
                p.FillRound(card, 10, p.T.Fill);
                p.Text(q.Who, Ink.SmallSemi(12), p.T.Text, card.Left + 10, card.Top + 12, card.Width - 20, StringAlignment.Near);
                int count = 0;
                foreach (var item in q.W) if (item.Has) count++;
                float col = (card.Width - 20) / count;
                int index = 0;
                foreach (var item in q.W)
                {
                    if (!item.Has) continue;
                    float x = card.Left + 10 + index++ * col, width = col - 10;
                    Color tone = item.Stale ? p.T.Sub : item.Pct >= 95 ? p.T.Negative : item.Pct >= 80 ? p.T.Warning : p.T.Accent;
                    string usage = item.Stale ? "" : "  " + item.PctText;
                    p.Text(item.Label + usage, Ink.SmallSemi(11.5f), p.T.Text, x, card.Top + 28, width, StringAlignment.Near);
                    p.FillRound(new RectangleF(x, card.Top + 38, width, 4), 2, p.T.Line);
                    float filled = item.Stale ? 0 : width * Ease.Clamp01(item.Pct / 100f);
                    if (filled > 0) p.FillRound(new RectangleF(x, card.Top + 38, filled, 4), Math.Min(2, filled / 2), tone);
                    p.Text(item.ResetText, Ink.Caption, p.T.Sub, x, card.Top + 52, width, StringAlignment.Near);
                }
            }
            return h;
        }

        // Ringkasan: only when a window runs high
        void DrawQuotaGlance(float x, float y, float full)
        {
            QuotaActs();
            QuotaTexts();
            int shown = 0, slot = 0;
            QuotaColumns(true);
            foreach (var q in quotas)
            {
                if (!QuotaShown(q) || QuotaPeak(q) < 70 || shown >= 2) continue;
                var r = new RectangleF(x, y + shown * QuotaGlanceRowH, full, QuotaGlanceRowH - 2);
                string id = shown == 0 ? "homeQuota" : "homeQuota2";
                AddHit(r, id, actQuotaOpen, null, HomeMenuAct(HomeCard.Quota));
                if (Hover(id)) p.FillRound(r, Ink.RadiusControl, Pressed(id) ? p.T.FillPressed : p.T.FillHover);
                QuotaLine(q, x + 16, r.Right - 12, r.Y + r.Height / 2, q.Tail, ref slot);
                shown++;
            }
        }

        // Widest item per window across lines
        void QuotaColumns(bool notable)
        {
            var lf = Ink.Caption;
            var pf = Ink.SmallSemi(11.5f);
            quotaWhoW = 0;
            foreach (var q in quotas)
                if (QuotaShown(q) && (!notable || QuotaPeak(q) >= 70)) quotaWhoW = Math.Max(quotaWhoW, p.Measure(q.Who, pf));
            for (int i = 0; i < 3; i++)
            {
                float widest = 0;
                foreach (var q in quotas)
                {
                    var w = q.W[i];
                    if (!w.Has || !QuotaShown(q) || (notable && QuotaPeak(q) < 70)) continue;
                    float iw = 20 + p.Measure(w.Label, lf) + 5 + (w.Stale ? 0 : p.Measure(w.PctText, pf) + 7) + p.Measure(w.ResetText, lf);
                    widest = Math.Max(widest, iw);
                }
                quotaCols[i] = widest;
            }
        }

        // AI name, then ring, label, percent, reset; tail right
        void QuotaLine(QuotaInfo q, float cx, float right, float cy, string tail, ref int slot)
        {
            var lf = Ink.Caption;
            var pf = Ink.SmallSemi(11.5f);
            float x = cx - 6.5f;
            p.Text(q.Who, pf, p.T.Text, x, cy, quotaWhoW + 2, StringAlignment.Near);
            x += quotaWhoW + 14;
            for (int i = 0; i < 3; i++)
            {
                var w = q.W[i];
                if (!w.Has) continue;
                bool hot = !w.Stale && w.Pct >= 80;
                float rx = x + 6.5f;
                if (!w.Stale && w.Pct >= 95 && !Anim.Reduced && slot < pulseX.Length)
                {
                    pulseX[slot] = rx;
                    pulseY[slot] = cy;
                    LiveRect(new RectangleF(rx - 11, cy - 11, 22, 22), 15, pulseActs[slot]);
                    slot++;
                }
                else Ring(rx, cy, 6.5f, w.Stale ? 0 : w.Pct / 100f, w.Stale ? p.T.Accent : w.Pct >= 95 ? p.T.Negative : hot ? p.T.Warning : p.T.Accent, 2f);
                float tx = x + 20;
                p.Text(w.Label, lf, hot ? p.T.Text : p.T.Sub, tx, cy, 200, StringAlignment.Near);
                tx += p.Measure(w.Label, lf) + 5;
                if (!w.Stale)
                {
                    p.Text(w.PctText, pf, hot ? p.T.Text : p.T.Sub, tx, cy, 60, StringAlignment.Near);
                    tx += p.Measure(w.PctText, pf) + 7;
                }
                p.Text(w.ResetText, lf, p.T.Faint, tx, cy, 200, StringAlignment.Near);
                x += Math.Max(quotaCols[i], tx + p.Measure(w.ResetText, lf) - x) + 22;
            }
            if (right - x > p.Measure(tail, lf) + 4) p.TextRight(tail, lf, p.T.Faint, right, cy);
        }

        // Near the limit: the ring breathes
        void QuotaPulse(float cx, float cy, DateTime t)
        {
            double phase = (t.Ticks / TimeSpan.TicksPerMillisecond % 1800) / 1800.0;
            float k = (float)(0.5 - 0.5 * Math.Cos(phase * Math.PI * 2));
            p.Circle(cx, cy, 9.5f + k * 1.5f, Ink.A(p.T.Accent, 0.10f + 0.16f * k));
            Ring(cx, cy, 6.5f, 1, p.T.Accent, 2f);
        }

        // Any agent's quota watched: one switch for all
        bool QuotaOn { get { return settings.GetBool("agent.quota", true) || settings.GetBool(StatusSetup.AgentsKey, true); } }

        // Settings row: each agent's first window
        string QuotaNote()
        {
            if (!QuotaOn) return Lang.T("Claude, Codex, Antigravity, dan Copilot, dari status line dan lognya");
            QuotaTexts();
            var parts = new List<string>();
            foreach (var q in quotas)
            {
                if (!QuotaShown(q)) continue;
                foreach (var w in q.W)
                {
                    if (!w.Has) continue;
                    parts.Add(q.Who + " " + w.Label + " " + (w.Stale ? Lang.T("sudah reset") : w.PctText));
                    break;
                }
            }
            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : Lang.T("Muncul setelah sesi agent berikutnya");
        }

        // Claude and Antigravity by status line, Codex and Copilot by log
        void SetQuota(bool on)
        {
            settings.Set("agent.quota", on);
            settings.Set(StatusSetup.AgentsKey, on);
            settings.Save();
            string err = null;
            try { if (HooksInstalled()) { if (on) err = StatusSetup.Install(); else StatusSetup.Remove(); } }
            catch (Exception ex) { Log.Write("statusline setup: " + ex.Message); err = Fmt.OneLine(ex.Message, 60); }
            try { if (AgentSetup.Installed("antigravity")) { if (on) StatusSetup.AgyInstall(); else StatusSetup.AgyRemove(); } }
            catch (Exception ex) { Log.Write("agy statusline: " + ex.Message); }
            if (on) { LogQuota("codex"); LogQuota("copilot"); }
            else { quotas.Clear(); quotaVersion++; }
            if (err != null) ShowInfo(IcoGauge, Lang.T("Kuota belum dipantau"), err, true);
            else ShowInfo(IcoGauge, on ? Lang.T("Kuota agent dipantau") : Lang.T("Kuota agent dilepas"), "", true);
            Kick();
        }

        readonly HashSet<string> logReading = new HashSet<string>();

        // Codex and Copilot: quota in their session logs
        void LogQuota(string agent)
        {
            if (logReading.Contains(agent) || !settings.GetBool(StatusSetup.AgentsKey, true) || !AgentSetup.Installed(agent)) return;
            logReading.Add(agent);
            string home = Path.GetDirectoryName(AgentSetup.ConfigFile(agent));
            new Thread(delegate ()
            {
                Dictionary<string, object> usage = null;
                try { usage = agent == "codex" ? CodexLog.Usage(Path.Combine(home, "sessions")) : CopilotLog.Usage(Path.Combine(home, "session-state")); }
                catch (Exception ex) { Log.Write(agent + " quota: " + ex.Message); }
                Post(() =>
                {
                    logReading.Remove(agent);
                    if (usage != null) OnUsage(new HookEvent { Agent = "usage", Name = "Usage", Data = usage });
                });
            }) { IsBackground = true, Name = agent + " quota" }.Start();
        }

        // Follows the Claude Code connection
        void QuotaFollowHooks(bool connected)
        {
            try
            {
                if (!connected) StatusSetup.Remove();
                else if (settings.GetBool("agent.quota", true))
                {
                    string err = StatusSetup.Install();
                    if (err != null) Log.Write("statusline setup: " + err);
                }
            }
            catch (Exception ex) { Log.Write("statusline setup: " + ex.Message); }
        }
    }
}
