using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Deskmate
{
    // One structured look at the active window: focus, headings, main viewport, side navigation, and concrete anchors
    sealed class IngatSnapshot
    {
        public string Source = "window";
        public string App = "", Exe = "", Project = "", Title = "", Url = "", Kind = "", Focus = "";
        public readonly List<string> Headings = new List<string>();
        public readonly List<string> MainLines = new List<string>();
        public readonly List<string> NavLines = new List<string>();
        public readonly List<string> Anchors = new List<string>();
        public bool Secret;
        public DateTime CapturedUtc = DateTime.UtcNow;

        public void ClearSecret()
        {
            Secret = true;
            Source = "window";
            Focus = "";
            Headings.Clear();
            MainLines.Clear();
            NavLines.Clear();
            Anchors.Clear();
        }

        public void RedactAll()
        {
            Title = IngatPrivacy.Redact(Title);
            Url = IngatPrivacy.CleanUrl(Url);
            Focus = IngatPrivacy.Redact(Focus);
            RedactList(Headings);
            RedactList(MainLines);
            RedactList(NavLines);
            RedactList(Anchors);
        }

        static void RedactList(List<string> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                string s = IngatPrivacy.Redact(list[i]).Trim();
                if (s.Length == 0) list.RemoveAt(i);
                else list[i] = s;
            }
        }

        // Structured text stored in events and episodes: keeps [x0-x1] geometry for chats, and clean labeled sections for everything else
        public string FormatText(int maxChars)
        {
            if (Secret) return "";
            var sb = new StringBuilder();
            if (Source == "chat")
            {
                if (Focus.Length > 0) sb.Append("[Header] ").Append(Focus).Append('\n');
                for (int i = 0; i < MainLines.Count && sb.Length < maxChars; i++)
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
                    sb.Append(MainLines[i]);
                }
                string chat = sb.ToString().TrimEnd();
                return chat.Length > maxChars ? chat.Substring(0, maxChars) : chat;
            }

            if (Focus.Length > 0 && !SameLine(Focus, Title))
                sb.Append("[Fokus] ").Append(Fmt.OneLine(Focus, 180)).Append('\n');
            if (Headings.Count > 0)
                sb.Append("[Bagian] ").Append(JoinCap(Headings, " · ", 280)).Append('\n');
            if (Anchors.Count > 0)
                sb.Append("[Konteks] ").Append(JoinCap(Anchors, ", ", 260)).Append('\n');
            if (MainLines.Count > 0)
            {
                if (sb.Length > 0) sb.Append("[Isi]\n");
                for (int i = 0; i < MainLines.Count && sb.Length < maxChars; i++)
                {
                    string line = MainLines[i];
                    if (line.Length == 0) continue;
                    int room = maxChars - sb.Length;
                    if (room <= 0) break;
                    sb.Append(line.Length > room ? line.Substring(0, room) : line).Append('\n');
                }
            }
            if (NavLines.Count > 0 && sb.Length + 40 < maxChars)
            {
                string nav = JoinCap(NavLines, " · ", Math.Min(360, maxChars - sb.Length - 14));
                if (nav.Length > 0) sb.Append("[Navigasi] ").Append(nav).Append('\n');
            }
            string res = sb.ToString().TrimEnd();
            return res.Length > maxChars ? res.Substring(0, maxChars) : res;
        }

        static bool SameLine(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        static string JoinCap(List<string> items, string sep, int cap)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                string s = Fmt.OneLine(items[i], 100);
                if (s.Length == 0) continue;
                int extra = (sb.Length > 0 ? sep.Length : 0) + s.Length;
                if (sb.Length + extra > cap) break;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(s);
            }
            return sb.ToString();
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>
            {
                { "source", Source },
                { "app", App },
                { "project", Project },
                { "title", Title },
                { "kind", Kind },
                { "capturedAt", CapturedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
            };
            if (Url.Length > 0) d["url"] = Url;
            if (Focus.Length > 0) d["focus"] = Focus;
            if (Headings.Count > 0) d["headings"] = Headings.ToArray();
            if (Anchors.Count > 0) d["anchors"] = Anchors.ToArray();
            var hi = new List<string>();
            for (int i = 0; i < MainLines.Count && hi.Count < 6; i++)
            {
                string l = Fmt.OneLine(StripPlacement(MainLines[i]), 160);
                if (l.Length >= 3) hi.Add(l);
            }
            if (hi.Count > 0) d["highlights"] = hi.ToArray();
            string ex = Excerpt(600);
            if (ex.Length > 0) d["excerpt"] = ex;
            return d;
        }

        public string Excerpt(int maxChars)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < MainLines.Count && sb.Length < maxChars; i++)
            {
                string l = Fmt.OneLine(StripPlacement(MainLines[i]), 220);
                if (l.Length < 2) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(l);
            }
            string s = sb.ToString();
            return s.Length > maxChars ? s.Substring(0, maxChars) : s;
        }

        static string StripPlacement(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '[') return s ?? "";
            int close = s.IndexOf("] ", StringComparison.Ordinal);
            return close > 1 && close < 10 ? s.Substring(close + 2) : s;
        }
    }

    // Structured anatomy of an episode across window switches, tabs, files, and scrolls
    sealed class IngatContext
    {
        public string Focus = "";
        public readonly List<Dictionary<string, object>> Steps = new List<Dictionary<string, object>>();
        public readonly List<string> Headings = new List<string>();
        public readonly List<string> Anchors = new List<string>();
        public readonly List<string> Points = new List<string>();
        public bool CuratedPoints;

        const int MaxSteps = 24, MaxHeadings = 16, MaxAnchors = 20, MaxPoints = 8;

        public static IngatContext Parse(string raw)
        {
            var ctx = new IngatContext();
            if (string.IsNullOrEmpty(raw)) return ctx;
            Dictionary<string, object> d = null;
            try { d = Json.Parse(raw) as Dictionary<string, object>; }
            catch (Exception) { }
            if (d == null) return ctx;
            ctx.Focus = Json.Str(d, "focus");
            ctx.CuratedPoints = Json.Str(d, "curated") == "True" || Json.Str(d, "curated") == "true" || Json.Str(d, "curated") == "1";
            foreach (var o in Json.Arr(d, "steps"))
            {
                var sd = o as Dictionary<string, object>;
                if (sd == null) continue;
                string t = Json.Str(sd, "title"), f = Json.Str(sd, "focus"), at = Json.Str(sd, "at");
                if (t.Length == 0 && f.Length == 0) continue;
                var step = new Dictionary<string, object> { { "at", at }, { "title", t } };
                if (f.Length > 0) step["focus"] = f;
                if (ctx.Steps.Count < MaxSteps) ctx.Steps.Add(step);
            }
            ReadStrings(d, "headings", ctx.Headings, MaxHeadings);
            ReadStrings(d, "anchors", ctx.Anchors, MaxAnchors);
            ReadStrings(d, "points", ctx.Points, MaxPoints);
            return ctx;
        }

        static void ReadStrings(Dictionary<string, object> d, string key, List<string> into, int max)
        {
            foreach (var o in Json.Arr(d, key))
            {
                string s = Fmt.OneLine(Convert.ToString(o, CultureInfo.InvariantCulture), 220);
                if (s.Length > 0 && into.Count < max) AddUnique(into, s, max);
            }
        }

        public string Encode()
        {
            if (Focus.Length == 0 && Steps.Count == 0 && Headings.Count == 0 && Anchors.Count == 0 && Points.Count == 0) return "";
            var d = new Dictionary<string, object>();
            if (Focus.Length > 0) d["focus"] = Focus;
            if (Steps.Count > 0) d["steps"] = Steps.ToArray();
            if (Headings.Count > 0) d["headings"] = Headings.ToArray();
            if (Anchors.Count > 0) d["anchors"] = Anchors.ToArray();
            if (Points.Count > 0) d["points"] = Points.ToArray();
            if (CuratedPoints) d["curated"] = true;
            return Json.Encode(d);
        }

        public void Absorb(DateTime utc, string title, string url, string text, IngatSnapshot snap)
        {
            string cleanTitle = Fmt.OneLine(IngatPrivacy.Redact(title ?? ""), 160);
            string focus = snap != null && snap.Focus.Length > 0 ? Fmt.OneLine(IngatPrivacy.Redact(snap.Focus), 140) : "";
            if (focus.Length > 0) Focus = focus;

            if (cleanTitle.Length > 0 || focus.Length > 0)
            {
                bool sameAsLast = false;
                if (Steps.Count > 0)
                {
                    var last = Steps[Steps.Count - 1];
                    string lt = Json.Str(last, "title"), lf = Json.Str(last, "focus");
                    sameAsLast = string.Equals(lt, cleanTitle, StringComparison.OrdinalIgnoreCase) &&
                                 (focus.Length == 0 || lf.Length == 0 || string.Equals(lf, focus, StringComparison.OrdinalIgnoreCase));
                    if (sameAsLast && lf.Length == 0 && focus.Length > 0 && !string.Equals(focus, cleanTitle, StringComparison.OrdinalIgnoreCase))
                        last["focus"] = focus;
                }
                if (!sameAsLast)
                {
                    if (Steps.Count >= MaxSteps) Steps.RemoveAt(1); // Keep initial step[0] + newest trail
                    var step = new Dictionary<string, object>
                    {
                        { "at", utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) },
                        { "title", cleanTitle },
                    };
                    if (focus.Length > 0 && !string.Equals(focus, cleanTitle, StringComparison.OrdinalIgnoreCase)) step["focus"] = focus;
                    Steps.Add(step);
                }
            }

            if (snap != null)
            {
                for (int i = 0; i < snap.Headings.Count; i++)
                    AddUnique(Headings, Fmt.OneLine(IngatPrivacy.Redact(snap.Headings[i]), 120), MaxHeadings);
                for (int i = 0; i < snap.Anchors.Count; i++)
                    AddUnique(Anchors, Fmt.OneLine(IngatPrivacy.Redact(snap.Anchors[i]), 100), MaxAnchors);
            }

            var found = IngatStructure.ExtractAnchors(cleanTitle, url, text, 10);
            for (int i = 0; i < found.Count; i++) AddUnique(Anchors, found[i], MaxAnchors);

            if (!CuratedPoints) RebuildAutoPoints(snap, text);
        }

        void RebuildAutoPoints(IngatSnapshot snap, string text)
        {
            if (snap != null)
            {
                for (int i = 0; i < snap.MainLines.Count && Points.Count < MaxPoints; i++)
                {
                    string l = Fmt.OneLine(snap.MainLines[i], 150);
                    if (l.Length >= 12 && !l.StartsWith("[", StringComparison.Ordinal)) AddUnique(Points, l, MaxPoints);
                }
            }
            else if (!string.IsNullOrEmpty(text) && text.StartsWith("Aku menulis: ", StringComparison.Ordinal))
            {
                AddUnique(Points, Fmt.OneLine(IngatPrivacy.Redact(text), 160), MaxPoints);
            }
        }

        public void ApplyAi(string[] points, string[] anchors)
        {
            if (points != null && points.Length > 0)
            {
                Points.Clear();
                for (int i = 0; i < points.Length && Points.Count < MaxPoints; i++)
                {
                    string p = Fmt.OneLine(IngatPrivacy.Redact(points[i]), 220);
                    if (p.Length >= 3) AddUnique(Points, p, MaxPoints);
                }
                if (Points.Count > 0) CuratedPoints = true;
            }
            if (anchors != null)
            {
                for (int i = 0; i < anchors.Length; i++)
                {
                    string a = Fmt.OneLine(IngatPrivacy.Redact(anchors[i]), 100);
                    if (a.Length >= 2) AddUnique(Anchors, a, MaxAnchors);
                }
            }
        }

        public void WriteTo(Dictionary<string, object> d)
        {
            if (Focus.Length > 0) d["focus"] = Focus;
            if (Steps.Count > 0) d["steps"] = Steps.ToArray();
            if (Headings.Count > 0) d["headings"] = Headings.ToArray();
            if (Anchors.Count > 0) d["anchors"] = Anchors.ToArray();
            if (Points.Count > 0) d["points"] = Points.ToArray();
        }

        // Indexed alongside title and summary in FTS5 so intermediate files, steps, and anchors are searchable
        public string SearchTitles()
        {
            var sb = new StringBuilder();
            if (Focus.Length > 0) sb.Append(Focus).Append(' ');
            for (int i = 0; i < Steps.Count; i++)
            {
                sb.Append(Json.Str(Steps[i], "title")).Append(' ');
                string f = Json.Str(Steps[i], "focus");
                if (f.Length > 0) sb.Append(f).Append(' ');
            }
            for (int i = 0; i < Headings.Count; i++) sb.Append(Headings[i]).Append(' ');
            return sb.ToString().Trim();
        }

        public string SearchSummary()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Anchors.Count; i++) sb.Append(Anchors[i]).Append(' ');
            for (int i = 0; i < Points.Count; i++) sb.Append(Points[i]).Append(' ');
            return sb.ToString().Trim();
        }

        internal static void AddUnique(List<string> list, string item, int max)
        {
            if (string.IsNullOrEmpty(item)) return;
            item = item.Trim();
            if (item.Length < 2) return;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], item, StringComparison.OrdinalIgnoreCase)) return;
            if (list.Count >= max) list.RemoveAt(0);
            list.Add(item);
        }
    }

    // Fast deterministic structuring: activity kind, noise filtering, concrete anchors, rolling body dedup, and timeline grouping
    static class IngatStructure
    {
        static readonly string[] CodingExes =
        {
            "code", "code - insiders", "cursor", "windsurf", "zed", "devenv", "studio64", "sublime_text", "notepad++",
            "nvim", "vim", "neovide", "windowsterminal", "wt", "powershell", "pwsh", "cmd", "wezterm-gui", "alacritty",
            "mintty", "git-bash", "unity", "unrealeditor", "godot", "dbeaver", "datagrip64", "postman", "insomnia", "linqpad",
        };

        static readonly string[] CodingPrefixes = { "rider", "idea", "pycharm", "webstorm", "clion", "goland", "phpstorm", "rubymine", "rustrover" };

        static readonly string[] DesignExes =
        {
            "figma", "photoshop", "illustrator", "indesign", "afterfx", "premiere", "blender", "aseprite", "krita", "gimp",
            "inkscape", "canva", "sketchup", "paintdotnet", "mspaint",
        };

        static readonly string[] WritingExes = { "winword", "obsidian", "notion", "typora", "logseq", "scrivener", "notepad", "wordpad", "onenote" };
        static readonly string[] AdminExes = { "excel", "powerpnt", "explorer", "taskmgr", "mmc", "regedit", "control", "systemsettings", "calc" };
        static readonly string[] MediaExes = { "spotify", "vlc", "mpv", "mpc-hc", "mpc-hc64", "potplayer", "potplayermini64", "obs64", "wmplayer", "music.ui" };
        static readonly string[] ReaderExes = { "acrord32", "acrobat", "sumatrapdf", "foxitpdfreader", "foxitreader", "okular", "calibre" };

        static readonly HashSet<string> ChromeWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "minimize", "maximize", "restore", "close", "tutup", "perkecil", "perbesar", "pulihkan",
            "back", "forward", "reload", "refresh", "kembali", "maju", "muat ulang",
            "menu", "system", "title bar", "tab bar", "status bar", "address and search bar", "scroll bar",
            "vertical", "horizontal", "page up", "page down", "line up", "line down", "thumb",
            "new tab", "tab baru", "close tab", "tutup tab", "more options", "opsi lainnya", "settings",
        };

        static readonly Regex FileRx = new Regex(
            @"(?<![\w/\\.-])([A-Za-z0-9_\-./\\]{1,64}\.(?:cs|ts|tsx|js|jsx|mjs|py|rs|go|java|kt|cpp|c|h|hpp|json|yaml|yml|toml|xml|html|css|scss|sql|ps1|sh|cmd|bat|md|pdf|docx|xlsx|pptx|sln|csproj|iss|proto|graphql))(?![\w])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex CodeTokenRx = new Regex(
            @"\b(CS\d{4}|TS\d{4,5}|HTTP\s+[45]\d{2}|0x[0-9A-Fa-f]{6,8}|[A-Z][A-Za-z0-9]{2,40}(?:Exception|Error))\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static readonly Regex CmdRx = new Regex(
            @"(?:^|[\n>$#])\s*((?:git|npm|pnpm|yarn|npx|dotnet|cargo|go|python|pip|uv|docker|kubectl|claude|ollama|csc)\s+[A-Za-z0-9_\-./:@ ]{2,48})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Classifies into IngatBrain's kinds without using 'meeting' (reserved for Rapat transcripts)
        public static string Classify(string exe, string url, string title, string text)
        {
            string e = (exe ?? "").Trim().ToLowerInvariant();
            if (e.EndsWith(".exe")) e = e.Substring(0, e.Length - 4);
            string ch = IngatChat.Kind(e, url, title);
            if (ch == "chat" || ch == "mail") return "chat";

            if (Array.IndexOf(CodingExes, e) >= 0) return "coding";
            for (int i = 0; i < CodingPrefixes.Length; i++) if (e.StartsWith(CodingPrefixes[i], StringComparison.Ordinal)) return "coding";
            if (Array.IndexOf(DesignExes, e) >= 0 || e.StartsWith("affinity", StringComparison.Ordinal)) return "design";
            if (Array.IndexOf(WritingExes, e) >= 0) return "writing";
            if (Array.IndexOf(AdminExes, e) >= 0) return "admin";
            if (Array.IndexOf(MediaExes, e) >= 0) return "media";
            if (Array.IndexOf(ReaderExes, e) >= 0) return "reading";

            string host = IngatPrivacy.Host(url);
            string lowUrl = (url ?? "").ToLowerInvariant();
            string lowTitle = (title ?? "").ToLowerInvariant();

            if (host.Length > 0)
            {
                if (host.Contains("github.com") || host.Contains("gitlab.com") || host.Contains("bitbucket.org") ||
                    host == "localhost" || host == "127.0.0.1" || host.Contains("stackoverflow.com") ||
                    host.Contains("npmjs.com") || host.Contains("nuget.org") || host.Contains("crates.io") || host.Contains("pypi.org"))
                    return "coding";
                if (host.Contains("figma.com") || host.Contains("canva.com") || host.Contains("dribbble.com") || host.Contains("behance.net"))
                    return "design";
                if (host.Contains("youtube.com") || host.Contains("netflix.com") || host.Contains("twitch.tv") || host.Contains("spotify.com") || host.Contains("soundcloud.com"))
                    return "media";
                if (lowUrl.Contains("docs.google.com/document") || host.Contains("notion.so") || host.Contains("overleaf.com") || host.Contains("medium.com/p/"))
                    return "writing";
                if (lowUrl.Contains("docs.google.com/spreadsheets") || host.Contains("linear.app") || host.Contains("trello.com") ||
                    host.Contains("asana.com") || host.Contains("atlassian.net") || host.Contains("calendar.google.com"))
                    return "admin";
                if (host.Contains("meet.google.com") || host.Contains("zoom.us"))
                    return "chat";
            }

            if (IngatText.IsBrowser(e))
            {
                if (lowTitle.Contains("pull request") || lowTitle.Contains("commit") || lowTitle.Contains("github") || lowTitle.Contains("stack overflow"))
                    return "coding";
                if (lowTitle.Contains("youtube") || lowTitle.Contains("netflix") || lowTitle.Contains("twitch"))
                    return "media";
                return "reading";
            }
            if (lowTitle.EndsWith(".pdf", StringComparison.Ordinal)) return "reading";
            return "other";
        }

        // Strips icon glyphs and window chrome labels so UIA walks keep meaningful text
        public static string CleanLine(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            bool prevSpace = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                // Private Use Area glyphs (Segoe MDL2 / Fluent icons)
                if (c >= '\uE000' && c <= '\uF8FF') continue;
                if (c == '\r' || c == '\t') c = ' ';
                if (c == ' ')
                {
                    if (prevSpace) continue;
                    prevSpace = true;
                }
                else prevSpace = false;
                if (!char.IsControl(c) || c == '\n') sb.Append(c);
            }
            string s = sb.ToString().Trim();
            if (s.Length < 2 || ChromeWords.Contains(s)) return "";
            bool hasAlnum = false;
            for (int i = 0; i < s.Length; i++) if (char.IsLetterOrDigit(s[i])) { hasAlnum = true; break; }
            return hasAlnum ? s : "";
        }

        public static List<string> ExtractAnchors(string title, string url, string text, int max)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(url))
            {
                string slug = UrlAnchor(url);
                if (slug.Length > 0) IngatContext.AddUnique(list, slug, max);
            }
            string combined = (title ?? "") + "\n" + ((text ?? "").Length > 3500 ? text.Substring(0, 3500) : (text ?? ""));
            combined = IngatPrivacy.Redact(combined);

            foreach (Match m in FileRx.Matches(combined))
            {
                if (list.Count >= max) break;
                string f = m.Groups[1].Value.TrimStart('.', '/', '\\');
                if (f.Length >= 3 && !f.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    int slash = Math.Max(f.LastIndexOf('/'), f.LastIndexOf('\\'));
                    if (slash > 0 && f.Length > 36) f = f.Substring(slash + 1);
                    IngatContext.AddUnique(list, f, max);
                }
            }
            foreach (Match m in CodeTokenRx.Matches(combined))
            {
                if (list.Count >= max) break;
                IngatContext.AddUnique(list, Regex.Replace(m.Groups[1].Value, @"\s+", " "), max);
            }
            foreach (Match m in CmdRx.Matches(combined))
            {
                if (list.Count >= max) break;
                IngatContext.AddUnique(list, m.Groups[1].Value.Trim(), max);
            }
            return list;
        }

        static string UrlAnchor(string url)
        {
            string clean = IngatPrivacy.CleanUrl(url);
            if (clean.Length == 0) return "";
            Uri u;
            if (!clean.Contains("://")) clean = "https://" + clean;
            if (!Uri.TryCreate(clean, UriKind.Absolute, out u)) return "";
            string host = u.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
            string path = u.AbsolutePath.Trim('/');
            if (path.Length == 0) return host;
            var segs = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder(host);
            for (int i = 0; i < Math.Min(2, segs.Length); i++)
            {
                if (segs[i].Length > 28) break;
                sb.Append('/').Append(segs[i]);
            }
            return sb.ToString();
        }

        // Line-deduplicating rolling body: scrolling appends only new lines, and long episodes never freeze at BodyCap
        public static string MergeBody(string existing, string addition, int cap)
        {
            if (string.IsNullOrEmpty(addition)) return existing ?? "";
            if (string.IsNullOrEmpty(existing))
                return SafePrefix(addition, cap);
            if (existing.IndexOf(addition, StringComparison.Ordinal) >= 0) return existing;

            var known = new HashSet<string>(StringComparer.Ordinal);
            var curLines = existing.Split('\n');
            for (int i = 0; i < curLines.Length; i++)
            {
                string k = curLines[i].Trim();
                if (k.Length > 0) known.Add(k);
            }

            var added = new StringBuilder();
            var newLines = addition.Split('\n');
            for (int i = 0; i < newLines.Length; i++)
            {
                string raw = newLines[i].TrimEnd('\r');
                string k = raw.Trim();
                if (k.Length == 0) continue;
                // Section tags and volatile metadata headers live in episodes.context; keep body focused on content lines
                if (k.StartsWith("[", StringComparison.Ordinal) &&
                    (k.EndsWith("]", StringComparison.Ordinal) ||
                     k.StartsWith("[Fokus] ", StringComparison.Ordinal) ||
                     k.StartsWith("[Bagian] ", StringComparison.Ordinal) ||
                     k.StartsWith("[Konteks] ", StringComparison.Ordinal) ||
                     k.StartsWith("[Navigasi] ", StringComparison.Ordinal)))
                    continue;
                if (!known.Add(k)) continue;
                if (added.Length > 0) added.Append('\n');
                added.Append(raw);
            }
            if (added.Length == 0) return existing;

            string merged = existing + "\n" + added;
            if (merged.Length <= cap) return merged;
            if (cap < 64) return SafeSuffix(merged, cap);

            // Preserve the opening context (up to 1800 chars) + newest activity within cap
            int headLen = Math.Min(1800, cap / 5);
            int headBreak = merged.IndexOf('\n', headLen);
            if (headBreak < 0 || headBreak > headLen + 300) headBreak = headLen;
            string head = SafePrefix(merged, headBreak).TrimEnd();
            int tailBudget = Math.Max(16, cap - head.Length - 5);
            int tailStart = Math.Max(head.Length, merged.Length - tailBudget);
            int tailBreak = merged.IndexOf('\n', tailStart);
            if (tailBreak > tailStart && tailBreak < merged.Length - 40) tailStart = tailBreak + 1;
            if (tailStart < merged.Length && char.IsLowSurrogate(merged[tailStart])) tailStart++;
            string tail = merged.Substring(tailStart).TrimStart();
            string rolled = head + "\n…\n" + tail;
            return rolled.Length > cap ? SafeSuffix(rolled, cap) : rolled;
        }

        static string SafePrefix(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) return "";
            if (s.Length <= max) return s;
            int cut = max;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            return cut > 0 ? s.Substring(0, cut) : "";
        }

        static string SafeSuffix(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || max <= 0) return "";
            if (s.Length <= max) return s;
            int start = s.Length - max;
            if (char.IsLowSurrogate(s[start])) start++;
            return start < s.Length ? s.Substring(start) : "";
        }

        // Parses [x0-x1] placed OCR lines into structured regions when UIA exposed nothing
        public static void FromPlacedOcr(string placed, IngatSnapshot snap, bool chatMode)
        {
            if (string.IsNullOrEmpty(placed) || snap == null) return;
            var lines = placed.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (chatMode)
                {
                    snap.MainLines.Add(line);
                    continue;
                }
                int x0 = 0, x1 = 100;
                string body = line;
                if (line[0] == '[')
                {
                    int dash = line.IndexOf('-');
                    int close = line.IndexOf("] ", StringComparison.Ordinal);
                    if (dash > 1 && close > dash && close < 10)
                    {
                        int.TryParse(line.Substring(1, dash - 1), out x0);
                        int.TryParse(line.Substring(dash + 1, close - dash - 1), out x1);
                        body = line.Substring(close + 2);
                    }
                }
                body = CleanLine(body);
                if (body.Length == 0) continue;
                if (x1 <= 24 && (x1 - x0) <= 22)
                {
                    if (snap.NavLines.Count < 10) IngatContext.AddUnique(snap.NavLines, body, 10);
                }
                else
                {
                    if (i < 3 && body.Length <= 90 && snap.Headings.Count < 4) IngatContext.AddUnique(snap.Headings, body, 4);
                    snap.MainLines.Add(body);
                }
            }
            var anchors = ExtractAnchors(snap.Title, snap.Url, snap.Excerpt(2500), 10);
            for (int i = 0; i < anchors.Count; i++) IngatContext.AddUnique(snap.Anchors, anchors[i], 12);
        }

        // Organizes episodes into projects, activity kinds, top anchors, and key highlights
        public static Dictionary<string, object> GroupTimeline(List<IngatEpisode> eps)
        {
            var projMin = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var projCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var projApps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var projAnchors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var kindMin = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var kindCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var allAnchors = new List<string>();
            double totalMin = 0;

            for (int i = 0; i < eps.Count; i++)
            {
                var ep = eps[i];
                double m = Math.Max(0.2, (ep.EndUtc - ep.StartUtc).TotalMinutes);
                totalMin += m;
                string pk = ep.Project.Length > 0 ? ep.Project : ep.App.Length > 0 ? ep.App : "(lainnya)";
                double pm;
                projMin.TryGetValue(pk, out pm);
                projMin[pk] = pm + m;
                int pc;
                projCount.TryGetValue(pk, out pc);
                projCount[pk] = pc + 1;
                List<string> apps;
                if (!projApps.TryGetValue(pk, out apps)) { apps = new List<string>(); projApps[pk] = apps; }
                if (ep.App.Length > 0) IngatContext.AddUnique(apps, ep.App, 6);

                string kk = ep.Kind.Length > 0 ? ep.Kind : Classify(ep.Exe, ep.Url, ep.Title, "");
                double km;
                kindMin.TryGetValue(kk, out km);
                kindMin[kk] = km + m;
                int kc;
                kindCount.TryGetValue(kk, out kc);
                kindCount[kk] = kc + 1;

                var ctx = IngatContext.Parse(ep.Context);
                List<string> pa;
                if (!projAnchors.TryGetValue(pk, out pa)) { pa = new List<string>(); projAnchors[pk] = pa; }
                for (int j = 0; j < ctx.Anchors.Count; j++)
                {
                    IngatContext.AddUnique(pa, ctx.Anchors[j], 8);
                    IngatContext.AddUnique(allAnchors, ctx.Anchors[j], 15);
                }
            }

            var pKeys = new List<string>(projMin.Keys);
            pKeys.Sort((a, b) => projMin[b].CompareTo(projMin[a]));
            var byProject = new List<object>();
            for (int i = 0; i < Math.Min(12, pKeys.Count); i++)
            {
                string k = pKeys[i];
                var item = new Dictionary<string, object>
                {
                    { "name", k },
                    { "minutes", (int)Math.Round(projMin[k]) },
                    { "episodes", projCount[k] },
                    { "apps", projApps[k].ToArray() },
                };
                if (projAnchors[k].Count > 0) item["anchors"] = projAnchors[k].ToArray();
                byProject.Add(item);
            }

            var kKeys = new List<string>(kindMin.Keys);
            kKeys.Sort((a, b) => kindMin[b].CompareTo(kindMin[a]));
            var byKind = new List<object>();
            for (int i = 0; i < kKeys.Count; i++)
            {
                string k = kKeys[i];
                byKind.Add(new Dictionary<string, object>
                {
                    { "kind", k },
                    { "minutes", (int)Math.Round(kindMin[k]) },
                    { "episodes", kindCount[k] },
                });
            }

            return new Dictionary<string, object>
            {
                { "totalMinutes", (int)Math.Round(totalMin) },
                { "episodeCount", eps.Count },
                { "byProject", byProject.ToArray() },
                { "byKind", byKind.ToArray() },
                { "anchors", allAnchors.ToArray() },
            };
        }
    }
}
