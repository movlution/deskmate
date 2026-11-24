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
        static int Dow(string s)
        {
            switch (s)
            {
                case "SU": return 0;
                case "MO": return 1;
                case "TU": return 2;
                case "WE": return 3;
                case "TH": return 4;
                case "FR": return 5;
                case "SA": return 6;
            }
            return -1;
        }

        // Starts in [from, to], DTSTART first
        static List<DateTime> Occurrences(Rule r, DateTime s, DateTime from, DateTime to, Func<DateTime, bool> past, Budget b)
        {
            var list = new List<DateTime>();
            if (s >= from && s <= to) list.Add(s);
            if (r.Count == 1 || (past != null && past(s))) return list;
            int count = 1;
            long k = 0;
            // No COUNT: jump close to the window
            if (r.Count < 0 && from > s) k = Math.Max(0, Periods(r, s, from) / r.Interval - 1);
            var cands = new List<DateTime>();
            for (; ; k++)
            {
                if (--b.Left <= 0) { b.Hit = true; break; }
                DateTime p;
                if (!PeriodStart(r, s, k, out p) || p > to) break;
                cands.Clear();
                Candidates(r, s, p, cands);
                if (cands.Count == 0) continue;
                b.Left -= cands.Count;
                cands.Sort();
                Dedupe(cands);
                if (r.BySetPos != null) SetPos(cands, r.BySetPos);
                foreach (var c in cands)
                {
                    if (c <= s) continue;
                    if (c > to || (past != null && past(c))) return list;
                    if (r.Count > 0 && ++count > r.Count) return list;
                    if (c < from) continue;
                    list.Add(c);
                    if (list.Count >= MaxPerEvent) { b.Hit = true; return list; }
                }
            }
            return list;
        }

        static DateTime WeekStart(DateTime d, DayOfWeek wkst)
        {
            return d.Date.AddDays(-(((int)d.DayOfWeek - (int)wkst + 7) % 7));
        }

        static long Periods(Rule r, DateTime s, DateTime w)
        {
            switch (r.Freq)
            {
                case 1: return (long)(w.Date - s.Date).TotalDays;
                case 2: return (long)(WeekStart(w, r.Wkst) - WeekStart(s, r.Wkst)).TotalDays / 7;
                case 3: return (w.Year - s.Year) * 12L + w.Month - s.Month;
                default: return w.Year - s.Year;
            }
        }

        static bool PeriodStart(Rule r, DateTime s, long k, out DateTime p)
        {
            p = DateTime.MinValue;
            long n = k * r.Interval;
            try
            {
                switch (r.Freq)
                {
                    case 1: if (n > 2500000) return false; p = s.Date.AddDays(n); return true;
                    case 2: if (n > 400000) return false; p = WeekStart(s, r.Wkst).AddDays(7 * n); return true;
                    case 3: if (n > 90000) return false; p = new DateTime(s.Year, s.Month, 1).AddMonths((int)n); return true;
                    default: if (n > 7000) return false; p = new DateTime(s.Year, 1, 1).AddYears((int)n); return true;
                }
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        static bool HasDow(Rule r, DayOfWeek d) { return (r.DowMask & (1 << (int)d)) != 0; }

        static void Candidates(Rule r, DateTime s, DateTime p, List<DateTime> c)
        {
            var tod = s.TimeOfDay;
            switch (r.Freq)
            {
                case 1:
                    if ((r.ByMonth == null || r.ByMonth.Contains(p.Month)) && MonthDayOk(r, p) && (r.ByDay == null || HasDow(r, p.DayOfWeek))) c.Add(p + tod);
                    break;
                case 2:
                    for (int i = 0; i < 7; i++)
                    {
                        var d = p.AddDays(i);
                        if (r.ByDay != null ? !HasDow(r, d.DayOfWeek) : d.DayOfWeek != s.DayOfWeek) continue;
                        if (r.ByMonth != null && !r.ByMonth.Contains(d.Month)) continue;
                        c.Add(d + tod);
                    }
                    break;
                case 3:
                    if (r.ByMonth == null || r.ByMonth.Contains(p.Month)) MonthDays(r, s, p.Year, p.Month, c);
                    break;
                default:
                    if (r.ByMonth != null) foreach (int m in r.ByMonth) MonthDays(r, s, p.Year, m, c);
                    else if (r.ByMonthDay != null) for (int m = 1; m <= 12; m++) MonthDays(r, s, p.Year, m, c);
                    else if (r.ByDay != null) YearDays(r, p.Year, tod, c);
                    else MonthDays(r, s, p.Year, s.Month, c);
                    break;
            }
        }

        static bool MonthDayOk(Rule r, DateTime d)
        {
            if (r.ByMonthDay == null) return true;
            int dim = DateTime.DaysInMonth(d.Year, d.Month);
            foreach (int md in r.ByMonthDay) if ((md > 0 ? md : dim + md + 1) == d.Day) return true;
            return false;
        }

        // Days of one month; invalid dates skipped
        static void MonthDays(Rule r, DateTime s, int y, int m, List<DateTime> c)
        {
            int dim = DateTime.DaysInMonth(y, m);
            var tod = s.TimeOfDay;
            if (r.ByMonthDay != null)
            {
                foreach (int md in r.ByMonthDay)
                {
                    int day = md > 0 ? md : dim + md + 1;
                    if (day < 1 || day > dim) continue;
                    var d = new DateTime(y, m, day);
                    if (r.ByDay != null && !HasDow(r, d.DayOfWeek)) continue;
                    c.Add(d + tod);
                }
                return;
            }
            if (r.ByDay != null)
            {
                var first = new DateTime(y, m, 1);
                foreach (var bd in r.ByDay)
                {
                    int off = (bd[1] - (int)first.DayOfWeek + 7) % 7;
                    if (bd[0] == 0 || r.Freq < 3)
                    {
                        for (int day = 1 + off; day <= dim; day += 7) c.Add(new DateTime(y, m, day) + tod);
                    }
                    else if (bd[0] > 0)
                    {
                        int day = 1 + off + (bd[0] - 1) * 7;
                        if (day <= dim) c.Add(new DateTime(y, m, day) + tod);
                    }
                    else
                    {
                        int back = ((int)new DateTime(y, m, dim).DayOfWeek - bd[1] + 7) % 7;
                        int day = dim - back + (bd[0] + 1) * 7;
                        if (day >= 1) c.Add(new DateTime(y, m, day) + tod);
                    }
                }
                return;
            }
            if (s.Day <= dim) c.Add(new DateTime(y, m, s.Day) + tod);
        }

        // YEARLY BYDAY only: counted per year
        static void YearDays(Rule r, int y, TimeSpan tod, List<DateTime> c)
        {
            var jan1 = new DateTime(y, 1, 1);
            int diy = DateTime.IsLeapYear(y) ? 366 : 365;
            foreach (var bd in r.ByDay)
            {
                int off = (bd[1] - (int)jan1.DayOfWeek + 7) % 7;
                if (bd[0] == 0) for (int d = off; d < diy; d += 7) c.Add(jan1.AddDays(d) + tod);
                else if (bd[0] > 0)
                {
                    int d = off + (bd[0] - 1) * 7;
                    if (d < diy) c.Add(jan1.AddDays(d) + tod);
                }
                else
                {
                    int back = ((int)new DateTime(y, 12, 31).DayOfWeek - bd[1] + 7) % 7;
                    int d = diy - 1 - back + (bd[0] + 1) * 7;
                    if (d >= 0) c.Add(jan1.AddDays(d) + tod);
                }
            }
        }

        static void Dedupe(List<DateTime> sorted)
        {
            for (int i = sorted.Count - 1; i > 0; i--) if (sorted[i] == sorted[i - 1]) sorted.RemoveAt(i);
        }

        static void SetPos(List<DateTime> cands, List<int> pos)
        {
            var pick = new List<DateTime>();
            foreach (int sp in pos)
            {
                int i = sp > 0 ? sp - 1 : cands.Count + sp;
                if (i >= 0 && i < cands.Count) pick.Add(cands[i]);
            }
            pick.Sort();
            Dedupe(pick);
            cands.Clear();
            cands.AddRange(pick);
        }

        // UNTIL in the same terms as DTSTART
        static Func<DateTime, bool> UntilTest(Rule r, Stamp start, Ctx ctx)
        {
            if (r.Until == null) return null;
            var u = ctx.Time(r.Until, null, null);
            if (u == null) return null;
            if (start.Date || u.Date) { var day = u.Wall.Date; return w => w.Date > day; }
            if (u.Utc)
            {
                var limit = u.Wall;
                if (start.Utc) return w => w > limit;
                var zone = start.Zone ?? Local;
                return w => zone.ToUtc(w) > limit;
            }
            var wall = u.Wall;
            return w => w > wall;
        }

        static void Event(Comp ev, List<Comp> overrides, bool single, Ctx ctx, DateTime fromUtc, DateTime toUtc)
        {
            var res = ctx.Res;
            var start = ctx.Time(ev.Get("DTSTART"));
            if (start == null) { res.Broken++; return; }
            bool allDay = start.Date;
            int days = 1;
            var dur = TimeSpan.Zero;
            TimeSpan d;
            var endP = ev.Get("DTEND");
            var durP = ev.Get("DURATION");
            if (allDay)
            {
                var e = ctx.Time(endP);
                if (e != null) days = (int)(e.Wall.Date - start.Wall.Date).TotalDays;
                else if (durP != null && Duration(durP.Value, out d)) days = (int)Math.Ceiling(d.TotalDays);
                days = Math.Max(1, Math.Min(days, 366));
            }
            else
            {
                var e = ctx.Time(endP);
                if (e != null) dur = ctx.Utc(e) - ctx.Utc(start);
                else if (durP != null && Duration(durP.Value, out d)) dur = d;
                if (dur < TimeSpan.Zero) dur = TimeSpan.Zero;
                if (dur > TimeSpan.FromDays(31)) dur = TimeSpan.FromDays(31);
            }
            var fromDate = fromUtc.ToLocalTime().Date;
            var toDate = toUtc.ToLocalTime().Date.AddDays(1);
            // Instance keys: UTC instants, or dates
            var keys = new List<DateTime>();
            Rule rule = null;
            var rr = single ? null : ev.Get("RRULE");
            if (rr != null)
            {
                rule = Rule.Parse(rr.Value);
                if (rule.Complex) { res.Complex++; rule = null; }
            }
            if (rule == null) keys.Add(allDay ? start.Wall.Date : ctx.Utc(start));
            else
            {
                DateTime from, to;
                if (allDay) { from = fromDate.AddDays(-days); to = toDate; }
                else if (start.Utc) { from = fromUtc - dur - TimeSpan.FromDays(1); to = toUtc.AddDays(1); }
                else { from = start.Zone.ToWall(fromUtc) - dur - TimeSpan.FromDays(1); to = start.Zone.ToWall(toUtc).AddDays(1); }
                var past = UntilTest(rule, start, ctx);
                foreach (var w in Occurrences(rule, start.Wall, from, to, past, ctx.Budget))
                    keys.Add(allDay ? w.Date : start.Utc ? DateTime.SpecifyKind(w, DateTimeKind.Utc) : start.Zone.ToUtc(w));
            }
            // Exclusions gathered first, applied once
            var gone = new HashSet<DateTime>();
            var goneDays = new HashSet<DateTime>();
            if (!single)
            {
                var have = new HashSet<DateTime>(keys);
                int values = 0;
                foreach (var p in ev.Props)
                {
                    if ((p.Name != "RDATE" && p.Name != "EXDATE") || values > MaxDates) continue;
                    foreach (var raw in p.Value.Split(','))
                    {
                        if (++values > MaxDates) { res.Capped = true; break; }
                        string v = raw;
                        int slash = v.IndexOf('/');
                        if (slash > 0) v = v.Substring(0, slash);
                        var t = ctx.Time(v, p.Type, p.Tzid);
                        if (t == null) continue;
                        if (p.Name == "EXDATE") { Drop(gone, goneDays, t, allDay, ctx); continue; }
                        var add = allDay ? t.Wall.Date : !t.Date ? ctx.Utc(t)
                                : start.Utc ? DateTime.SpecifyKind(t.Wall + start.Wall.TimeOfDay, DateTimeKind.Utc) : start.Zone.ToUtc(t.Wall + start.Wall.TimeOfDay);
                        if (keys.Count < MaxPerEvent && have.Add(add)) keys.Add(add);
                    }
                }
            }
            if (overrides != null)
                foreach (var o in overrides)
                {
                    var rid = ctx.Time(o.Get("RECURRENCE-ID"));
                    if (rid != null) Drop(gone, goneDays, rid, allDay, ctx);
                }
            if (gone.Count > 0 || goneDays.Count > 0)
            {
                var zone = start.Zone ?? Local;
                keys.RemoveAll(k => gone.Contains(k) || (goneDays.Count > 0 && goneDays.Contains(allDay || start.Utc ? k.Date : zone.ToWall(k).Date)));
            }
            bool cancelled = ev.Text("STATUS").Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
            string uid = ev.Text("UID"), summary = ev.Text("SUMMARY"), location = ev.Text("LOCATION");
            string url = ev.Get("URL") != null ? ev.Get("URL").Value.Trim() : "";
            string meet = ev.Get("X-GOOGLE-CONFERENCE") != null ? ev.Get("X-GOOGLE-CONFERENCE").Value.Trim()
                        : ev.Get("X-MICROSOFT-SKYPETEAMSMEETINGURL") != null ? ev.Get("X-MICROSOFT-SKYPETEAMSMEETINGURL").Value.Trim() : "";
            keys.Sort();
            foreach (var k in keys)
            {
                var item = new IcsItem { Uid = uid, Summary = summary, Location = location, Url = url, Meet = meet, AllDay = allDay, Cancelled = cancelled };
                if (allDay)
                {
                    item.Start = k;
                    item.End = k.AddDays(days);
                    if (item.End <= fromDate || item.Start >= toDate) continue;
                }
                else
                {
                    item.Start = k;
                    item.End = k + dur;
                    if (k >= toUtc || item.End < fromUtc || (dur == TimeSpan.Zero && k < fromUtc)) continue;
                }
                if (res.Items.Count >= MaxItems) { res.Capped = true; return; }
                res.Items.Add(item);
            }
            if (overrides != null)
                foreach (var o in overrides)
                {
                    int before = res.Items.Count;
                    Event(o, null, true, ctx, fromUtc, toUtc);
                    if (cancelled) for (int i = before; i < res.Items.Count; i++) res.Items[i].Cancelled = true;
                }
        }

        // EXDATE or override: instant or day
        static void Drop(HashSet<DateTime> gone, HashSet<DateTime> days, Stamp t, bool allDay, Ctx ctx)
        {
            if (allDay || t.Date) days.Add(t.Wall.Date);
            else gone.Add(ctx.Utc(t));
        }

        static void Todo(Comp t, Ctx ctx, DateTime fromUtc, DateTime toUtc)
        {
            string status = t.Text("STATUS").ToUpperInvariant();
            if (status == "COMPLETED" || status == "CANCELLED" || t.Get("COMPLETED") != null || t.Text("PERCENT-COMPLETE") == "100") return;
            var due = ctx.Time(t.Get("DUE")) ?? ctx.Time(t.Get("DTSTART"));
            if (due == null) return;
            var item = new IcsItem { Todo = true, AllDay = due.Date, Uid = t.Text("UID"), Summary = t.Text("SUMMARY"), Location = t.Text("LOCATION") };
            var url = t.Get("URL");
            if (url != null) item.Url = url.Value.Trim();
            // Overdue up to two weeks
            if (due.Date)
            {
                item.Start = item.End = due.Wall.Date;
                if (item.Start < fromUtc.ToLocalTime().Date.AddDays(-14) || item.Start > toUtc.ToLocalTime().Date) return;
            }
            else
            {
                item.Start = item.End = ctx.Utc(due);
                if (item.Start < fromUtc.AddDays(-14) || item.Start > toUtc) return;
            }
            ctx.Res.Items.Add(item);
        }
    }
}
