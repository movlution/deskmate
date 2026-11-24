using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Deskmate
{
    // Indonesian National Holidays via public Google Calendar ICS
    class NationalHolidaysSource : Source
    {
        const string PublicIdUrl = "https://calendar.google.com/calendar/ical/id.indonesian%23holiday%40group.v.calendar.google.com/public/basic.ics";
        public volatile bool Enabled = true;

        public NationalHolidaysSource()
        {
            Key = "holiday";
            Name = "Hari libur nasional";
            Every = TimeSpan.FromHours(12);
        }

        public override bool GivesTasks { get { return false; } }
        public override bool GivesEvents { get { return true; } }
        public override object Snapshot() { return Enabled; }
        public override SourceResult Fetch(object snap, DateTime nowUtc)
        {
            if (!(bool)snap) return new SourceResult { Events = new List<ClassSlot>() };
            return Pull(nowUtc);
        }

        public SourceResult Pull(DateTime now)
        {
            if (!Enabled) return new SourceResult { Events = new List<ClassSlot>() };
            var res = Deskmate.Fetch.Send("GET", PublicIdUrl, null, null, 512 * 1024, 15000);
            if (!res.Ok) return SourceResult.Http(Lang.T("Hari libur nasional"), res);
            var list = ParseHolidayIcs(res.Text, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(365));
            return new SourceResult { Events = list };
        }

        public static List<ClassSlot> ParseHolidayIcs(string icsText, DateTime fromDay, DateTime toDay)
        {
            var list = new List<ClassSlot>();
            if (string.IsNullOrEmpty(icsText)) return list;
            var bytes = Encoding.UTF8.GetBytes(icsText);
            var ics = Ics.Parse(bytes, bytes.Length, fromDay.ToUniversalTime(), toDay.ToUniversalTime());
            foreach (var it in ics.Items)
            {
                if (it.Todo || it.Cancelled) continue;
                var start = it.Start.Date;
                var end = it.End.Date > start ? it.End.Date : start.AddDays(1);
                list.Add(new ClassSlot
                {
                    Title = Clean(it.Summary, 160),
                    Kind = "libur",
                    Source = "holiday",
                    AllDay = true,
                    Start = start,
                    End = end,
                });
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }
    }
}
