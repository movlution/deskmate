using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Deskmate
{
    static partial class Ics
    {
        // Feed context: time zones, budget
        class Ctx
        {
            public readonly IcsResult Res;
            public readonly Budget Budget = new Budget();
            public readonly Dictionary<string, Comp> Vtz = new Dictionary<string, Comp>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, Zone> cache = new Dictionary<string, Zone>(StringComparer.OrdinalIgnoreCase);

            public Ctx(IcsResult res) { Res = res; }

            public Stamp Time(Prop p) { return p == null ? null : Time(p.Value, p.Type, p.Tzid); }

            public Stamp Time(string v, string type, string tzid)
            {
                DateTime wall;
                bool date, utc;
                if (!ParseStamp(v, type, out wall, out date, out utc)) return null;
                var t = new Stamp { Wall = wall, Date = date, Utc = utc };
                if (!date && !utc) t.Zone = string.IsNullOrEmpty(tzid) ? Local : Resolve(tzid);
                return t;
            }

            public DateTime Utc(Stamp t)
            {
                if (t.Utc) return DateTime.SpecifyKind(t.Wall, DateTimeKind.Utc);
                return (t.Zone ?? Local).ToUtc(t.Wall);
            }

            Zone Resolve(string tzid)
            {
                string id = tzid.Trim().TrimStart('/');
                Zone z;
                if (cache.TryGetValue(id, out z)) return z;
                // Lookups are costly; real feeds use few
                if (cache.Count >= MaxZones) { Res.UnknownZones++; return Local; }
                z = Find(id);
                if (z == null) { Res.UnknownZones++; z = Local; }
                cache[id] = z;
                return z;
            }

            // Windows id, ICU, VTIMEZONE, name, offset
            Zone Find(string id)
            {
                if (id.Length == 0 || id.Length > 120) return null;
                FeedZone feed = null;
                Comp vtz;
                if (Vtz.TryGetValue(id, out vtz))
                {
                    feed = new FeedZone(vtz, this);
                    if (!feed.Valid) feed = null;
                }
                var sys = Sys(id);
                if (sys != null) return sys;
                string win = WindowsId(id);
                if (win != null && (sys = Sys(win)) != null) return feed != null && !Agree(sys, feed) ? (Zone)feed : sys;
                if (feed != null) return feed;
                foreach (var tz in SystemZones())
                    if (string.Equals(tz.DisplayName, id, StringComparison.OrdinalIgnoreCase)) return new SysZone(tz);
                TimeSpan off;
                if (UtcPrefix(id, out off)) return new FixedZone(off);
                return null;
            }

            // Stale ICU data loses to the feed
            static bool Agree(Zone a, Zone b)
            {
                var now = DateTime.UtcNow;
                for (int i = 0; i < 3; i++)
                {
                    var at = now.AddDays(i * 122);
                    if (a.Offset(at) != b.Offset(at)) return false;
                }
                return true;
            }
        }

        static readonly Zone Local = new SysZone(TimeZoneInfo.Local);
        static ReadOnlyCollection<TimeZoneInfo> systemZones;

        static ReadOnlyCollection<TimeZoneInfo> SystemZones()
        {
            if (systemZones == null)
            {
                try { systemZones = TimeZoneInfo.GetSystemTimeZones(); }
                catch (Exception) { systemZones = new ReadOnlyCollection<TimeZoneInfo>(new List<TimeZoneInfo>()); }
            }
            return systemZones;
        }

        static SysZone Sys(string id)
        {
            // Backslash would open a registry subkey
            if (id.IndexOf('\0') >= 0 || id.IndexOf('\\') >= 0) return null;
            try { return new SysZone(TimeZoneInfo.FindSystemTimeZoneById(id)); }
            catch (Exception) { return null; }
        }

        static bool UtcPrefix(string id, out TimeSpan off)
        {
            off = TimeSpan.Zero;
            var m = Regex.Match(id, @"^\(?(?:UTC|GMT)\s*([+-])(\d{1,2})(?::?(\d{2}))?\)?");
            if (!m.Success) return Regex.IsMatch(id, @"^\(?(?:UTC|GMT)\)?(\s|$)");
            int h = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int mi = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            if (h > 14 || mi > 59) return false;
            off = new TimeSpan(h, mi, 0);
            if (m.Groups[1].Value == "-") off = -off;
            return true;
        }

        [DllImport("icu.dll", EntryPoint = "ucal_getWindowsTimeZoneID", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern int ucal_getWindowsTimeZoneID(string id, int len, [Out] char[] winid, int winidCapacity, ref int status);

        static bool icuGone;

        // IANA to Windows id, or null
        public static string WindowsId(string iana)
        {
            if (icuGone || iana.Length == 0 || iana.Length > 64 || iana.IndexOf(' ') >= 0) return null;
            try
            {
                var buf = new char[128];
                int status = 0;
                int n = ucal_getWindowsTimeZoneID(iana, iana.Length, buf, buf.Length, ref status);
                return status > 0 || n <= 0 || n > buf.Length ? null : new string(buf, 0, n);
            }
            catch (DllNotFoundException) { icuGone = true; }
            catch (EntryPointNotFoundException) { icuGone = true; }
            catch (BadImageFormatException) { icuGone = true; }
            return null;
        }

        abstract class Zone
        {
            public abstract DateTime ToUtc(DateTime wall);
            public abstract TimeSpan Offset(DateTime utc);
            public DateTime ToWall(DateTime utc) { return DateTime.SpecifyKind(utc + Offset(utc), DateTimeKind.Unspecified); }
        }

        sealed class SysZone : Zone
        {
            readonly TimeZoneInfo tz;
            public SysZone(TimeZoneInfo tz) { this.tz = tz; }

            public override TimeSpan Offset(DateTime utc)
            {
                try { return tz.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)); }
                catch (ArgumentException) { return tz.BaseUtcOffset; }
            }

            public override DateTime ToUtc(DateTime wall)
            {
                var w = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
                try
                {
                    // Gap: the offset before it (RFC 5545)
                    if (tz.IsInvalidTime(w)) return DateTime.SpecifyKind(w - tz.GetUtcOffset(w.AddHours(-6)), DateTimeKind.Utc);
                    // Overlap: the first occurrence
                    if (tz.IsAmbiguousTime(w))
                    {
                        var max = TimeSpan.MinValue;
                        foreach (var o in tz.GetAmbiguousTimeOffsets(w)) if (o > max) max = o;
                        return DateTime.SpecifyKind(w - max, DateTimeKind.Utc);
                    }
                    return DateTime.SpecifyKind(w - tz.GetUtcOffset(w), DateTimeKind.Utc);
                }
                catch (ArgumentException) { return DateTime.SpecifyKind(w - tz.BaseUtcOffset, DateTimeKind.Utc); }
            }
        }

        sealed class FixedZone : Zone
        {
            readonly TimeSpan off;
            public FixedZone(TimeSpan off) { this.off = off; }
            public override TimeSpan Offset(DateTime utc) { return off; }
            public override DateTime ToUtc(DateTime wall) { return DateTime.SpecifyKind(wall - off, DateTimeKind.Utc); }
        }

        // Offsets from the feed's own VTIMEZONE
        sealed class FeedZone : Zone
        {
            class Obs
            {
                public DateTime Start;
                public TimeSpan From, To;
                public Rule Rule;
                public Stamp Until;
            }

            // Yearly rules, expanded per year on demand
            readonly List<Obs> rules = new List<Obs>();
            // One-off onsets and RDATEs, sorted once
            readonly List<KeyValuePair<DateTime, TimeSpan>> fixedOnsets = new List<KeyValuePair<DateTime, TimeSpan>>();
            readonly Dictionary<int, List<KeyValuePair<DateTime, TimeSpan>>> years = new Dictionary<int, List<KeyValuePair<DateTime, TimeSpan>>>();
            TimeSpan first;
            bool valid;

            public bool Valid { get { return valid; } }

            public FeedZone(Comp vtz, Ctx ctx)
            {
                var earliest = DateTime.MaxValue;
                foreach (var k in vtz.Kids)
                {
                    if (k.Name != "STANDARD" && k.Name != "DAYLIGHT") continue;
                    DateTime start;
                    bool date, utc;
                    TimeSpan from, to;
                    var sp = k.Get("DTSTART");
                    if (sp == null || !ParseStamp(sp.Value, null, out start, out date, out utc)) continue;
                    if (!Offset(k.Text("TZOFFSETFROM"), out from) || !Offset(k.Text("TZOFFSETTO"), out to)) continue;
                    valid = true;
                    if (start < earliest) { earliest = start; first = from; }
                    var rr = k.Get("RRULE");
                    var rule = rr != null ? Rule.Parse(rr.Value) : null;
                    // Real zones change at most yearly
                    if (rule != null && !rule.Complex && rule.Freq == 4 && rules.Count < 24)
                    {
                        var o = new Obs { Start = start, From = from, To = to, Rule = rule };
                        if (rule.Until != null) o.Until = ctx.Time(rule.Until, null, null);
                        rules.Add(o);
                    }
                    else if (fixedOnsets.Count < 2000) fixedOnsets.Add(new KeyValuePair<DateTime, TimeSpan>(start - from, to));
                    foreach (var p in k.Props)
                    {
                        if (p.Name != "RDATE") continue;
                        foreach (var raw in p.Value.Split(','))
                        {
                            DateTime d;
                            if (fixedOnsets.Count < 2000 && ParseStamp(raw, p.Type, out d, out date, out utc)) fixedOnsets.Add(new KeyValuePair<DateTime, TimeSpan>(d - from, to));
                        }
                    }
                }
                fixedOnsets.Sort((a, b) => a.Key.CompareTo(b.Key));
            }

            static bool Offset(string v, out TimeSpan off)
            {
                off = TimeSpan.Zero;
                var m = Regex.Match(v, @"^([+-])(\d{2})(\d{2})(\d{2})?$");
                if (!m.Success) return false;
                int h = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), mi = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                int s = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
                if (h > 23 || mi > 59 || s > 59) return false;
                off = new TimeSpan(h, mi, s);
                if (m.Groups[1].Value == "-") off = -off;
                return true;
            }

            // Rule onsets in UTC around a year
            List<KeyValuePair<DateTime, TimeSpan>> Onsets(int year)
            {
                List<KeyValuePair<DateTime, TimeSpan>> list;
                if (years.TryGetValue(year, out list)) return list;
                if (years.Count >= 16) years.Clear();
                list = new List<KeyValuePair<DateTime, TimeSpan>>();
                var from = new DateTime(Math.Max(1601, year - 1), 1, 1);
                var to = new DateTime(Math.Min(9000, year + 1), 12, 31, 23, 59, 59);
                // Own budget: zones never starve events
                var budget = new Budget { Left = 500 };
                foreach (var o in rules)
                {
                    var ob = o;
                    Func<DateTime, bool> past = null;
                    if (o.Until != null)
                    {
                        var limit = o.Until.Utc ? o.Until.Wall : o.Until.Wall - ob.From;
                        past = w => w - ob.From > limit;
                    }
                    int n = 0;
                    foreach (var w in Occurrences(o.Rule, o.Start, from, to, past, budget))
                        if (++n <= 8) list.Add(new KeyValuePair<DateTime, TimeSpan>(w - o.From, o.To));
                }
                list.Sort((a, b) => a.Key.CompareTo(b.Key));
                years[year] = list;
                return list;
            }

            public override TimeSpan Offset(DateTime utc)
            {
                var off = first;
                var at = DateTime.MinValue;
                int i = Last(fixedOnsets, utc);
                if (i >= 0) { at = fixedOnsets[i].Key; off = fixedOnsets[i].Value; }
                var list = Onsets(utc.Year);
                int j = Last(list, utc);
                if (j >= 0 && list[j].Key >= at) off = list[j].Value;
                return off;
            }

            // Last onset at or before t
            static int Last(List<KeyValuePair<DateTime, TimeSpan>> list, DateTime t)
            {
                int lo = 0, hi = list.Count - 1, found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (list[mid].Key <= t) { found = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                return found;
            }

            public override DateTime ToUtc(DateTime wall)
            {
                var guess = DateTime.SpecifyKind(wall, DateTimeKind.Utc);
                var set = new List<TimeSpan> { Offset(guess.AddHours(-26)), Offset(guess), Offset(guess.AddHours(26)) };
                bool found = false;
                TimeSpan best = TimeSpan.Zero, low = set[0];
                foreach (var o in set)
                {
                    if (o < low) low = o;
                    if (Offset(guess - o) != o) continue;
                    // Overlap: the earlier instant
                    if (!found || o > best) best = o;
                    found = true;
                }
                return DateTime.SpecifyKind(guess - (found ? best : low), DateTimeKind.Utc);
            }
        }

        class Rule
        {
            // DowMask: weekdays in BYDAY, one bit each
            public int Freq, Interval = 1, Count = -1, DowMask;
            public string Until;
            public List<int> ByMonth, ByMonthDay, BySetPos;
            public List<int[]> ByDay;
            public DayOfWeek Wkst = DayOfWeek.Monday;
            public bool Complex;

            public static Rule Parse(string text)
            {
                var r = new Rule();
                foreach (var part in text.Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = part.Substring(0, eq).Trim().ToUpperInvariant(), v = part.Substring(eq + 1).Trim().ToUpperInvariant();
                    int n;
                    switch (k)
                    {
                        case "FREQ": r.Freq = v == "DAILY" ? 1 : v == "WEEKLY" ? 2 : v == "MONTHLY" ? 3 : v == "YEARLY" ? 4 : -1; break;
                        case "INTERVAL":
                            if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0) r.Interval = Math.Min(n, 1000);
                            else r.Complex = true;
                            break;
                        case "COUNT":
                            if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > 0) r.Count = Math.Min(n, 1000000);
                            else r.Complex = true;
                            break;
                        case "UNTIL": r.Until = v; break;
                        case "BYMONTH": r.ByMonth = Ints(v, 1, 12, false, r); break;
                        case "BYMONTHDAY": r.ByMonthDay = Ints(v, 1, 31, true, r); break;
                        case "BYSETPOS": r.BySetPos = Ints(v, 1, 366, true, r); break;
                        case "BYDAY": r.ByDay = Days(v, r); break;
                        case "WKST":
                            int d = Dow(v);
                            if (d >= 0) r.Wkst = (DayOfWeek)d;
                            break;
                        case "BYWEEKNO": case "BYYEARDAY": case "BYHOUR": case "BYMINUTE": case "BYSECOND": case "RSCALE": case "SKIP":
                            r.Complex = true;
                            break;
                    }
                }
                if (r.Freq <= 0 || (r.Count > 0 && r.Until != null)) r.Complex = true;
                return r;
            }

            static List<int> Ints(string v, int min, int max, bool negative, Rule r)
            {
                var list = new List<int>();
                var parts = v.Split(',');
                if (parts.Length > MaxRuleParts) { r.Complex = true; return list; }
                foreach (var part in parts)
                {
                    int n;
                    if (!int.TryParse(part.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) { r.Complex = true; continue; }
                    int a = Math.Abs(n);
                    if (a < min || a > max || (n < 0 && !negative)) { r.Complex = true; continue; }
                    if (!list.Contains(n)) list.Add(n);
                }
                if (list.Count == 0) r.Complex = true;
                return list;
            }

            static List<int[]> Days(string v, Rule r)
            {
                var list = new List<int[]>();
                var parts = v.Split(',');
                if (parts.Length > MaxRuleParts) { r.Complex = true; return list; }
                foreach (var raw in parts)
                {
                    string s = raw.Trim();
                    int dow = s.Length >= 2 ? Dow(s.Substring(s.Length - 2)) : -1;
                    string num = s.Length >= 2 ? s.Substring(0, s.Length - 2) : "";
                    int ord = 0;
                    if (dow < 0 || (num.Length > 0 && (!int.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ord) || ord == 0 || Math.Abs(ord) > 53)))
                    {
                        r.Complex = true;
                        continue;
                    }
                    r.DowMask |= 1 << dow;
                    if (!list.Exists(x => x[0] == ord && x[1] == dow)) list.Add(new[] { ord, dow });
                }
                if (list.Count == 0) r.Complex = true;
                return list;
            }
        }
    }
}
