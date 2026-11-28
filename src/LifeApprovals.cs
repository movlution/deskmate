using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Windows.UI.Notifications.Management;

namespace Deskmate
{
    partial class Island
    {
        // Everything that will run or be written
        // Counts behind the session summary
        static void Tally(AgentSession s, string tool, Dictionary<string, object> input)
        {
            if (tool == "Bash" || tool == "PowerShell") { s.Commands++; s.TurnCommands++; }
            else if (tool == "Edit" || tool == "Write" || tool == "MultiEdit" || tool == "NotebookEdit")
            {
                string f = Json.Str(input, "file_path");
                if (f.Length == 0) f = Json.Str(input, "notebook_path");
                if (f.Length > 0) { s.Files.Add(f.ToLowerInvariant()); s.TurnFiles.Add(f.ToLowerInvariant()); }
            }
            else if (tool == "apply_patch")
            {
                foreach (var f in DiffBuilder.PatchFiles(input))
                {
                    s.Files.Add(f.ToLowerInvariant());
                    s.TurnFiles.Add(f.ToLowerInvariant());
                }
            }
        }

        static string Summary(AgentSession s, DateTime now)
        {
            var parts = new List<string>();
            int min = Math.Max(0, (int)(now - s.StartedUtc).TotalMinutes);
            parts.Add(min < 1 ? Lang.T("baru") : min < 60 ? min + Lang.T(" mnt") : (min / 60) + Lang.T(" j ") + (min % 60) + Lang.T(" mnt"));
            if (s.Files.Count > 0) parts.Add(s.Files.Count + Lang.T(" berkas"));
            if (s.Commands > 0) parts.Add(s.Commands + Lang.T(" perintah"));
            return string.Join(" · ", parts);
        }

        // Bring the session's terminal forward
        bool FocusTerminal(AgentSession s)
        {
            if (s == null) return false;
            if (!TerminalFinder.Focus(s.Window))
            {
                s.Window = TerminalFinder.ForPid(s.Pid);
                if (!TerminalFinder.Focus(s.Window)) return false;
            }
            expanded = false;
            if (toast != null && toast.View == View.Done) DismissToast();
            Kick();
            return true;
        }

        // Shown text, flags, and the "Always" key
        static void FillApproval(Approval a, string tool, Dictionary<string, object> input)
        {
            a.Note = Fmt.Visible(Fmt.OneLine(Json.Str(input, "description"), 160));
            if (a.Note.Length == 0) a.Note = ChangeSummary(tool, input);
            a.Trust = Json.Sha256(tool + "\n" + Json.Canonical(input));
            a.Risk = AgentRisk.Of(tool, input);
            a.Flags.Clear();
            if (input != null && (tool == "Bash" || tool == "PowerShell"))
                foreach (var kv in input)
                {
                    if (kv.Key == "command" || kv.Key == "description") continue;
                    string v = Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    if (kv.Key == "run_in_background") { if (v == "True") a.Flags.Add(Lang.T("jalan di latar")); }
                    else if (kv.Key == "dangerouslyDisableSandbox") { if (v == "True") a.Flags.Add(Lang.T("tanpa sandbox")); }
                    else if (kv.Key == "timeout") a.Flags.Add(Lang.F("batas {0}", Duration(TimeSpan.FromMilliseconds(Json.Num(input, "timeout", 0)))));
                    else a.Flags.Add(kv.Key + ": " + Fmt.Visible(Fmt.OneLine(v, 40)));
                }
            a.Diff = DiffBuilder.For(tool, input);
            string d = Detail(tool, input);
            // Long plain text: terminal shows it whole
            a.Truncated = d.Length > 8000 && a.Diff == null;
            a.Detail = Fmt.Visible(a.Truncated ? d.Substring(0, 8000) + "…" : d);
        }

        static string Detail(string tool, Dictionary<string, object> input)
        {
            if (input == null) return "";
            string d = Plain(input, "command");
            if (tool != "Bash" && tool != "PowerShell") d = null;
            string path = Plain(input, "file_path");
            if (d == null && path != null)
            {
                if (tool == "Edit")
                    d = path + (Json.Str(input, "replace_all") == "True" ? Lang.T(" · semua kemunculan") : "") + "\n" +
                        Prefix("- ", Plain(input, "old_string")) + "\n" + Prefix("+ ", Plain(input, "new_string"));
                else if (tool == "MultiEdit")
                {
                    var sb = new System.Text.StringBuilder(path);
                    var edits = Json.Arr(input, "edits");
                    for (int i = 0; i < edits.Length; i++)
                    {
                        var e = edits[i] as Dictionary<string, object>;
                        if (e == null) continue;
                        sb.Append("\n@@ ").Append(i + 1).Append('/').Append(edits.Length).Append(Json.Str(e, "replace_all") == "True" ? Lang.T(" · semua kemunculan") : "");
                        sb.Append('\n').Append(Prefix("- ", Plain(e, "old_string"))).Append('\n').Append(Prefix("+ ", Plain(e, "new_string")));
                    }
                    d = sb.ToString();
                }
                else if (tool == "Write") d = path + "\n" + (Plain(input, "content") ?? "");
                else if (tool == "Read") d = path;
            }
            if (d == null && tool == "NotebookEdit")
                d = Json.Str(input, "notebook_path") + " · " + Json.Str(input, "edit_mode") + " " + Json.Str(input, "cell_id") + "\n" + Json.Str(input, "new_source");
            if (d == null && tool == "WebFetch") d = Plain(input, "url");
            if (d == null) d = Json.Encode(input);
            return d.Replace("\r", "");
        }

        static string Plain(Dictionary<string, object> input, string key)
        {
            object v;
            return input.TryGetValue(key, out v) ? v as string : null;
        }

        static string Prefix(string mark, string text)
        {
            return mark + (text ?? "").Replace("\r", "").Replace("\n", "\n" + mark);
        }

        // Size note beside the tool name
        static string ChangeSummary(string tool, Dictionary<string, object> input)
        {
            if (input == null) return "";
            if (tool == "Edit")
                return Lang.T("ganti ") + LineCount(Json.Str(input, "old_string")) + Lang.T(" baris jadi ") + LineCount(Json.Str(input, "new_string")) + Lang.T(" baris");
            if (tool == "Write") return Lang.T("tulis ") + LineCount(Json.Str(input, "content")) + Lang.T(" baris");
            if (tool == "MultiEdit") return Json.Arr(input, "edits").Length + Lang.T(" perubahan");
            if (tool == "NotebookEdit") return Lang.T("ubah sel notebook");
            return "";
        }

        static int LineCount(string s) { return s.Length == 0 ? 0 : s.Split('\n').Length; }

        bool HasApproval(string sessionId) { return approvals.Exists(a => a.SessionId == sessionId); }

        // Later event: answered in terminal
        void SettleApprovals(string sessionId, string tool, string described)
        {
            for (int i = approvals.Count - 1; i >= 0; i--)
            {
                var a = approvals[i];
                if (a.SessionId != sessionId) continue;
                if (tool != null && (a.Tool != tool || a.Key != described)) continue;
                a.Answer(null);
                approvals.RemoveAt(i);
            }
        }

        // Allow now and every identical request in this session
        void AllowAlways(Approval a)
        {
            if (a.Truncated || a.Risk != null) return;
            AgentSession s;
            if (sessions.TryGetValue(a.SessionId, out s)) s.Trusted.Add(a.Trust);
            Decide(a, "allow");
        }

        void Decide(Approval a, string decision)
        {
            a.Answer(decision);
            approvals.Remove(a);
            PublishPhone();
            AgentSession s;
            if (sessions.TryGetValue(a.SessionId, out s) && !HasApproval(s.Id))
            {
                s.State = decision == null ? AgentState.Waiting : AgentState.Working;
                if (decision == null) s.Activity = Lang.T("Menunggu izin di terminal");
            }
            Kick();
        }

        void DropApproval(Approval a)
        {
            bool expired = a.TimedOut;
            // SettleApprovals/Decide may have removed it first: then it is not "expired"
            bool removed = approvals.Remove(a);
            PublishPhone();
            Kick();
            // The card disappearing silently left the agent waiting in the terminal
            if (expired && removed) ShowInfo(Ink.IcoTerminal, Lang.T("Izin kedaluwarsa"), Lang.T("Jawab di terminal"), true);
        }

        void ReleaseApprovals()
        {
            foreach (var a in approvals) a.Answer(null);
            approvals.Clear();
        }

        AgentSession BusySession()
        {
            AgentSession best = null;
            foreach (var s in sessions.Values)
                if ((s.State == AgentState.Working || s.State == AgentState.Waiting) && (best == null || s.LastUtc > best.LastUtc)) best = s;
            return best;
        }

        int BusyCount()
        {
            int n = 0;
            foreach (var s in sessions.Values) if (s.State == AgentState.Working || s.State == AgentState.Waiting) n++;
            return n;
        }

        List<AgentSession> SortedSessions()
        {
            var list = new List<AgentSession>(sessions.Values);
            list.Sort((a, b) =>
            {
                int ra = Rank(a.State), rb = Rank(b.State);
                return ra != rb ? ra.CompareTo(rb) : b.LastUtc.CompareTo(a.LastUtc);
            });
            return list;
        }

        static int Rank(AgentState s)
        {
            return s == AgentState.Waiting ? 0 : s == AgentState.Working ? 1 : s == AgentState.Done ? 2 : s == AgentState.Ready ? 3 : 4;
        }
    }
}
