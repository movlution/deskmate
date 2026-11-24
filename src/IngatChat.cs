using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace Deskmate
{
    // Chats and mail: which app is a conversation, whose conversation it is, and what the user is writing
    static class IngatChat
    {
        static readonly string[] ChatExes =
        {
            "whatsapp", "whatsapp.root", "telegram", "ayugram", "discord", "slack", "teams", "ms-teams", "signal", "line", "messenger",
            "skype", "viber", "element", "zoom", "wechat", "kakaotalk",
        };
        static readonly string[] MailExes = { "outlook", "olk", "thunderbird", "mailspring", "hxoutlook", "em client", "mailbird" };

        static readonly string[] ChatHosts =
        {
            "web.whatsapp.com", "web.telegram.org", "discord.com", "app.slack.com", "teams.microsoft.com", "teams.live.com", "www.messenger.com",
            "messenger.com", "chat.google.com", "www.instagram.com", "www.linkedin.com", "x.com", "twitter.com", "web.facebook.com", "www.facebook.com",
        };
        static readonly string[] MailHosts = { "mail.google.com", "outlook.live.com", "outlook.office.com", "outlook.office365.com", "mail.yahoo.com", "mail.proton.me" };

        // "chat", "mail", or "" for everything else
        public static string Kind(string exe, string url, string title)
        {
            string e = (exe ?? "").ToLowerInvariant();
            if (Array.IndexOf(ChatExes, e) >= 0) return "chat";
            if (Array.IndexOf(MailExes, e) >= 0) return "mail";
            string host = IngatPrivacy.Host(url);
            if (host.Length > 0)
            {
                if (Array.IndexOf(MailHosts, host) >= 0) return "mail";
                if (Array.IndexOf(ChatHosts, host) >= 0)
                {
                    // Only the messaging parts of social sites
                    if ((host.Contains("instagram") && !url.Contains("/direct")) || (host.Contains("linkedin") && !url.Contains("/messaging")) ||
                        ((host == "x.com" || host == "twitter.com") && !url.Contains("/messages")) || (host.Contains("facebook") && !url.Contains("/messages")))
                        return "";
                    return "chat";
                }
            }
            // Browser tab titles name the web app when the URL could not be read
            string t = (title ?? "").ToLowerInvariant();
            if (IngatText.IsBrowser(e))
            {
                if (t.Contains("whatsapp") || t.Contains("telegram web") || t.EndsWith("discord") || t.Contains("| slack") || t.Contains("messenger")) return "chat";
                if (t.Contains("gmail") || t.Contains("outlook")) return "mail";
            }
            return "";
        }

        static readonly string[] AppWords =
        {
            "whatsapp", "telegram", "discord", "slack", "microsoft teams", "teams", "messenger", "signal", "line", "skype", "viber", "element", "zoom",
            "gmail", "outlook", "mail", "thunderbird", "google chrome", "microsoft edge", "mozilla firefox", "brave", "opera", "vivaldi", "arc",
            "chat", "chats", "obrolan", "inbox", "kotak masuk", "home", "beranda", "direct", "messages", "pesan", "message (html)", "message (plain text)",
        };

        // Who the open conversation is with, from the window title; "" when the title does not say (WhatsApp desktop)
        public static string Conversation(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            var parts = title.Split(new[] { " - ", " | ", " – ", " — ", " • " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                string p = Regex.Replace(raw.Trim(), @"^\(\d+\+?\)\s*", "");
                p = p.Trim().TrimStart('@', '#', '●', '•', '*').Trim();
                if (p.Length < 2 || p.Length > 60) continue;
                if (p.Contains("@") && p.Contains(".")) continue;
                if (Array.IndexOf(AppWords, p.ToLowerInvariant()) >= 0) continue;
                if (Regex.IsMatch(p, @"^\d+\s*(unread|belum dibaca|new|baru)", RegexOptions.IgnoreCase)) continue;
                return p;
            }
            return "";
        }

        // The field with keyboard focus inside this window: what the user is typing right now.
        // Read from the field itself through UI Automation; keystrokes are never watched.
        public static string Draft(IntPtr hwnd, out bool secret)
        {
            string field;
            return Draft(hwnd, out secret, out field);
        }

        // field: the element's identity, so another tab or chat in the same window is not "the same box, now empty"
        public static string Draft(IntPtr hwnd, out bool secret, out string field)
        {
            secret = false;
            field = "";
            AutomationElement f;
            try { f = AutomationElement.FocusedElement; }
            catch (Exception) { return null; }
            if (f == null) return null;
            try
            {
                var c = f.Current;
                if (c.IsPassword) { secret = true; return null; }
                int pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (c.ProcessId != pid) return null;
                var type = c.ControlType;
                bool editable = type == ControlType.Edit || type == ControlType.Document || c.IsKeyboardFocusable && (type == ControlType.Group || type == ControlType.Custom || type == ControlType.Pane);
                if (!editable) return null;
                try { field = string.Join(".", Array.ConvertAll(f.GetRuntimeId(), i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))); } catch (Exception) { field = ""; }
                object p;
                string v = null;
                if (f.TryGetCurrentPattern(ValuePattern.Pattern, out p)) { var vp = (ValuePattern)p; if (vp.Current.IsReadOnly) return null; v = vp.Current.Value; }
                else if (f.TryGetCurrentPattern(TextPattern.Pattern, out p)) v = ((TextPattern)p).DocumentRange.GetText(4000);
                if (v == null) return null;
                v = v.Trim();
                // Search boxes and address bars are not messages
                string name = (c.Name ?? "").ToLowerInvariant();
                if (name.Contains("search") || name.Contains("cari") || name.Contains("address") || name.Contains("alamat")) return null;
                // A whole document (Notepad, Word) is not a message being written; the screen text already has it
                if (type == ControlType.Document && v.Length > 1500) return null;
                return v.Length > 4000 ? v.Substring(0, 4000) : v;
            }
            catch (ElementNotAvailableException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (COMException) { return null; }
        }

        // Names compare without case, spaces, emoji, or titles
        public static string Norm(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name.ToLowerInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            string n = sb.ToString();
            foreach (var t in new[] { "pak", "bu", "bapak", "ibu", "mas", "mbak", "kak", "bang", "dr", "mr", "mrs", "ms" })
                if (n.StartsWith(t) && n.Length > t.Length + 2 && name.ToLowerInvariant().StartsWith(t + " ")) { n = n.Substring(t.Length); break; }
            return n;
        }
    }
}
