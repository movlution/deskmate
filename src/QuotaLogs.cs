using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Codex: newest session log with rate limits
    static class CodexLog
    {
        const int TailBytes = 512 * 1024, MaxFiles = 6;

        public static Dictionary<string, object> Usage(string root)
        {
            if (!Directory.Exists(root)) return null;
            var files = new List<FileInfo>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int d = 0; d < 8 && files.Count < 12; d++)
            {
                var day = DateTime.Now.Date.AddDays(-d);
                string dir = Path.Combine(root, day.ToString("yyyy", ci), day.ToString("MM", ci), day.ToString("dd", ci));
                if (Directory.Exists(dir)) files.AddRange(new DirectoryInfo(dir).GetFiles("rollout-*.jsonl"));
            }
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = 0; i < files.Count && i < MaxFiles; i++)
            {
                var limits = LastLimits(files[i].FullName);
                if (limits != null) return ToUsage(limits);
            }
            return null;
        }

        // Agents keep the file open: share it
        internal static string Tail(string path, int bytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, fs.Length - bytes);
                fs.Seek(start, SeekOrigin.Begin);
                var buf = new byte[fs.Length - start];
                int n = 0, r;
                while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                return Encoding.UTF8.GetString(buf, 0, n);
            }
        }

        static Dictionary<string, object> LastLimits(string path)
        {
            var lines = Tail(path, TailBytes).Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].IndexOf("\"rate_limits\":{", StringComparison.Ordinal) < 0) continue;
                Dictionary<string, object> o;
                try { o = Json.Parse(lines[i].Trim()) as Dictionary<string, object>; }
                catch (Exception) { continue; }
                var limits = Json.Obj(Json.Obj(o, "payload"), "rate_limits");
                if (limits != null) return limits;
            }
            return null;
        }

        static Dictionary<string, object> ToUsage(Dictionary<string, object> limits)
        {
            var o = new Dictionary<string, object> { { "hook_event_name", "Usage" }, { "agent", "codex" }, { "model", Json.Str(limits, "plan_type") } };
            int found = 0;
            foreach (var key in new[] { "primary", "secondary" })
            {
                var src = Json.Obj(limits, key);
                double pct = Json.Num(src, "used_percent", double.NaN), at = Json.Num(src, "resets_at", double.NaN);
                if (double.IsNaN(pct) || double.IsNaN(at)) continue;
                o["w" + found++] = new Dictionary<string, object> { { "used_percentage", pct }, { "resets_at", at }, { "minutes", Json.Num(src, "window_minutes", 0) } };
            }
            return found > 0 ? o : null;
        }
    }

    // Copilot: quotaSnapshots in its newest session events
    static class CopilotLog
    {
        const int TailBytes = 4 * 1024 * 1024, MaxFiles = 6;
        const string Key = "\"quotaSnapshots\":{";

        public static Dictionary<string, object> Usage(string root)
        {
            if (!Directory.Exists(root)) return null;
            var files = new List<FileInfo>();
            foreach (var d in new DirectoryInfo(root).GetDirectories())
            {
                var f = new FileInfo(Path.Combine(d.FullName, "events.jsonl"));
                if (f.Exists) files.Add(f);
            }
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = 0; i < files.Count && i < MaxFiles; i++)
            {
                var usage = ToUsage(LastSnapshot(files[i].FullName));
                if (usage != null) return usage;
            }
            return null;
        }

        // Lines carry whole prompts: cut the object out
        static Dictionary<string, object> LastSnapshot(string path)
        {
            string text = CodexLog.Tail(path, TailBytes);
            int at = text.LastIndexOf(Key, StringComparison.Ordinal);
            while (at >= 0)
            {
                int start = at + Key.Length - 1, end = ObjectEnd(text, start);
                if (end > start)
                {
                    try
                    {
                        var o = Json.Parse(text.Substring(start, end - start + 1)) as Dictionary<string, object>;
                        if (o != null) return o;
                    }
                    catch (Exception) { }
                }
                at = at > 0 ? text.LastIndexOf(Key, at - 1, StringComparison.Ordinal) : -1;
            }
            return null;
        }

        // Matching brace, strings skipped
        static int ObjectEnd(string s, int start)
        {
            int depth = 0;
            bool str = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (str) { if (c == '\\') i++; else if (c == '"') str = false; continue; }
                if (c == '"') str = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            return -1;
        }

        static Dictionary<string, object> ToUsage(Dictionary<string, object> snap)
        {
            var src = Json.Obj(snap, "premium_interactions");
            double total = Json.Num(src, "entitlementRequests", 0), left = Json.Num(src, "remainingPercentage", double.NaN);
            if (src == null || true.Equals(HookSetup.Get(src, "isUnlimitedEntitlement")) || total <= 0 || double.IsNaN(left)) return null;
            DateTime reset;
            if (!DateTime.TryParse(Json.Str(src, "resetDate"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out reset)) return null;
            var w = new Dictionary<string, object>
            {
                { "kind", "premium" },
                { "used_percentage", 100 - left },
                { "resets_at", (reset - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds },
            };
            return new Dictionary<string, object> { { "hook_event_name", "Usage" }, { "agent", "copilot" }, { "model", "" }, { "w0", w } };
        }
    }

}
