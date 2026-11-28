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
    // Type a prompt into an agent's terminal
    partial class Island
    {
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        const int PromptMax = 4000, PromptChunkChars = 64;

        bool SharesWindow(AgentSession s)
        {
            foreach (var other in sessions.Values)
                if (other != s && other.Window == s.Window && other.State != AgentState.Ended) return true;
            return false;
        }

        // One line, no hidden or control chars; collapses \r\n cleanly to a single space
        internal static string CleanPrompt(string text)
        {
            var clean = new StringBuilder((text ?? "").Length);
            bool lastSpace = false;
            foreach (char ch in Fmt.StripHidden(text))
            {
                if (char.IsControl(ch))
                {
                    if (!lastSpace) { clean.Append(' '); lastSpace = true; }
                    continue;
                }
                clean.Append(ch);
                lastSpace = ch == ' ';
            }
            string s = clean.ToString().Trim();
            return s.Length > PromptMax ? s.Substring(0, PromptMax) : s;
        }

        // Next chunk length without splitting a UTF-16 surrogate pair
        internal static int NextPromptChunk(string text, int offset, int maxChars)
        {
            int len = Math.Min(maxChars, text.Length - offset);
            if (len > 1 && offset + len < text.Length && char.IsHighSurrogate(text[offset + len - 1])) len--;
            return len;
        }

        static string AgentKind(AgentSession s)
        {
            return s.Profile == "Codex" || s.Profile == "Antigravity" || s.Profile == "Copilot" ? s.Profile : "Claude";
        }

        // Agent mark, Claude keeps its profile
        static string AgentTag(AgentSession s)
        {
            string kind = AgentKind(s);
            return kind == "Claude" && s.Profile.Length > 0 ? kind + " · " + s.Profile : kind;
        }

        // Claude: its profile's quota; others: the agent's
        QuotaInfo QuotaFor(AgentSession s)
        {
            bool claude = AgentKind(s) == "Claude";
            foreach (var q in quotas) if ((claude ? q.Agent.Length == 0 : q.Agent.Length > 0) && q.Profile == s.Profile && QuotaShown(q)) return q;
            return null;
        }

        void AskAgent(AgentSession s)
        {
            expanded = false;
            Kick();
            string text;
            using (var f = new AgentPromptBox(this, s))
            {
                if (f.ShowDialog() != DialogResult.OK) return;
                text = CleanPrompt(f.Prompt);
            }
            if (text.Length == 0) return;
            if (!TerminalFinder.Focus(s.Window))
            {
                s.Window = TerminalFinder.ForPid(s.Pid);
                if (!TerminalFinder.Focus(s.Window)) { ShowInfo(Ink.IcoTerminal, Lang.T("Terminal tidak bisa dibuka"), s.Project, true); return; }
            }
            IntPtr want = GetAncestor(s.Window, 3);
            string project = s.Project;
            // Waiting happens off the UI thread
            ThreadPool.QueueUserWorkItem(delegate
            {
                string fail = TypeInto(want, text);
                if (fail != null) Post(() => ShowInfo(Ink.IcoTerminal, fail, project, true));
            });
        }

        // Worker: null when typed, else why not
        static string TypeInto(IntPtr want, string text)
        {
            bool front = false;
            for (int i = 0; i < 20 && !front; i++)
            {
                Thread.Sleep(40);
                front = GetAncestor(GetForegroundWindow(), 3) == want;
            }
            if (!front) return Lang.T("Terminal tidak bisa dibuka");
            // Held Shift/Ctrl/Alt/Enter would change the keystrokes
            for (int i = 0; i < 40 && AnyHeld(0x0D, 0x10, 0x11, 0x12, 0x5B, 0x5C); i++) Thread.Sleep(20);
            if (AnyHeld(0x10, 0x11, 0x12, 0x5B, 0x5C)) return Lang.T("Shift, Ctrl, atau Alt masih ditekan");
            // Focus can move during the wait
            if (GetAncestor(GetForegroundWindow(), 3) != want) return Lang.T("Terminal kehilangan fokus");

            int structSize = Marshal.SizeOf(typeof(INPUT));
            for (int pos = 0; pos < text.Length; )
            {
                if (GetAncestor(GetForegroundWindow(), 3) != want) return Lang.T("Terminal kehilangan fokus");
                int take = NextPromptChunk(text, pos, PromptChunkChars);
                var chunk = new INPUT[take * 2];
                for (int i = 0; i < take; i++)
                {
                    chunk[i * 2] = Key(0, text[pos + i], 4);
                    chunk[i * 2 + 1] = Key(0, text[pos + i], 4 | 2);
                }
                uint sent = SendInput((uint)chunk.Length, chunk, structSize);
                if (sent != chunk.Length)
                {
                    Log.Write("send prompt chunk: " + sent + "/" + chunk.Length + " err " + Marshal.GetLastWin32Error());
                    return Lang.T("Gagal mengetik ke terminal");
                }
                pos += take;
                if (pos < text.Length) Thread.Sleep(8);
            }
            // Brief settle so TUI input buffers absorb the text before Enter submits
            Thread.Sleep(35);
            if (GetAncestor(GetForegroundWindow(), 3) != want) return Lang.T("Terminal kehilangan fokus");
            var enter = new[] { Key(0x0D, '\0', 0), Key(0x0D, '\0', 2) };
            uint sentEnter = SendInput((uint)enter.Length, enter, structSize);
            if (sentEnter == enter.Length) return null;
            Log.Write("send prompt enter: " + sentEnter + "/2 err " + Marshal.GetLastWin32Error());
            return Lang.T("Gagal mengetik ke terminal");
        }

        static bool AnyHeld(params int[] keys)
        {
            foreach (int k in keys) if ((GetAsyncKeyState(k) & 0x8000) != 0) return true;
            return false;
        }

        static INPUT Key(ushort vk, char ch, uint flags)
        {
            return new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, wScan = ch, dwFlags = flags } };
        }

    }
}
