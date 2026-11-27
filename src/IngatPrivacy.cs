using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Deskmate
{
    // What Ingatan never keeps, and what it blanks out before keeping
    static class IngatPrivacy
    {
        // Password managers, banking and wallets, sign-in prompts, and Deskmate itself
        static readonly string[] BlockedExes =
        {
            "deskmate", "1password", "bitwarden", "keepass", "keepassxc", "lastpass", "dashlane", "enpass", "nordpass", "roboform", "keeper", "proton pass", "protonpass",
            "credentialuibroker", "consent", "logonui", "lockapp", "securityhealthsystray", "windowsdefender", "secpol", "certmgr",
            "exodus", "ledger live", "electrum", "metamask", "trezor suite",
        };

        // Title words that mean private browsing or a secret on screen
        static readonly string[] PrivateTitles =
        {
            "inprivate", "incognito", "private browsing", "penjelajahan pribadi", "jendela pribadi", "mode penyamaran",
            "password", "kata sandi", "sign in", "masuk ke", "log in", "login", "2fa", "otp", "one-time", "recovery code", "kode pemulihan",
            "internet banking", "klikbca", "livin by mandiri", "brimo", "bni mobile", "mybca", "jenius", "paypal",
        };

        public static bool BlockedApp(string exe, ICollection<string> extra)
        {
            string e = (exe ?? "").ToLowerInvariant();
            if (e.EndsWith(".exe")) e = e.Substring(0, e.Length - 4);
            foreach (var b in BlockedExes) if (e == b || e.Replace(" ", "") == b.Replace(" ", "")) return true;
            if (extra != null) foreach (var b in extra) if (b.Length > 0 && e == b.ToLowerInvariant().Replace(".exe", "")) return true;
            return false;
        }

        public static bool PrivateWindow(string title, string url, ICollection<string> extraSites)
        {
            string t = (title ?? "").ToLowerInvariant();
            foreach (var w in PrivateTitles) if (t.Contains(w)) return true;
            if (extraSites != null && extraSites.Count > 0)
            {
                string host = Host(url);
                foreach (var s in extraSites)
                {
                    string site = s.Trim().ToLowerInvariant();
                    if (site.Length == 0) continue;
                    if (host.Length > 0 && (host == site || host.EndsWith("." + site))) return true;
                    if (t.Contains(site)) return true;
                }
            }
            return false;
        }

        public static string Host(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url)) return "";
            if (!url.Contains("://")) url = "https://" + url;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() : "";
        }

        // Ordered: specific secrets first, then broad personal data
        static readonly KeyValuePair<Regex, string>[] Rules =
        {
            R(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(-----END [A-Z ]*PRIVATE KEY-----|$)", "[kunci privat]"),
            R(@"\b(sk|pk|rk)-(ant-|proj-|live_|test_)?[A-Za-z0-9_\-]{16,}", "[kunci API]"),
            R(@"\b(gsk|ghp|gho|ghu|ghs|ghr|github_pat|glpat|xox[abprs]|AIza|ya29)[_\-.]?[A-Za-z0-9_\-]{16,}", "[token]"),
            R(@"\bAKIA[0-9A-Z]{16}\b", "[kunci AWS]"),
            R(@"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{5,}", "[JWT]"),
            R(@"(?i)\b(password|passwd|pwd|kata ?sandi|secret|token|api[_\-]?key)\s*[:=]\s*\S+", "$1: [disembunyikan]"),
            R(@"(?i)\bbearer\s+[A-Za-z0-9_\-.=]{12,}", "Bearer [token]"),
            R(@"\b(?:\d[ \-]?){13,19}\b", "[nomor kartu]"),
            R(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", "[email]"),
            R(@"(?<![\w])(\+62|62|0)8[1-9][0-9\- ]{6,11}[0-9](?![\w])", "[nomor HP]"),
            R(@"\b\d{16}\b", "[NIK]"),
        };

        static KeyValuePair<Regex, string> R(string p, string with) { return new KeyValuePair<Regex, string>(new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant), with); }

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            foreach (var r in Rules)
            {
                try { text = r.Key.Replace(text, r.Value); }
                catch (RegexMatchTimeoutException) { }
            }
            return text;
        }

        // URL without query or fragment: tokens like to hide there
        public static string CleanUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string u = url.Trim();
            int cut = u.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) u = u.Substring(0, cut);
            return u.Length > 300 ? u.Substring(0, 300) : u;
        }

        // Project from the window title: "file.cs - Deskmate - Visual Studio Code" → Deskmate
        public static string Project(string exe, string title)
        {
            string e = (exe ?? "").ToLowerInvariant(), t = title ?? "";
            var parts = t.Split(new[] { " - ", " — ", " – " }, StringSplitOptions.RemoveEmptyEntries);
            if (e == "code" || e == "cursor" || e == "windsurf" || e == "code - insiders" || e == "zed")
                return parts.Length >= 3 ? Strip(parts[parts.Length - 2]) : parts.Length == 2 ? Strip(parts[0]) : "";
            if (e == "devenv" || e.StartsWith("rider") || e.StartsWith("idea") || e.StartsWith("pycharm") || e.StartsWith("webstorm") || e.StartsWith("clion") || e.StartsWith("goland") || e == "studio64")
                return parts.Length >= 2 ? Strip(parts[0]) : "";
            if (e == "unity") return parts.Length >= 2 ? Strip(parts[0]) : "";
            if (e == "windowsterminal" || e == "powershell" || e == "pwsh" || e == "cmd" || e == "wezterm-gui" || e == "alacritty")
            {
                var m = Regex.Match(t, @"[A-Za-z]:\\(?:[^\\]+\\)*([^\\]+)\\?$");
                if (m.Success) return m.Groups[1].Value;
                var u = Regex.Match(t, @"(?:~|/[A-Za-z0-9_.\-]+)(?:/[^/\s]+)*/([^/\s]+?)/?$");
                if (u.Success) return u.Groups[1].Value;
            }
            return "";
        }

        static string Strip(string s)
        {
            s = s.Trim().TrimStart('●', '•', '*', ' ');
            if (s.StartsWith("[") && s.EndsWith("]")) s = s.Substring(1, s.Length - 2);
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }
    }
}
