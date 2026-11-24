using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Deskmate.exe --statusline < status.json
    static class StatusLine
    {
        const int InputCap = 1024 * 1024, OutputCap = 16 * 1024, PostMs = 150, ChainMs = 5000;
        static readonly string[] Windows = { "five_hour", "seven_day", "spend_limit" };

        public static int Run(string[] args)
        {
            var clock = Stopwatch.StartNew();
            byte[] input = ReadInput();
            Dictionary<string, object> data = null;
            try { data = LiteJson.Parse(Encoding.UTF8.GetString(input)) as Dictionary<string, object>; }
            catch (Exception) { }
            var settings = new Settings();
            Lang.English = settings.Get("lang", "id") == "en";
            bool agy = Json.Str(data, "product") == "antigravity";
            string name = agy ? StatusSetup.AgyName : ConfigName(data);
            var usage = agy ? AgyUsage(data) : Usage(data, name);
            Thread post = null;
            if (usage != null && settings.GetBool(agy ? StatusSetup.AgentsKey : "agent.quota", true) && Relay.Running())
            {
                byte[] body = Encoding.UTF8.GetBytes(LiteJson.Encode(usage));
                post = new Thread(delegate () { Relay.Post("/usage", body, PostMs); }) { IsBackground = true };
                post.Start();
            }
            string text = null;
            // A chained child never chains again
            if (Environment.GetEnvironmentVariable("DESKMATE_STATUSLINE") == null)
            {
                string prev = StatusSetup.Previous(name);
                if (prev != null) text = Chain(prev, input);
            }
            if (string.IsNullOrEmpty(text)) text = agy ? AgyLine(data) : Line(data);
            Write(text);
            if (post != null) post.Join(Math.Max(0, PostMs - (int)clock.ElapsedMilliseconds));
            return 0;
        }

        static byte[] ReadInput()
        {
            if (!Console.IsInputRedirected) return new byte[0];
            var ms = new MemoryStream();
            try
            {
                var input = Console.OpenStandardInput();
                var buf = new byte[16384];
                int n;
                while (ms.Length < InputCap && (n = input.Read(buf, 0, (int)Math.Min(buf.Length, InputCap - ms.Length))) > 0) ms.Write(buf, 0, n);
            }
            catch (Exception) { }
            return ms.ToArray();
        }

        static void Write(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var o = new UTF8Encoding(false).GetBytes(text);
                using (var so = Console.OpenStandardOutput()) so.Write(o, 0, o.Length);
            }
            catch (Exception) { }
        }

        // Profile folder: env first, then transcript
        public static string ConfigName(Dictionary<string, object> data)
        {
            string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrEmpty(dir)) return SafeName(AgentHub.LastPart(dir.Trim()));
            string p = Json.Str(data, "transcript_path").Replace('/', '\\');
            int i = p.IndexOf("\\.claude", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return ".claude";
            int end = p.IndexOf('\\', i + 1);
            return SafeName(end > 0 ? p.Substring(i + 1, end - i - 1) : p.Substring(i + 1));
        }

        public static string SafeName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_') sb.Append(c);
                if (sb.Length >= 40) break;
            }
            return sb.Length > 0 ? sb.ToString() : ".claude";
        }

        public static string ProfileLabel(string name)
        {
            if (name == ".claude") return "";
            return name.StartsWith(".claude-") ? name.Substring(8) : name.TrimStart('.');
        }

        // Only the numbers Deskmate shows
        static Dictionary<string, object> Usage(Dictionary<string, object> data, string name)
        {
            var limits = Json.Obj(data, "rate_limits");
            if (limits == null) return null;
            var o = new Dictionary<string, object>
            {
                { "hook_event_name", "Usage" },
                { "session_id", Ascii(Json.Str(data, "session_id"), 80) },
                { "profile", ProfileLabel(name) },
                { "model", Ascii(Json.Str(Json.Obj(data, "model"), "display_name"), 40) },
            };
            int found = 0;
            foreach (var key in Windows)
            {
                var src = Json.Obj(limits, key);
                double pct = Json.Num(src, "used_percentage", double.NaN), at = Json.Num(src, "resets_at", double.NaN);
                if (double.IsNaN(pct) || double.IsNaN(at)) continue;
                var w = new Dictionary<string, object> { { "used_percentage", pct }, { "resets_at", at } };
                double used = Json.Num(src, "used_usd", double.NaN), limit = Json.Num(src, "limit_usd", double.NaN);
                if (!double.IsNaN(used)) w["used_usd"] = used;
                if (!double.IsNaN(limit)) w["limit_usd"] = limit;
                o[key] = w;
                found++;
            }
            return found > 0 ? o : null;
        }

        // Antigravity: weekly Gemini and other-model quota
        static readonly string[] AgyKeys = { "gemini-weekly", "3p-weekly" }, AgyKinds = { "gemini", "other" };

        static Dictionary<string, object> AgyUsage(Dictionary<string, object> data)
        {
            var quota = Json.Obj(data, "quota");
            if (quota == null) return null;
            var o = new Dictionary<string, object>
            {
                { "hook_event_name", "Usage" },
                { "agent", "antigravity" },
                { "model", Ascii(Json.Str(Json.Obj(data, "model"), "display_name"), 40) },
            };
            int found = 0;
            for (int i = 0; i < AgyKeys.Length; i++)
            {
                var src = Json.Obj(quota, AgyKeys[i]);
                double left = Json.Num(src, "remaining_fraction", double.NaN), at = AgyReset(src);
                if (double.IsNaN(left) || double.IsNaN(at)) continue;
                o["w" + found++] = new Dictionary<string, object> { { "kind", AgyKinds[i] }, { "used_percentage", (1 - left) * 100 }, { "resets_at", at } };
            }
            return found > 0 ? o : null;
        }

        // Unix seconds from reset_time, else reset_in_seconds
        static double AgyReset(Dictionary<string, object> src)
        {
            DateTime t;
            if (DateTime.TryParse(Json.Str(src, "reset_time"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t))
                return (t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            double s = Json.Num(src, "reset_in_seconds", double.NaN);
            return double.IsNaN(s) ? double.NaN : (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds + s;
        }

        static string AgyLine(Dictionary<string, object> data)
        {
            var parts = new List<string>();
            string model = Ascii(Json.Str(Json.Obj(data, "model"), "display_name"), 32);
            if (model.Length > 0) parts.Add(model);
            var quota = Json.Obj(data, "quota");
            string[] labels = { "Gemini", Lang.T("lain") };
            for (int i = 0; i < AgyKeys.Length; i++)
            {
                double left = Json.Num(Json.Obj(quota, AgyKeys[i]), "remaining_fraction", double.NaN);
                if (double.IsNaN(left) || double.IsInfinity(left)) continue;
                parts.Add(labels[i] + " " + (int)Math.Floor(Math.Max(0, Math.Min(100, (1 - left) * 100))) + "%");
            }
            return string.Join(" | ", parts.ToArray());
        }

        // Own line: model and windows, ASCII only
        static string Line(Dictionary<string, object> data)
        {
            var parts = new List<string>();
            string model = Ascii(Json.Str(Json.Obj(data, "model"), "display_name"), 32);
            if (model.Length > 0) parts.Add(model);
            var limits = Json.Obj(data, "rate_limits");
            string[] labels = { Lang.T("5j"), Lang.T("7h"), Lang.T("batas") };
            for (int i = 0; i < Windows.Length; i++)
            {
                double v = Json.Num(Json.Obj(limits, Windows[i]), "used_percentage", double.NaN);
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                parts.Add(labels[i] + " " + (int)Math.Floor(Math.Max(0, Math.Min(100, v))) + "%");
            }
            return string.Join(" | ", parts.ToArray());
        }

        static string Ascii(string s, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (c >= 0x20 && c < 0x7F) sb.Append(c);
                if (sb.Length >= max) break;
            }
            return sb.ToString().Trim();
        }

        // Previous status line, same stdin
        static string Chain(string command, byte[] input)
        {
            try
            {
                var psi = Shell(command);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.EnvironmentVariables["DESKMATE_STATUSLINE"] = "1";
                using (var proc = Process.Start(psi))
                {
                    var read = proc.StandardOutput.ReadToEndAsync();
                    var stdin = proc.StandardInput.BaseStream;
                    // Own thread: a child may never read
                    new Thread(delegate ()
                    {
                        try { stdin.Write(input, 0, input.Length); stdin.Close(); }
                        catch (Exception) { }
                    }) { IsBackground = true }.Start();
                    if (!read.Wait(ChainMs))
                    {
                        try { proc.Kill(); }
                        catch (Exception) { }
                        return null;
                    }
                    string s = read.Result;
                    return s.Length > OutputCap ? s.Substring(0, OutputCap) : s;
                }
            }
            catch (Exception) { return null; }
        }

        // Same shell Claude Code picks
        static ProcessStartInfo Shell(string command)
        {
            string bash = GitBash();
            if (bash != null) return new ProcessStartInfo(bash, "-c " + Arg(command));
            return new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        }

        static string GitBash()
        {
            string env = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    if (dir.Trim().Length == 0 || !File.Exists(Path.Combine(dir.Trim(), "git.exe"))) continue;
                    string b = Path.GetFullPath(Path.Combine(dir.Trim(), "..", "bin", "bash.exe"));
                    if (File.Exists(b)) return b;
                }
                catch (Exception) { }
            }
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("LOCALAPPDATA") + "\\Programs" })
            {
                if (string.IsNullOrEmpty(root)) continue;
                string b = Path.Combine(root, "Git", "bin", "bash.exe");
                if (File.Exists(b)) return b;
            }
            return null;
        }

        // One argv entry, Windows quoting rules
        static string Arg(string s)
        {
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }
    }
}
