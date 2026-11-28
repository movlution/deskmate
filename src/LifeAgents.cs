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
        // Agents

        void StartAgents()
        {
            string token = settings.Get("agent.token", "");
            if (token.Length < 24)
            {
                var bytes = new byte[24];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                settings.Set("agent.token", token);
                // New installs: a port nobody can guess
                if (!settings.Has("agent.port")) settings.Set("agent.port", AgentHub.FreePort());
                settings.Save();
            }
            StartHub();
            if (hub != null && hub.Error == null)
            {
                try { HookSetup.Refresh(hub.Port, settings.Get("agent.token", "")); }
                catch (Exception ex) { Log.Write("hook refresh: " + ex.Message); }
            }
            if (settings.GetBool("phone", false)) StartPhone();
        }

        internal const int PhoneDetailMax = 1500;

        // Phone only shows up to PhoneDetailMax chars; never allow unseen commands or diffs remotely
        internal static bool PhoneTruncated(Approval a)
        {
            if (a == null) return true;
            if (a.Truncated) return true;
            if ((a.Detail ?? "").Length > PhoneDetailMax) return true;
            if (a.Diff != null && a.Diff.Count > ApproveScrollMax) return true;
            return false;
        }

        internal static bool CanPhoneDecide(Approval a, string decision)
        {
            if (a == null) return false;
            if (decision == "allow" && PhoneTruncated(a)) return false;
            return decision == "allow" || decision == "deny";
        }

        internal static Dictionary<string, object> PhoneApprovalItem(Approval a)
        {
            string raw = a.Detail ?? "";
            bool truncated = PhoneTruncated(a);
            string detail = raw.Length > PhoneDetailMax ? raw.Substring(0, PhoneDetailMax) + "…" : raw;
            return new Dictionary<string, object> {
                { "id", a.Id }, { "agent", a.Agent }, { "project", a.Project }, { "tool", a.Tool }, { "key", Fmt.OneLine(a.Key, 300) },
                { "note", a.Note }, { "detail", detail },
                { "flags", new List<object>(a.Flags) }, { "truncated", truncated }, { "risk", a.Risk ?? "" } };
        }

        PhoneLink phone;

        // LAN listener for the Android app; off by default
        void StartPhone()
        {
            if (phone != null) return;
            phone = new PhoneLink();
            phone.Decided += (id, d) => Post(() =>
            {
                var a = approvals.Find(x => x.Id == id);
                // Truncated requests are never allowed remotely
                if (CanPhoneDecide(a, d)) Decide(a, d);
            });
            phone.Paired += () => Post(() => { ShowInfo(IcoPhone, Lang.T("HP tersambung"), phone.Device, true); PublishPhone(); });
            WirePhone();
            phone.Start(settings.GetInt("phone.port", 0));
            if (phone.Port > 0 && settings.GetInt("phone.port", 0) != phone.Port) { settings.Set("phone.port", phone.Port); settings.Save(); }
            PublishPhone();
            if (Array.IndexOf(tabs, PagePhone) < 0) SetLayout("panel.tabs", TabIds(tabs) + ",phone");
        }

        void StopPhone()
        {
            if (phone == null) return;
            phone.Stop();
            phone = null;
            if (Array.IndexOf(tabs, PagePhone) >= 0 && tabs.Length > 1)
            {
                var list = new List<int>(tabs);
                list.Remove(PagePhone);
                SetLayout("panel.tabs", TabIds(list));
            }
        }

        // Approvals and sessions for the phone, JSON
        void PublishPhone()
        {
            if (phone == null) return;
            var list = new List<object>();
            foreach (var a in approvals) list.Add(PhoneApprovalItem(a));
            var ss = new List<object>();
            foreach (var s in sessions.Values)
                if (s.State != AgentState.Ended)
                    ss.Add(new Dictionary<string, object> { { "project", s.Project }, { "state", s.State.ToString().ToLowerInvariant() }, { "activity", Fmt.OneLine(s.Activity, 160) } });
            phone.Publish(Json.Encode(new Dictionary<string, object> { { "approvals", list }, { "sessions", ss } }));
        }

        void StartHub()
        {
            hub = new AgentHub(settings.GetInt("agent.port", 47821), settings.Get("agent.token", ""), AgentHub.ClampWait(settings.GetInt("agent.wait", AgentHub.DefaultWaitSeconds)));
            hub.Received += ev => Post(() => OnHook(ev));
            hub.Expired += a => Post(() => DropApproval(a));
            hub.Start();
        }

        internal void SetPermissionWait(int seconds)
        {
            int wait = AgentHub.ClampWait(seconds);
            settings.Set("agent.wait", wait);
            settings.SaveDeferred();
            if (hub != null) hub.WaitSeconds = wait;
            try { AgentSetup.EnsureReplyTimeout("codex", wait); }
            catch (Exception ex) { Log.Write("codex wait sync: " + ex.Message); }
        }

        bool hooksOn;
        int hookProfiles;

        bool HooksInstalled()
        {
            try
            {
                hooksOn = HookSetup.Installed(hub.Port);
                hookProfiles = HookSetup.Profiles().Count;
            }
            catch (Exception ex) { Log.Write("hook check: " + ex.Message); hooksOn = false; }
            return hooksOn;
        }

        // Applies to new Claude Code sessions
        void ToggleHooks()
        {
            try
            {
                if (HooksInstalled())
                {
                    HookSetup.Remove(hub.Port);
                    QuotaFollowHooks(false);
                    HooksInstalled();
                    ShowInfo(Ink.IcoTerminal, Lang.T("Claude Code dilepas"), Lang.T("hook Deskmate dihapus"), true);
                    CheckLinks();
                    return;
                }
                string err = HookSetup.Install(hub.Port, settings.Get("agent.token", ""));
                if (err == null) QuotaFollowHooks(true);
                HooksInstalled();
                if (err != null) ShowInfo(Ink.IcoTerminal, Lang.T("Belum terhubung"), err, true);
                else ShowInfo(Ink.IcoTerminal, Lang.T("Claude Code terhubung"), hookProfiles + Lang.T(" profil · berlaku di sesi baru"), true);
            }
            catch (Exception ex)
            {
                Log.Write("hook setup: " + ex);
                ShowInfo(Ink.IcoTerminal, Lang.T("Belum terhubung"), Fmt.OneLine(ex.Message, 60), true);
            }
            CheckLinks();
        }

        string linked = "";

        // Agent names with hooks in place
        void CheckLinks()
        {
            var names = new List<string>();
            if (HooksInstalled()) names.Add("Claude Code");
            foreach (var id in AgentSetup.Ids)
            {
                try { if (AgentSetup.Installed(id)) names.Add(AgentSetup.Label(id)); }
                catch (Exception ex) { Log.Write("agent check: " + ex.Message); }
            }
            linked = string.Join(", ", names);
        }

        // Applies to that agent's new sessions
        void ToggleAgent(string id)
        {
            string name = AgentSetup.Label(id);
            try
            {
                if (AgentSetup.Installed(id))
                {
                    AgentSetup.Remove(id);
                    ShowInfo(Ink.IcoTerminal, name + Lang.T(" dilepas"), Lang.T("hook Deskmate dihapus"), true);
                }
                else
                {
                    string err = AgentSetup.Install(id);
                    if (err != null) ShowInfo(Ink.IcoTerminal, Lang.T("Belum terhubung"), err, true);
                    else ShowInfo(Ink.IcoTerminal, Lang.F("{0} terhubung", name), id == "codex" ? Lang.T("percayai lewat /hooks di Codex") : Lang.T("pantau saja · berlaku di sesi baru"), true);
                }
            }
            catch (Exception ex)
            {
                Log.Write("agent setup: " + ex);
                ShowInfo(Ink.IcoTerminal, Lang.T("Belum terhubung"), Fmt.OneLine(ex.Message, 60), true);
            }
            CheckLinks();
        }

        AgentSession SessionFor(HookEvent ev)
        {
            AgentSession s;
            if (!sessions.TryGetValue(ev.SessionId, out s))
            {
                s = new AgentSession { Id = ev.SessionId, StartedUtc = DateTime.UtcNow, State = AgentState.Ready };
                sessions[ev.SessionId] = s;
            }
            string cwd = Json.Str(ev.Data, "cwd");
            if (cwd.Length > 0)
            {
                s.Cwd = cwd;
                string name = AgentHub.LastPart(cwd);
                s.Project = name.Length > 0 ? name : cwd;
            }
            string profile = AgentHub.ProfileOf(Json.Str(ev.Data, "transcript_path"));
            if (profile.Length > 0 || s.Profile.Length == 0) s.Profile = profile;
            s.LastUtc = DateTime.UtcNow;
            return s;
        }

        // Failed event must not strand agent
        void OnHook(HookEvent ev)
        {
            try { OnHookCore(ev); PublishPhone(); }
            catch (Exception ex)
            {
                Log.Write("hook event " + ev.Name + ": " + ex);
                if (ev.Approval != null)
                {
                    ev.Approval.Answer(null);
                    approvals.Remove(ev.Approval);
                }
                Kick();
            }
        }

        void OnHookCore(HookEvent ev)
        {
            if (ev.Agent == "plugin") { if (Json.Str(ev.Data, "hook_event_name") == "Run") OnRun(ev); else OnPlugin(ev); return; }
            if (ev.Agent == "usage") { OnUsage(ev); return; }
            if ((ev.Agent != "claude" && !AgentAdapters.Normalize(ev)) || ev.SessionId.Length == 0)
            {
                if (ev.Approval != null) ev.Approval.Answer(null);
                return;
            }
            var s = SessionFor(ev);
            if (ev.Agent != "claude") s.Profile = AgentAdapters.Label(ev.Agent);
            if (ev.Window != IntPtr.Zero) s.Window = ev.Window;
            if (ev.Pid > 0 && (s.Pid == 0 || ev.Window != IntPtr.Zero)) s.Pid = ev.Pid;
            var input = Json.Obj(ev.Data, "tool_input");
            string tool = Json.Str(ev.Data, "tool_name");
            switch (ev.Name)
            {
                case "SessionStart":
                    s.State = AgentState.Ready;
                    s.Activity = Lang.T("Siap");
                    break;
                case "UserPromptSubmit":
                    SettleApprovals(s.Id, null, null);
                    s.State = AgentState.Working;
                    s.TurnStartedUtc = DateTime.UtcNow;
                    s.Tool = "";
                    s.Activity = Lang.T("Berpikir");
                    s.Failed = false;
                    s.TurnFiles.Clear();
                    s.TurnCommands = s.TurnFailed = 0;
                    break;
                case "PreToolUse":
                    s.State = AgentState.Working;
                    s.Tool = tool;
                    s.Activity = AgentHub.Describe(tool, input);
                    // Codex has no PostToolUse hook here
                    if (ev.Agent == "codex") { Tally(s, tool, input); TakePlan(s, tool, input); }
                    break;
                case "PostToolUse":
                case "PostToolUseFailure":
                    if (ev.Name == "PostToolUse") { Tally(s, tool, input); TakePlan(s, tool, input); }
                    else if (tool == "Bash" || tool == "PowerShell") s.TurnFailed++;
                    SettleApprovals(s.Id, tool, AgentHub.Describe(tool, input));
                    if (s.State == AgentState.Waiting)
                    {
                        s.State = AgentState.Working;
                        s.Activity = AgentHub.Describe(tool, input);
                    }
                    break;
                case "PermissionRequest":
                    var a = ev.Approval;
                    a.Agent = AgentAdapters.Label(ev.Agent);
                    a.Project = s.Project;
                    a.Profile = s.Profile;
                    a.Tool = tool;
                    FillApproval(a, tool, input);
                    a.Key = AgentHub.Describe(tool, input);
                    if (!a.Truncated && a.Risk == null && s.Trusted.Contains(a.Trust))
                    {
                        a.Answer("allow");
                        s.State = AgentState.Working;
                        s.Tool = tool;
                        s.Activity = a.Key;
                        break;
                    }
                    approvals.Add(a);
                    SoundCue(CueAsk);
                    s.State = AgentState.Waiting;
                    s.Tool = tool;
                    s.Activity = AgentHub.Describe(tool, input);
                    expanded = false;
                    topGap.Velocity += 90;
                    break;
                case "Notification":
                    if (Json.Str(ev.Data, "notification_type") == "permission_prompt" && !HasApproval(s.Id))
                    {
                        s.State = AgentState.Waiting;
                        s.Activity = Lang.T("Menunggu izin di terminal");
                    }
                    break;
                case "Stop":
                case "StopFailure":
                    SettleApprovals(s.Id, null, null);
                    s.State = AgentState.Done;
                    if (ev.Agent == "codex" || ev.Agent == "copilot") LogQuota(ev.Agent);
                    s.Failed = ev.Name == "StopFailure";
                    s.LastMessage = Fmt.OneLine(Json.Str(ev.Data, "last_assistant_message"), 600);
                    SoundCue(s.Failed ? CueFail : CueDone);
                    var t = ShowToast(View.Done, 6, 2, false);
                    if (t != null) t.Data = s;
                    break;
                case "SessionEnd":
                    SettleApprovals(s.Id, null, null);
                    s.State = AgentState.Ended;
                    break;
            }
            Kick();
        }
    }
}
