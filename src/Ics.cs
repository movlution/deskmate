using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Deskmate
{
    // One occurrence or to-do from a feed
    class IcsItem
    {
        public string Uid = "", Summary = "", Location = "", Url = "", Meet = "";
        public bool Todo, AllDay, Cancelled;
        // Timed: UTC; all-day: dates, end exclusive
        public DateTime Start, End;
    }

    class IcsResult
    {
        public string Name = "";
        public TimeSpan Refresh;
        public readonly List<IcsItem> Items = new List<IcsItem>();
        public int Events, Todos, Complex, Broken, UnknownZones;
        public bool Capped;
    }

    // iCalendar reader with a recurrence subset
    static partial class Ics
    {
        public const int MaxBytes = 5 * 1024 * 1024;
        const int MaxLine = 16 * 1024, MaxComponents = 20000, MaxProps = 400, MaxDepth = 8, MaxItems = 4000, MaxPerEvent = 800, Steps = 400000;
        const int MaxAllProps = 150000, MaxOther = 4000, MaxZones = 64, MaxDates = 4000, MaxRuleParts = 100, MaxMs = 3000;

        // Only properties the reader uses
        static readonly HashSet<string> Kept = new HashSet<string>(StringComparer.Ordinal)
        {
            "X-WR-CALNAME", "REFRESH-INTERVAL", "X-PUBLISHED-TTL", "TZID", "TZOFFSETFROM", "TZOFFSETTO", "UID", "RECURRENCE-ID", "DTSTART", "DTEND", "DURATION", "DUE",
            "RRULE", "RDATE", "EXDATE", "STATUS", "COMPLETED", "PERCENT-COMPLETE", "SUMMARY", "LOCATION", "URL", "X-GOOGLE-CONFERENCE", "X-MICROSOFT-SKYPETEAMSMEETINGURL",
        };

        class Prop
        {
            public string Name, Value, Tzid, Type;
        }

        class Comp
        {
            public string Name;
            public Comp Parent;
            public readonly List<Prop> Props = new List<Prop>();
            public readonly List<Comp> Kids = new List<Comp>();

            public Prop Get(string name)
            {
                foreach (var p in Props) if (p.Name == name) return p;
                return null;
            }

            public string Text(string name)
            {
                var p = Get(name);
                return p == null ? "" : Unescape(p.Value).Trim();
            }
        }

        class Budget
        {
            public int Left = Steps;
            public bool Hit;
        }

        class Stamp
        {
            public DateTime Wall;
            public bool Date, Utc;
            public Zone Zone;
        }

        public static IcsResult Parse(byte[] data, int length, DateTime fromUtc, DateTime toUtc)
        {
            var res = new IcsResult();
            // Last line of defence: wall-clock cap
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var reader = new Reader(res);
            reader.Read(data, Math.Min(length, MaxBytes));
            var ctx = new Ctx(res);
            var events = new List<Comp>();
            var todos = new List<Comp>();
            foreach (var cal in reader.Root.Kids)
            {
                if (cal.Name != "VCALENDAR") continue;
                if (res.Name.Length == 0) res.Name = cal.Text("X-WR-CALNAME");
                var ri = cal.Get("REFRESH-INTERVAL") ?? cal.Get("X-PUBLISHED-TTL");
                TimeSpan every;
                if (ri != null && Duration(ri.Value, out every) && every > res.Refresh) res.Refresh = every;
                foreach (var k in cal.Kids)
                {
                    if (k.Name == "VTIMEZONE")
                    {
                        string id = k.Text("TZID");
                        if (id.Length > 0 && !ctx.Vtz.ContainsKey(id)) ctx.Vtz[id] = k;
                    }
                    else if (k.Name == "VEVENT") events.Add(k);
                    else if (k.Name == "VTODO") todos.Add(k);
                }
            }
            res.Events = events.Count;
            res.Todos = todos.Count;
            // Overrides may come before their master
            var masters = new List<Comp>();
            var overrides = new Dictionary<string, List<Comp>>(StringComparer.Ordinal);
            foreach (var ev in events)
            {
                string uid = ev.Text("UID");
                if (uid.Length > 0 && ev.Get("RECURRENCE-ID") != null)
                {
                    List<Comp> list;
                    if (!overrides.TryGetValue(uid, out list)) overrides[uid] = list = new List<Comp>();
                    list.Add(ev);
                }
                else masters.Add(ev);
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in masters)
            {
                if (res.Items.Count >= MaxItems || clock.ElapsedMilliseconds > MaxMs) { res.Capped = true; break; }
                string uid = m.Text("UID");
                List<Comp> ov = null;
                if (uid.Length > 0 && used.Add(uid)) overrides.TryGetValue(uid, out ov);
                Event(m, ov, false, ctx, fromUtc, toUtc);
            }
            foreach (var kv in overrides)
            {
                if (clock.ElapsedMilliseconds > MaxMs) { res.Capped = true; break; }
                if (!used.Contains(kv.Key))
                    foreach (var o in kv.Value) Event(o, null, true, ctx, fromUtc, toUtc);
            }
            foreach (var t in todos)
            {
                if (res.Items.Count >= MaxItems || clock.ElapsedMilliseconds > MaxMs) { res.Capped = true; break; }
                Todo(t, ctx, fromUtc, toUtc);
            }
            if (ctx.Budget.Hit) res.Capped = true;
            return res;
        }

        // Unfolds on bytes, then decodes
        class Reader
        {
            public readonly Comp Root = new Comp { Name = "" };
            readonly IcsResult res;
            readonly byte[] buf = new byte[MaxLine];
            Comp cur;
            int len, depth, skip, items, others, props;
            bool over, have;

            public Reader(IcsResult res) { this.res = res; cur = Root; }

            public void Read(byte[] data, int length)
            {
                int pos = 0;
                if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) pos = 3;
                while (pos < length)
                {
                    int s = pos;
                    while (pos < length && data[pos] != 10 && data[pos] != 13) pos++;
                    int e = pos;
                    if (pos < length) pos += data[pos] == 13 && pos + 1 < length && data[pos + 1] == 10 ? 2 : 1;
                    if (have && e > s && (data[s] == 32 || data[s] == 9)) { Append(data, s + 1, e - s - 1); continue; }
                    Flush();
                    have = true;
                    Append(data, s, e - s);
                }
                Flush();
            }

            void Append(byte[] data, int at, int n)
            {
                if (over || n <= 0) return;
                if (len + n > MaxLine) { over = true; return; }
                Buffer.BlockCopy(data, at, buf, len, n);
                len += n;
            }

            void Flush()
            {
                if (have)
                {
                    if (over) res.Broken++;
                    else if (len > 0) Line(Encoding.UTF8.GetString(buf, 0, len));
                }
                have = over = false;
                len = 0;
            }

            void Line(string line)
            {
                var p = ParseLine(line);
                if (p == null) { if (line.Trim().Length > 0) res.Broken++; return; }
                if (p.Name == "BEGIN")
                {
                    string what = p.Value.Trim().ToUpperInvariant();
                    bool item = what == "VEVENT" || what == "VTODO", full = item ? items >= MaxComponents : others >= MaxOther;
                    if (skip > 0 || depth >= MaxDepth || !Wanted(what) || full)
                    {
                        if (full && skip == 0 && Wanted(what)) res.Capped = true;
                        skip++;
                        return;
                    }
                    if (item) items++;
                    else others++;
                    var c = new Comp { Name = what, Parent = cur };
                    cur.Kids.Add(c);
                    cur = c;
                    depth++;
                    return;
                }
                if (p.Name == "END")
                {
                    if (skip > 0) { skip--; return; }
                    string what = p.Value.Trim().ToUpperInvariant();
                    for (var c = cur; c != null && c != Root; c = c.Parent)
                    {
                        depth--;
                        if (c.Name != what) continue;
                        cur = c.Parent;
                        return;
                    }
                    depth = 0;
                    for (var c = cur; c != null && c != Root; c = c.Parent) depth++;
                    return;
                }
                if (skip > 0 || cur == Root || cur.Props.Count >= MaxProps || !Kept.Contains(p.Name)) return;
                if (++props > MaxAllProps) { res.Capped = true; return; }
                cur.Props.Add(p);
            }

            static bool Wanted(string name)
            {
                return name == "VCALENDAR" || name == "VEVENT" || name == "VTODO" || name == "VTIMEZONE" || name == "STANDARD" || name == "DAYLIGHT";
            }
        }

        // name *(;param=value) : value, quotes respected
        static Prop ParseLine(string line)
        {
            int n = line.Length, i = 0;
            while (i < n && line[i] != ';' && line[i] != ':') i++;
            if (i == 0 || i >= n) return null;
            var p = new Prop { Name = line.Substring(0, i).Trim().ToUpperInvariant() };
            int dot = p.Name.IndexOf('.');
            if (dot >= 0) p.Name = p.Name.Substring(dot + 1);
            while (i < n && line[i] == ';')
            {
                i++;
                int ks = i;
                while (i < n && line[i] != '=' && line[i] != ';' && line[i] != ':') i++;
                string key = line.Substring(ks, i - ks).Trim().ToUpperInvariant();
                if (i >= n || line[i] != '=') continue;
                i++;
                string first = null;
                while (true)
                {
                    string v;
                    if (i < n && line[i] == '"')
                    {
                        int q = line.IndexOf('"', i + 1);
                        if (q < 0) return null;
                        v = line.Substring(i + 1, q - i - 1);
                        i = q + 1;
                    }
                    else
                    {
                        int vs = i;
                        while (i < n && line[i] != ';' && line[i] != ':' && line[i] != ',') i++;
                        v = line.Substring(vs, i - vs);
                    }
                    if (first == null) first = v;
                    if (i < n && line[i] == ',') { i++; continue; }
                    break;
                }
                first = Caret(first);
                if (key == "TZID" && p.Tzid == null) p.Tzid = first;
                else if (key == "VALUE" && p.Type == null) p.Type = first.Trim().ToUpperInvariant();
            }
            if (i >= n || line[i] != ':') return null;
            p.Value = line.Substring(i + 1);
            return p;
        }

        // RFC 6868: ^n ^^ ^'
        static string Caret(string v)
        {
            if (v.IndexOf('^') < 0) return v;
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (c == '^' && i + 1 < v.Length)
                {
                    char k = v[i + 1];
                    if (k == 'n') { sb.Append('\n'); i++; continue; }
                    if (k == '^') { sb.Append('^'); i++; continue; }
                    if (k == '\'') { sb.Append('"'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string Unescape(string v)
        {
            if (v.IndexOf('\\') < 0) return v;
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (c == '\\' && i + 1 < v.Length)
                {
                    char k = v[++i];
                    sb.Append(k == 'n' || k == 'N' ? '\n' : k);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // P[nW][nD][T[nH][nM][nS]], signed
        static bool Duration(string text, out TimeSpan d)
        {
            d = TimeSpan.Zero;
            string v = text.Trim().ToUpperInvariant();
            int i = 0;
            bool neg = false, time = false;
            if (i < v.Length && (v[i] == '+' || v[i] == '-')) { neg = v[i] == '-'; i++; }
            if (i >= v.Length || v[i] != 'P') return false;
            long num = -1;
            double secs = 0;
            for (i++; i < v.Length; i++)
            {
                char c = v[i];
                if (c == 'T') { if (num >= 0) return false; time = true; continue; }
                if (c >= '0' && c <= '9')
                {
                    num = (num < 0 ? 0 : num) * 10 + (c - '0');
                    if (num > 100000000) return false;
                    continue;
                }
                if (num < 0) return false;
                if (c == 'W' && !time) secs += num * 604800.0;
                else if (c == 'D' && !time) secs += num * 86400.0;
                else if (c == 'H' && time) secs += num * 3600.0;
                else if (c == 'M' && time) secs += num * 60.0;
                else if (c == 'S' && time) secs += num;
                else return false;
                num = -1;
            }
            if (num >= 0) return false;
            secs = Math.Min(secs, 400 * 86400.0);
            d = TimeSpan.FromSeconds(neg ? -secs : secs);
            return true;
        }

        // DATE or DATE-TIME; lenient about separators
        static bool ParseStamp(string v, string type, out DateTime wall, out bool date, out bool utc)
        {
            wall = DateTime.MinValue;
            date = utc = false;
            var sb = new StringBuilder(20);
            foreach (char c in v.Trim()) if (c != '-' && c != ':') sb.Append(c);
            string s = sb.ToString();
            if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) { utc = true; s = s.Substring(0, s.Length - 1); }
            int t = s.IndexOfAny(new[] { 'T', 't' });
            string d = t < 0 ? s : s.Substring(0, t);
            if (d.Length != 8 || !Digits(d)) return false;
            int y = Num(d, 0, 4), mo = Num(d, 4, 2), da = Num(d, 6, 2);
            if (y < 1600 || y > 9000 || mo < 1 || mo > 12 || da < 1 || da > DateTime.DaysInMonth(y, mo)) return false;
            if (type == "DATE" || t < 0)
            {
                date = true;
                utc = false;
                wall = new DateTime(y, mo, da);
                return true;
            }
            string tm = s.Substring(t + 1);
            int frac = tm.IndexOf('.');
            if (frac >= 0) tm = tm.Substring(0, frac);
            if ((tm.Length != 6 && tm.Length != 4) || !Digits(tm)) return false;
            int hh = Num(tm, 0, 2), mm = Num(tm, 2, 2), ss = tm.Length == 6 ? Num(tm, 4, 2) : 0;
            if (hh > 23 || mm > 59 || ss > 60) return false;
            wall = new DateTime(y, mo, da, hh, mm, Math.Min(ss, 59));
            return true;
        }

        static bool Digits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        static int Num(string s, int at, int n)
        {
            int v = 0;
            for (int i = at; i < at + n; i++) v = v * 10 + (s[i] - '0');
            return v;
        }

    }
}
