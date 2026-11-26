using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // One provider's answer: usage, connected only, or signed out
    class LiveQuotaResult
    {
        public string Agent = "", Profile = "", Note = "";
        public Dictionary<string, object> Usage;
        public bool Gone;
    }

    // Signed-in AI tools asked directly; tokens never logged
    static class LiveQuota
    {
        const int TimeoutMs = 6000, LocalTimeoutMs = 3000;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Per provider: rate limits wait longer
        static readonly Dictionary<string, DateTime> hold = new Dictionary<string, DateTime>();

        public static List<LiveQuotaResult> Poll(bool claude, bool agents)
        {
            var list = new List<LiveQuotaResult>();
            if (claude) Safe(list, "claude", delegate { Claude(list); });
            if (agents)
            {
                Safe(list, "codex", delegate { list.Add(Codex()); });
                Safe(list, "antigravity", delegate { list.Add(Antigravity()); });
            }
            return list;
        }

        static void Safe(List<LiveQuotaResult> list, string who, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Write(who + " live quota: " + ex.GetType().Name); }
        }

        static bool Held(string who)
        {
            DateTime until;
            return hold.TryGetValue(who, out until) && DateTime.UtcNow < until;
        }

        // 429 or 5xx: back off; other failures retry next round
        static void Failed(string who, int status)
        {
            Log.Write(who + " live quota: http " + status);
            if (status == 429 || status >= 500) hold[who] = DateTime.UtcNow.AddMinutes(20);
        }

        // Claude and Codex rewrite these: read shared
        static Dictionary<string, object> ReadJson(string path)
        {
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                return Json.Parse(sr.ReadToEnd()) as Dictionary<string, object>;
        }

        static string Title(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length > 24) s = s.Substring(0, 24);
            return s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        static string Connected(string plan)
        {
            plan = Title(plan);
            return plan.Length > 0 ? Lang.T("Terhubung") + " · " + plan : Lang.T("Terhubung");
        }

        static double Unix(DateTime utc) { return (utc - Epoch).TotalSeconds; }

        // Claude Code: OAuth usage, one row per profile
        static void Claude(List<LiveQuotaResult> list)
        {
            var profiles = HookSetup.Profiles();
            // No Claude Code at all: every Claude row goes
            if (profiles.Count == 0) list.Add(new LiveQuotaResult { Profile = "*", Gone = true });
            foreach (var dir in profiles)
            {
                var r = new LiveQuotaResult { Profile = StatusLine.ProfileLabel(StatusLine.SafeName(Path.GetFileName(dir))) };
                list.Add(r);
                string file = Path.Combine(dir, ".credentials.json");
                if (!File.Exists(file)) { r.Gone = true; continue; }
                Dictionary<string, object> oauth;
                try { oauth = Json.Obj(ReadJson(file), "claudeAiOauth"); }
                catch (Exception) { r.Note = Connected(""); continue; }
                string token = Json.Str(oauth, "accessToken");
                if (token.Length == 0) { r.Gone = true; continue; }
                string plan = Json.Str(oauth, "subscriptionType");
                r.Note = Connected(plan);
                // Expired: Claude Code refreshes it, not us
                double exp = Json.Num(oauth, "expiresAt", 0);
                if (exp > 0 && Epoch.AddMilliseconds(exp) <= DateTime.UtcNow) continue;
                string key = "claude:" + r.Profile;
                if (Held(key)) continue;
                var res = Web.Send("GET", "https://api.anthropic.com/api/oauth/usage", new Dictionary<string, string>
                {
                    { "Authorization", "Bearer " + token },
                    { "anthropic-beta", "oauth-2025-04-20" },
                }, null, TimeoutMs);
                token = null;
                if (!res.Ok) { Failed(key, res.Status); continue; }
                var data = Json.Parse(res.Body) as Dictionary<string, object>;
                var o = new Dictionary<string, object> { { "hook_event_name", "Usage" }, { "profile", r.Profile }, { "model", Title(plan) } };
                int found = 0;
                foreach (var k in new[] { "five_hour", "seven_day" })
                {
                    var src = Json.Obj(data, k);
                    double pct = Json.Num(src, "utilization", double.NaN);
                    var at = Json.Time(src, "resets_at");
                    if (double.IsNaN(pct) || at == DateTime.MinValue) continue;
                    o[k] = new Dictionary<string, object> { { "used_percentage", pct }, { "resets_at", Unix(at) } };
                    found++;
                }
                if (found > 0) r.Usage = o;
            }
        }

        // Codex CLI: ChatGPT sign-in usage
        static LiveQuotaResult Codex()
        {
            var r = new LiveQuotaResult { Agent = "codex", Profile = AgentAdapters.Label("codex") };
            string home = Path.GetDirectoryName(AgentSetup.ConfigFile("codex"));
            var auth = ReadJson(Path.Combine(home, "auth.json"));
            if (auth == null) { r.Gone = true; return r; }
            var tokens = Json.Obj(auth, "tokens");
            string token = Json.Str(tokens, "access_token"), account = Json.Str(tokens, "account_id");
            if (token.Length == 0)
            {
                // API key only: billed per call, no plan window
                if (Json.Str(auth, "OPENAI_API_KEY").Length > 0) r.Note = Lang.T("Terhubung") + " · " + Lang.T("kunci API");
                else r.Gone = true;
                return r;
            }
            r.Note = Connected("");
            if (Held("codex")) return r;
            var headers = new Dictionary<string, string> { { "Authorization", "Bearer " + token } };
            if (account.Length > 0) headers["ChatGPT-Account-Id"] = account;
            var res = Web.Send("GET", "https://chatgpt.com/backend-api/wham/usage", headers, null, TimeoutMs);
            headers = null;
            token = null;
            if (!res.Ok) { Failed("codex", res.Status); return r; }
            var data = Json.Parse(res.Body) as Dictionary<string, object>;
            string plan = Json.Str(data, "plan_type");
            r.Note = Connected(plan);
            var limits = Json.Obj(data, "rate_limit");
            var o = new Dictionary<string, object> { { "hook_event_name", "Usage" }, { "agent", "codex" }, { "model", plan } };
            int found = 0;
            foreach (var k in new[] { "primary_window", "secondary_window" })
            {
                var src = Json.Obj(limits, k);
                double pct = Json.Num(src, "used_percent", double.NaN), at = Json.Num(src, "reset_at", double.NaN);
                if (double.IsNaN(at))
                {
                    double after = Json.Num(src, "reset_after_seconds", double.NaN);
                    if (!double.IsNaN(after)) at = Unix(DateTime.UtcNow) + after;
                }
                if (double.IsNaN(pct) || double.IsNaN(at)) continue;
                o["w" + found++] = new Dictionary<string, object> { { "used_percentage", pct }, { "resets_at", at }, { "minutes", Json.Num(src, "limit_window_seconds", 0) / 60 } };
            }
            if (found > 0) r.Usage = o;
            return r;
        }

        static string AgyRoot { get { return Path.Combine(Settings.Home, ".gemini"); } }

        // Antigravity: its running language server knows the quota
        static LiveQuotaResult Antigravity()
        {
            var r = new LiveQuotaResult { Agent = "antigravity", Profile = AgentAdapters.Label("antigravity") };
            string daemon = Path.Combine(Path.Combine(AgyRoot, "antigravity"), "daemon");
            var hubs = new List<FileInfo>();
            if (Directory.Exists(daemon)) hubs.AddRange(new DirectoryInfo(daemon).GetFiles("hub_*.json"));
            bool cli = File.Exists(Path.Combine(Path.Combine(AgyRoot, "antigravity-cli"), "settings.json"));
            if (hubs.Count == 0 && !cli) { r.Gone = true; return r; }
            r.Note = Connected("");
            hubs.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            int tried = 0;
            foreach (var f in hubs)
            {
                if (tried >= 3) break;
                Dictionary<string, object> hub;
                try { hub = ReadJson(f.FullName); }
                catch (Exception) { continue; }
                int pid = (int)Json.Num(hub, "pid", 0), port = (int)Json.Num(hub, "httpPort", 0);
                string csrf = Json.Str(hub, "csrfToken");
                if (pid <= 0 || port <= 0 || port > 65535 || csrf.Length == 0 || !Alive(pid)) continue;
                tried++;
                var res = Web.Send("POST", "http://127.0.0.1:" + port + "/exa.language_server_pb.LanguageServerService/GetUserStatus", new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "X-Codeium-Csrf-Token", csrf },
                    { "Connect-Protocol-Version", "1" },
                }, "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"locale\":\"en\"}}", LocalTimeoutMs);
                if (!res.Ok) continue;
                AgyStatus(r, Json.Obj(Json.Parse(res.Body), "userStatus"));
                return r;
            }
            return r;
        }

        static bool Alive(int pid)
        {
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; }
            catch (Exception) { return false; }
        }

        // Lowest remaining per group: Gemini, then other models
        static void AgyStatus(LiveQuotaResult r, Dictionary<string, object> status)
        {
            if (status == null) return;
            string plan = Json.Str(Json.Obj(Json.Obj(status, "planStatus"), "planInfo"), "planName");
            r.Note = Connected(plan);
            double[] left = { double.NaN, double.NaN };
            DateTime[] reset = { DateTime.MinValue, DateTime.MinValue };
            foreach (var m in Json.Arr(Json.Obj(status, "cascadeModelConfigData"), "clientModelConfigs"))
            {
                var info = Json.Obj(m, "quotaInfo");
                double f = Json.Num(info, "remainingFraction", double.NaN);
                var at = Json.Time(info, "resetTime");
                if (info == null || at == DateTime.MinValue) continue;
                // Proto drops zero: present quota, no fraction, is empty
                if (double.IsNaN(f)) f = 0;
                string id = Json.Str(m, "modelId"), label = Json.Str(m, "label");
                int g = id.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (double.IsNaN(left[g]) || f < left[g] || (f == left[g] && at < reset[g])) { left[g] = f; reset[g] = at; }
            }
            var o = new Dictionary<string, object> { { "hook_event_name", "Usage" }, { "agent", "antigravity" }, { "model", plan } };
            string[] kinds = { "gemini", "other" };
            int found = 0;
            for (int g = 0; g < 2; g++)
            {
                if (double.IsNaN(left[g])) continue;
                double used = (1 - Math.Max(0, Math.Min(1, left[g]))) * 100;
                o["w" + found++] = new Dictionary<string, object> { { "kind", kinds[g] }, { "used_percentage", used }, { "resets_at", Unix(reset[g]) } };
            }
            if (found > 0) r.Usage = o;
        }
    }

    // Polls every few minutes, off the UI thread
    partial class Island
    {
        const int LiveQuotaMinutes = 5;
        bool liveQuotaBusy;
        // First round after boot settles
        DateTime liveQuotaNext = DateTime.UtcNow.AddSeconds(45);

        void LiveQuotaTick(DateTime nowUtc)
        {
            if (nowUtc >= liveQuotaNext) LiveQuotaPoll();
        }

        void LiveQuotaPoll()
        {
            liveQuotaNext = DateTime.UtcNow.AddMinutes(LiveQuotaMinutes);
            if (liveQuotaBusy || !QuotaOn) return;
            liveQuotaBusy = true;
            bool claude = settings.GetBool("agent.quota", true), agents = settings.GetBool(StatusSetup.AgentsKey, true);
            new Thread(delegate ()
            {
                List<LiveQuotaResult> list = null;
                try { list = LiveQuota.Poll(claude, agents); }
                catch (Exception ex) { Log.Write("live quota: " + ex.GetType().Name); }
                Post(() =>
                {
                    liveQuotaBusy = false;
                    if (list != null) OnLiveQuota(list);
                });
            }) { IsBackground = true, Name = "live quota" }.Start();
        }

        void OnLiveQuota(List<LiveQuotaResult> list)
        {
            if (IsDisposed || Disposing) return;
            int before = QuotaSig();
            foreach (var r in list)
            {
                if (!settings.GetBool(r.Agent.Length > 0 ? StatusSetup.AgentsKey : "agent.quota", true)) continue;
                string profile = r.Agent.Length > 0 ? r.Profile : StatusLine.SafeName(CleanText(r.Profile, 24));
                if (r.Agent.Length == 0 && r.Profile.Length == 0) profile = "";
                // Signed out: the row goes
                if (r.Gone) { quotas.RemoveAll(q => q.Agent == r.Agent && (r.Profile == "*" || q.Profile == profile)); continue; }
                if (r.Usage != null) OnUsage(new HookEvent { Agent = "usage", Name = "Usage", Data = r.Usage });
                var row = QuotaRow(r.Agent, profile);
                row.Note = CleanText(r.Note, 40);
                if (row.AtUtc == DateTime.MinValue) row.AtUtc = DateTime.UtcNow;
            }
            quotaVersion++;
            if (QuotaSig() != before || expanded) Kick();
        }
    }
}
