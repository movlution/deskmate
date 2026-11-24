using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Deskmate
{
    // Codex and Claude Code already signed in on this laptop: Deskmate runs the CLI and never holds a token
    static class AiCli
    {
        const int AskTimeout = 180000;
        static readonly Regex SafeModel = new Regex(@"^[A-Za-z0-9._:/\[\]-]{1,80}$");

        // Where each CLI lives, looked up again after a short while
        static readonly Dictionary<string, KeyValuePair<DateTime, string>> found = new Dictionary<string, KeyValuePair<DateTime, string>>();

        public static string Exe(AiProvider p)
        {
            if (p == null || p.Cli == null) return null;
            lock (found)
            {
                KeyValuePair<DateTime, string> hit;
                if (found.TryGetValue(p.Cli, out hit) && DateTime.UtcNow < hit.Key) return hit.Value;
            }
            string exe = Locate(p.Cli);
            lock (found) found[p.Cli] = new KeyValuePair<DateTime, string>(DateTime.UtcNow.AddSeconds(30), exe);
            return exe;
        }

        static string Locate(string name)
        {
            var dirs = new List<string>();
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) if (d.Trim().Length > 0) dirs.Add(d.Trim().Trim('"'));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            // Deskmate may start at login with a PATH older than the install
            dirs.Add(Path.Combine(home, ".local", "bin"));
            dirs.Add(Path.Combine(roaming, "npm"));
            foreach (var dir in dirs)
            {
                try
                {
                    string exe = Path.Combine(dir, name + ".exe");
                    if (File.Exists(exe)) return exe;
                    string cmd = Path.Combine(dir, name + ".cmd");
                    if (!File.Exists(cmd)) continue;
                    // npm shims start node through cmd: prefer the native build they wrap
                    if (name == "codex")
                        foreach (var arch in new[] { "x64", "arm64" })
                        {
                            string triple = arch == "x64" ? "x86_64-pc-windows-msvc" : "aarch64-pc-windows-msvc";
                            string native = Path.Combine(dir, "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-" + arch, "vendor", triple, "bin", "codex.exe");
                            if (File.Exists(native)) return native;
                        }
                    return cmd;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        public static void Forget(AiProvider p) { if (p != null && p.Cli != null) lock (found) found.Remove(p.Cli); }

        static string LoginHint(AiProvider p) { return p.Cli == "codex" ? "codex login" : "claude"; }

        // Null when the CLI is there and signed in
        public static string Check(AiProvider p)
        {
            string exe = Exe(p);
            if (exe == null) return Lang.F("{0} belum terpasang di laptop ini", Ai.Label(p));
            var r = Run(exe, p.Cli == "codex" ? "login status" : "auth status", null, 30000);
            if (r.TimedOut) return Lang.F("{0} terlalu lama menjawab", Ai.Label(p));
            bool ok = p.Cli == "codex" ? r.Code == 0 && (r.Out + r.Err).IndexOf("Logged in", StringComparison.OrdinalIgnoreCase) >= 0
                                       : r.Code == 0 && Regex.IsMatch(r.Out, "\"loggedIn\"\\s*:\\s*true");
            return ok ? null : Lang.F("{0} belum login; jalankan \"{1}\" sekali di terminal", Ai.Label(p), LoginHint(p));
        }

        // Tools are not passed: the CLI answers in plain text
        public static Ai.Reply Ask(AiProvider p, string model, string system, List<KeyValuePair<string, string>> turns, out string error)
        {
            error = null;
            string exe = Exe(p);
            if (exe == null) { error = Lang.F("{0} belum terpasang di laptop ini", Ai.Label(p)); return null; }
            if (model.Length > 0 && !SafeModel.IsMatch(model)) { error = Lang.F("Model {0} belum dipilih", Ai.Label(p)); return null; }

            // An empty folder so the agent has no project of the user's to read
            string work = Path.Combine(Path.GetTempPath(), "Deskmate-cli");
            string tag = Guid.NewGuid().ToString("N");
            string sysFile = Path.Combine(work, tag + ".system.txt");
            string outFile = Path.Combine(work, tag + ".reply.txt");
            try
            {
                Directory.CreateDirectory(work);
                string args, input;
                if (p.Cli == "codex")
                {
                    input = "Follow these instructions. Do not run commands or edit files; answer in text only.\n\n" + system + "\n\n" + Transcript(turns);
                    args = "exec --skip-git-repo-check --ephemeral --sandbox read-only --color never --ignore-rules -c model_reasoning_effort=low" +
                           " -C " + Quote(work) + " -o " + Quote(outFile) + (model.Length > 0 ? " -m " + model : "") + " -";
                }
                else
                {
                    File.WriteAllText(sysFile, system ?? "", new UTF8Encoding(false));
                    input = Transcript(turns);
                    // Project settings only (none in the empty folder): a user's settings.json can route
                    // Claude Code to a proxy or rename models, and the point here is the claude.ai login
                    args = "-p --output-format text --tools \"\" --no-session-persistence --safe-mode --strict-mcp-config --setting-sources project" +
                           " --system-prompt-file " + Quote(sysFile) + (model.Length > 0 ? " --model " + model : "");
                }
                var r = Run(exe, args, input, AskTimeout, work);
                if (r.TimedOut) { error = Lang.F("{0} terlalu lama menjawab", Ai.Label(p)); return null; }
                string text = p.Cli == "codex" && File.Exists(outFile) ? File.ReadAllText(outFile, Encoding.UTF8) : p.Cli == "codex" ? "" : r.Out;
                text = (text ?? "").Trim();
                if (r.Code != 0 || text.Length == 0)
                {
                    Log.Write("ai " + p.Id + " exit " + r.Code);
                    error = Lang.F("{0} gagal: {1}", Ai.Label(p), Short(r.Err.Length > 0 ? r.Err : r.Out));
                    return null;
                }
                return new Ai.Reply { Text = text };
            }
            catch (Exception ex)
            {
                Log.Write("ai " + p.Id + " " + ex.GetType().Name);
                error = Lang.F("{0} gagal: {1}", Ai.Label(p), ex.GetType().Name);
                return null;
            }
            finally
            {
                try { File.Delete(sysFile); } catch (Exception) { }
                try { File.Delete(outFile); } catch (Exception) { }
            }
        }

        // Claude Code lists no models: its aliases (always the newest) and the current ids
        static readonly string[] ClaudeAliases = { "sonnet", "opus", "haiku", "fable" };
        static readonly string[] ClaudeIds = { "claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5", "claude-fable-5-2" };

        public static List<KeyValuePair<string, string>> ListModels(AiProvider p, out string error)
        {
            error = null;
            var list = new List<KeyValuePair<string, string>>();
            if (p.Cli == "claude")
            {
                foreach (var a in ClaudeAliases) list.Add(new KeyValuePair<string, string>(a, Lang.T("alias, selalu versi terbaru")));
                foreach (var id in ClaudeIds) list.Add(new KeyValuePair<string, string>(id, ""));
                return list;
            }
            string exe = Exe(p);
            if (exe == null) { error = Lang.F("{0} belum terpasang di laptop ini", Ai.Label(p)); return null; }
            var r = Run(exe, "debug models", null, 30000);
            if (r.TimedOut) { error = Lang.F("{0} terlalu lama menjawab", Ai.Label(p)); return null; }
            try
            {
                foreach (var o in Json.Arr(Json.Parse(r.Out), "models"))
                {
                    var m = o as Dictionary<string, object>;
                    string slug = Json.Str(m, "slug");
                    // Hidden entries are internal or retired
                    if (slug.Length == 0 || Json.Str(m, "visibility") != "list") continue;
                    string name = Json.Str(m, "display_name");
                    list.Add(new KeyValuePair<string, string>(slug, name.Equals(slug, StringComparison.OrdinalIgnoreCase) ? "" : name));
                }
            }
            catch (Exception) { list.Clear(); }
            if (list.Count == 0) { error = Lang.F("{0} gagal: {1}", Ai.Label(p), Short(r.Err.Length > 0 ? r.Err : Lang.T("daftar model kosong"))); return null; }
            return list;
        }

        // One prompt from the turns: the CLI keeps no history of its own here
        internal static string Transcript(List<KeyValuePair<string, string>> turns)
        {
            if (turns.Count == 1) return turns[0].Value ?? "";
            var b = new StringBuilder("Conversation so far, oldest first:\n\n");
            foreach (var t in turns) b.Append(t.Key == "assistant" ? "Assistant: " : "User: ").Append(t.Value).Append("\n\n");
            b.Append("Reply as the Assistant to the last User message. Reply with the message only.");
            return b.ToString();
        }

        static string Short(string s)
        {
            string last = "";
            foreach (var line in (s ?? "").Split('\n')) if (line.Trim().Length > 0) last = line.Trim();
            if (last.Length == 0) return Lang.T("tanpa keterangan");
            return last.Length > 140 ? last.Substring(0, 140) + "…" : last;
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "") + "\""; }

        class Result { public int Code = -1; public string Out = "", Err = ""; public bool TimedOut; }

        static Result Run(string exe, string args, string input, int timeoutMs, string cwd = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = cwd ?? Path.GetTempPath(),
            };
            // No API key, endpoint or model map from the environment: the CLI's own login answers.
            // Also what a parent Claude Code session injects, so the child does not nest into it
            var drop = new List<string>();
            foreach (string k in psi.EnvironmentVariables.Keys)
                if (k.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith("CLAUDE_AGENT_SDK", StringComparison.OrdinalIgnoreCase) || k.Equals("CLAUDECODE", StringComparison.OrdinalIgnoreCase))
                    drop.Add(k);
            foreach (var k in drop) psi.EnvironmentVariables.Remove(k);
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            var res = new Result();
            using (var proc = Process.Start(psi))
            {
                var outBuf = new StringBuilder();
                var errBuf = new StringBuilder();
                var t1 = new Thread(delegate () { outBuf.Append(proc.StandardOutput.ReadToEnd()); }) { IsBackground = true };
                var t2 = new Thread(delegate () { errBuf.Append(proc.StandardError.ReadToEnd()); }) { IsBackground = true };
                t1.Start();
                t2.Start();
                try
                {
                    // UTF-8 without a BOM: .NET 4.8 has no StandardInputEncoding
                    byte[] bytes = new UTF8Encoding(false).GetBytes(input ?? "");
                    proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    proc.StandardInput.Close();
                }
                catch (IOException) { }
                if (!proc.WaitForExit(timeoutMs))
                {
                    res.TimedOut = true;
                    try { proc.Kill(); } catch (Exception) { }
                    return res;
                }
                t1.Join(5000);
                t2.Join(5000);
                res.Code = proc.ExitCode;
                res.Out = outBuf.ToString();
                res.Err = errBuf.ToString();
            }
            return res;
        }
    }
}
