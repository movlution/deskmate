using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Deskmate
{
    public static class AgentHooksCheck
    {
        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        static string SendHook(int port, string token, string jsonBody)
        {
            byte[] body = Encoding.UTF8.GetBytes(jsonBody);
            using (var c = new TcpClient())
            {
                c.Connect(IPAddress.Loopback, port);
                c.SendTimeout = c.ReceiveTimeout = 5000;
                var st = c.GetStream();
                var head = Encoding.ASCII.GetBytes(
                    "POST /hook HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Deskmate-Token: " + token +
                    "\r\nContent-Type: application/json\r\nContent-Length: " + body.Length +
                    "\r\nConnection: close\r\n\r\n");
                st.Write(head, 0, head.Length);
                st.Write(body, 0, body.Length);
                var ms = new MemoryStream();
                var buf = new byte[4096];
                int n;
                while ((n = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        static int CountHandlers(Dictionary<string, object> hooks, string ev)
        {
            int count = 0;
            foreach (var gr in HookSetup.AsList(HookSetup.Get(hooks, ev)))
                foreach (var h in HookSetup.AsList(HookSetup.Get(gr, "hooks")))
                    count++;
            return count;
        }

        static double PermissionTimeout(Dictionary<string, object> hooks, string key, string match)
        {
            foreach (var gr in HookSetup.AsList(HookSetup.Get(hooks, "PermissionRequest")))
                foreach (var h in HookSetup.AsList(HookSetup.Get(gr, "hooks")))
                    if (Json.Str(h, key).Contains(match))
                        return Json.Num(h, "timeout", -1);
            return -1;
        }

        public static string Run()
        {
            string prevHome = Environment.GetEnvironmentVariable("DESKMATE_HOME");
            string fakeHome = Path.Combine(Path.GetTempPath(), "Deskmate-agent-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(fakeHome);
                Environment.SetEnvironmentVariable("DESKMATE_HOME", fakeHome);

                // --- 1. Claude Code port rotation & zombie hook cleanup ---
                string claudeDir = Path.Combine(fakeHome, ".claude");
                Directory.CreateDirectory(claudeDir);
                string claudeFile = Path.Combine(claudeDir, "settings.json");
                File.WriteAllText(claudeFile,
                    "{\n" +
                    "  \"hooks\": {\n" +
                    "    \"PreToolUse\": [\n" +
                    "      { \"hooks\": [ { \"type\": \"http\", \"url\": \"http://127.0.0.1:9999/hook\" } ] }\n" +
                    "    ],\n" +
                    "    \"Stop\": [\n" +
                    "      { \"hooks\": [ { \"type\": \"command\", \"command\": \"echo keep-user-hook\" } ] }\n" +
                    "    ]\n" +
                    "  }\n" +
                    "}\n", new UTF8Encoding(false));

                string token = "0123456789abcdef0123456789abcdef0123456789abcdef";
                Assert(HookSetup.Install(47821, token) == null, "Initial Claude hook install failed");
                Assert(HookSetup.Installed(47821), "Expected Claude hooks installed on port 47821");

                // Move to a random port without calling Remove first (mirrors MoveToRandomPort)
                Assert(HookSetup.Install(51234, token) == null, "Port-rotated Claude hook install failed");
                Assert(HookSetup.Installed(51234), "Expected Claude hooks installed on new port 51234");
                Assert(!HookSetup.Installed(47821), "Old port 47821 should no longer report installed");

                string rawAfterRotate = File.ReadAllText(claudeFile, Encoding.UTF8);
                Assert(!rawAfterRotate.Contains("47821"), "Old port 47821 zombie hook remained in settings.json");
                Assert(rawAfterRotate.Contains("http://127.0.0.1:9999/hook"), "Third-party HTTP hook must be preserved");
                Assert(rawAfterRotate.Contains("echo keep-user-hook"), "Third-party Stop hook must be preserved");

                var root = HookSetup.Load(claudeFile);
                var hooks = Json.Obj(root, "hooks");
                Assert(CountHandlers(hooks, "PreToolUse") == 2, "Expected 1 user + 1 Deskmate PreToolUse hook, got " + CountHandlers(hooks, "PreToolUse"));
                Assert(CountHandlers(hooks, "Stop") == 2, "Expected 1 user + 1 Deskmate Stop hook, got " + CountHandlers(hooks, "Stop"));
                Assert(CountHandlers(hooks, "PermissionRequest") == 1, "Expected exactly 1 PermissionRequest hook");
                Assert(PermissionTimeout(hooks, "url", "51234") == AgentHub.HookReplyTimeoutSeconds,
                    "Claude PermissionRequest timeout should be " + AgentHub.HookReplyTimeoutSeconds);

                // Legacy file with BOTH stale port 47821 zombie AND old 130s timeout on port 51234
                File.WriteAllText(claudeFile,
                    rawAfterRotate.Replace("\"timeout\": " + AgentHub.HookReplyTimeoutSeconds, "\"timeout\": 130")
                        .Replace("\"PermissionRequest\": [",
                            "\"PermissionRequest\": [\n" +
                            "      { \"hooks\": [ { \"type\": \"http\", \"url\": \"http://127.0.0.1:47821/hook\", \"headers\": { \"X-Deskmate-Token\": \"" + token + "\" }, \"timeout\": 130 } ] },"),
                    new UTF8Encoding(false));
                HookSetup.Refresh(51234, token);
                string rawAfterRefresh = File.ReadAllText(claudeFile, Encoding.UTF8);
                Assert(!rawAfterRefresh.Contains("47821"), "Refresh should purge legacy zombie hooks from old ports");
                var refreshedHooks = Json.Obj(HookSetup.Load(claudeFile), "hooks");
                Assert(PermissionTimeout(refreshedHooks, "url", "51234") == AgentHub.HookReplyTimeoutSeconds,
                    "Refresh should upgrade old 130s Claude timeout to " + AgentHub.HookReplyTimeoutSeconds);

                // Remove with a different port still strips stale Deskmate hooks while keeping user hooks
                HookSetup.Remove(59999);
                string rawAfterRemove = File.ReadAllText(claudeFile, Encoding.UTF8);
                Assert(!rawAfterRemove.Contains("51234"), "Remove should strip Deskmate hooks even on port mismatch");
                Assert(rawAfterRemove.Contains("http://127.0.0.1:9999/hook"), "Remove must preserve third-party HTTP hook");
                Assert(rawAfterRemove.Contains("echo keep-user-hook"), "Remove must preserve third-party command hook");

                // --- 2. Live Hold-Permissions Timeout (AgentHub + Relay + Codex) ---
                Assert(AgentHub.ClampWait(10) == 30, "ClampWait lower bound failed");
                Assert(AgentHub.ClampWait(110) == 110, "ClampWait default failed");
                Assert(AgentHub.ClampWait(300) == 300, "ClampWait upper bound failed");
                Assert(AgentHub.ClampWait(999) == 300, "ClampWait upper clamp failed");

                var cfg = new Settings();
                cfg.Set("agent.wait", 30);
                Assert(Relay.ReplyTimeoutMs(cfg) == 45000, "Relay timeout at 30s should be 45000ms");
                cfg.Set("agent.wait", 110);
                Assert(Relay.ReplyTimeoutMs(cfg) == 125000, "Relay timeout at 110s should be 125000ms");
                cfg.Set("agent.wait", 240);
                Assert(Relay.ReplyTimeoutMs(cfg) == 255000, "Relay timeout at 240s should be 255000ms");
                cfg.Set("agent.wait", 300);
                Assert(Relay.ReplyTimeoutMs(cfg) == 315000, "Relay timeout at 300s should be 315000ms");
                Assert(Relay.ReplyTimeoutMs(cfg) < AgentHub.HookReplyTimeoutSeconds * 1000,
                    "Relay max timeout must stay below agent hook timeout");

                int port = AgentHub.FreePort();
                var hub = new AgentHub(port, token, 30);
                double seenWait = 0;
                string nextDecision = "allow";
                hub.Received += ev =>
                {
                    if (ev.Approval != null)
                    {
                        seenWait = (ev.Approval.UntilUtc - ev.Approval.CreatedUtc).TotalSeconds;
                        ev.Approval.Answer(nextDecision);
                    }
                };
                hub.Start();
                try
                {
                    Assert(hub.Error == null, "Test AgentHub failed to bind: " + hub.Error);
                    string req = "{\"hook_event_name\":\"PermissionRequest\",\"session_id\":\"s1\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"echo hi\"}}";
                    string resp1 = SendHook(port, token, req);
                    Assert(resp1.Contains("\"allow\""), "First live permission request should return allow");
                    Assert(seenWait >= 28 && seenWait <= 32, "Expected ~30s wait before live update, got " + seenWait);

                    // Update wait live without restarting AgentHub
                    hub.WaitSeconds = 240;
                    Assert(hub.WaitSeconds == 240, "WaitSeconds getter did not reflect live update");
                    nextDecision = "deny";
                    string resp2 = SendHook(port, token, req);
                    Assert(resp2.Contains("\"deny\""), "Second live permission request should return deny");
                    Assert(seenWait >= 238 && seenWait <= 242, "Expected ~240s wait after live update, got " + seenWait);
                }
                finally
                {
                    hub.Stop();
                }

                // Codex hook timeout installation & legacy upgrade
                string codexDir = Path.Combine(fakeHome, ".codex");
                Directory.CreateDirectory(codexDir);
                Assert(AgentSetup.Install("codex") == null, "Codex install failed");
                string codexFile = AgentSetup.ConfigFile("codex");
                var codexHooks = Json.Obj(HookSetup.Load(codexFile), "hooks");
                Assert(PermissionTimeout(codexHooks, "command", "--relay codex") == AgentHub.HookReplyTimeoutSeconds,
                    "Codex PermissionRequest timeout should be " + AgentHub.HookReplyTimeoutSeconds);

                // Downgrade to legacy 130s and ensure EnsureReplyTimeout upgrades when agent.wait > 110
                string codexRaw = File.ReadAllText(codexFile, Encoding.UTF8);
                File.WriteAllText(codexFile, codexRaw.Replace("\"timeout\": " + AgentHub.HookReplyTimeoutSeconds, "\"timeout\": 130"), new UTF8Encoding(false));
                AgentSetup.EnsureReplyTimeout("codex", 110);
                Assert(PermissionTimeout(Json.Obj(HookSetup.Load(codexFile), "hooks"), "command", "--relay codex") == 130,
                    "Codex timeout at default 110s should not rewrite unnecessarily");
                AgentSetup.EnsureReplyTimeout("codex", 200);
                Assert(PermissionTimeout(Json.Obj(HookSetup.Load(codexFile), "hooks"), "command", "--relay codex") == AgentHub.HookReplyTimeoutSeconds,
                    "Codex timeout should upgrade to " + AgentHub.HookReplyTimeoutSeconds + " when agent.wait is 200s");

                // --- 3. Phone Remote Approval Truncation Security ---
                var shortCmd = new Approval { Tool = "Bash", Detail = "git status", Truncated = false };
                Assert(!Island.PhoneTruncated(shortCmd), "Short Bash command should not be phone-truncated");
                Assert(Island.CanPhoneDecide(shortCmd, "allow"), "Short Bash command should be remotely allow-able");
                Assert(Island.CanPhoneDecide(shortCmd, "deny"), "Short Bash command should be remotely deny-able");
                Assert(false.Equals(Island.PhoneApprovalItem(shortCmd)["truncated"]), "Short payload truncated flag should be false");

                var midCmd = new Approval { Tool = "Bash", Detail = new string('x', 2500), Truncated = false };
                Assert(Island.PhoneTruncated(midCmd), "2500-char Bash command exceeds 1500-char phone slice and must be phone-truncated");
                Assert(!Island.CanPhoneDecide(midCmd, "allow"), "Phone must NEVER allow a command sliced at 1500 chars");
                Assert(Island.CanPhoneDecide(midCmd, "deny"), "Phone must still allow denying a truncated command");
                var midItem = Island.PhoneApprovalItem(midCmd);
                Assert(true.Equals(midItem["truncated"]), "Phone payload must report truncated=true for 2500-char command");
                Assert(((string)midItem["detail"]).Length == Island.PhoneDetailMax + 1, "Phone detail should be sliced to 1500 + ellipsis");

                var longDiff = new Approval
                {
                    Tool = "Write",
                    Detail = "app.cs\n" + new string('y', 4000),
                    Diff = new List<DiffLine> { new DiffLine { Kind = 3, Text = "app.cs" }, new DiffLine { Kind = 1, Text = new string('y', 4000) } },
                    Truncated = false
                };
                Assert(Island.PhoneTruncated(longDiff), "Long diff (>1500 chars) must be phone-truncated even though desktop Truncated is false");
                Assert(!Island.CanPhoneDecide(longDiff, "allow"), "Phone must NEVER allow a long diff that was sliced");
                Assert(Island.CanPhoneDecide(longDiff, "deny"), "Phone must still allow denying a long diff");

                var manyLineDiff = new Approval { Tool = "Write", Detail = "short.txt", Diff = new List<DiffLine>(), Truncated = false };
                for (int i = 0; i < 310; i++) manyLineDiff.Diff.Add(new DiffLine { Kind = 1, Text = "a" });
                Assert(Island.PhoneTruncated(manyLineDiff), "Diff exceeding 300 lines must be phone-truncated");
                Assert(!Island.CanPhoneDecide(manyLineDiff, "allow"), "Phone must NEVER allow a diff exceeding desktop 300-line limit");

                var desktopTrunc = new Approval { Tool = "Bash", Detail = new string('z', 100), Truncated = true };
                Assert(Island.PhoneTruncated(desktopTrunc), "Desktop-truncated approval must stay phone-truncated");
                Assert(!Island.CanPhoneDecide(desktopTrunc, "allow"), "Phone must NEVER allow a desktop-truncated approval");

                // --- 4. TerminalFinder ephemeral helper detection ---
                Assert(TerminalFinder.IsEphemeralHelper("Deskmate.exe"), "Deskmate.exe should be treated as ephemeral relay helper");
                Assert(TerminalFinder.IsEphemeralHelper("curl.exe"), "curl.exe should be treated as ephemeral hook helper");
                Assert(TerminalFinder.IsEphemeralHelper("cmd.exe"), "cmd.exe should be treated as ephemeral wrapper");
                Assert(!TerminalFinder.IsEphemeralHelper("codex.exe"), "codex.exe must NOT be ephemeral");
                Assert(!TerminalFinder.IsEphemeralHelper("node.exe"), "node.exe must NOT be ephemeral");

                // --- 5. Antigravity & Codex adapters, PlanReader, and AgentRisk ---
                var agyEdit = new HookEvent
                {
                    Agent = "antigravity",
                    Name = "PostToolUse",
                    Data = new Dictionary<string, object>
                    {
                        { "conversationId", "conv-1" },
                        { "toolCall", new Dictionary<string, object>
                            {
                                { "name", "edit_file" },
                                { "args", new Dictionary<string, object> { { "Path", "d:\\proj\\src\\App.cs" } } }
                            }
                        }
                    }
                };
                Assert(AgentAdapters.Normalize(agyEdit), "Antigravity edit_file event should normalize");
                Assert(Json.Str(agyEdit.Data, "tool_name") == "Edit", "Antigravity edit_file should map to Edit");
                Assert(AgentHub.Describe("Edit", Json.Obj(agyEdit.Data, "tool_input")) == "App.cs", "Antigravity Edit should describe App.cs");

                var agySteps = PlanReader.Read("update_steps", new Dictionary<string, object>
                {
                    { "steps", new object[]
                        {
                            new Dictionary<string, object> { { "title", "Step 1" }, { "status", "done" } },
                            new Dictionary<string, object> { { "title", "Step 2" }, { "status", "in_progress" } },
                            new Dictionary<string, object> { { "title", "Step 3" }, { "status", "pending" } }
                        }
                    }
                });
                Assert(agySteps != null && agySteps.Count == 3 && agySteps[0].Status == 2 && agySteps[1].Status == 1 && agySteps[2].Status == 0,
                    "PlanReader should parse Antigravity update_steps");

                var safePatch = new Dictionary<string, object>
                {
                    { "input", "*** Begin Patch\n*** Update File: src/App.cs\n@@\n-a\n+b\n*** Add File: src/New.cs\n+x\n*** End Patch" }
                };
                var patchFiles = DiffBuilder.PatchFiles(safePatch);
                Assert(patchFiles.Count == 2 && patchFiles[0] == "src/App.cs" && patchFiles[1] == "src/New.cs", "PatchFiles should extract both files");
                Assert(AgentHub.Describe("apply_patch", safePatch) == "App.cs (+1)", "apply_patch Describe should summarize patched files");
                Assert(AgentHub.ToolLabel("apply_patch") == "Patch", "apply_patch ToolLabel should be Patch");
                Assert(AgentRisk.Of("apply_patch", safePatch) == null, "Safe apply_patch should not be flagged risky");

                var secretPatch = new Dictionary<string, object>
                {
                    { "input", "*** Begin Patch\n*** Update File: .env\n@@\n-A=1\n+A=2\n*** End Patch" }
                };
                Assert(AgentRisk.Of("apply_patch", secretPatch) != null, "apply_patch touching .env must be flagged risky");
                Assert(AgentRisk.Of("PowerShell", new Dictionary<string, object> { { "command", "powershell -ec SQBFAFgA" } }) != null,
                    "PowerShell -ec alias must be flagged risky");
                Assert(AgentRisk.Of("NotebookEdit", new Dictionary<string, object> { { "notebook_path", "d:\\proj\\.env" } }) != null,
                    "NotebookEdit touching .env must be flagged risky");

                // --- 6. LiveQuota must not erase statusline/log quota rows ---
                var hookQuota = new QuotaInfo { Profile = "", FromLive = false, AtUtc = DateTime.UtcNow };
                hookQuota.W[0].Has = true;
                hookQuota.W[0].Pct = 42;
                hookQuota.W[0].ResetUtc = DateTime.UtcNow.AddHours(2);
                Assert(!Island.ShouldDropOnLiveGone(hookQuota), "LiveQuota Gone must NOT delete valid statusline/log quota row");

                var liveQuota = new QuotaInfo { Profile = "", FromLive = true, AtUtc = DateTime.UtcNow };
                liveQuota.W[0].Has = true;
                Assert(Island.ShouldDropOnLiveGone(liveQuota), "LiveQuota Gone should still remove live-sourced row");

                // --- 7. AgentPrompt CleanPrompt & surrogate-safe chunking ---
                Assert(Island.CleanPrompt("baris 1\r\nbaris 2\n\tbaris 3") == "baris 1 baris 2 baris 3",
                    "CleanPrompt should collapse CRLF/tabs into single spaces");
                string emojiText = "ab\uD83D\uDE00cd";
                Assert(Island.NextPromptChunk(emojiText, 0, 3) == 2,
                    "NextPromptChunk must not split a UTF-16 surrogate pair");
                Assert(Island.NextPromptChunk(emojiText, 2, 3) == 3,
                    "NextPromptChunk should include whole surrogate pair plus following char");

                return "agent hooks, live wait timeout, phone security, adapters, risk, quota, and prompt checks passed";
            }
            finally
            {
                Environment.SetEnvironmentVariable("DESKMATE_HOME", prevHome);
                try { if (Directory.Exists(fakeHome)) Directory.Delete(fakeHome, true); }
                catch (Exception) { }
            }
        }
    }
}
