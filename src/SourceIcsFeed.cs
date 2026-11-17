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
    // One iCal feed: schedule or tasks
    class IcsFeed : Source
    {
        static readonly string FilePath = Path.Combine(Settings.Dir, "calendars.vault");
        public string Id = "", Url = "", Kind = "jadwal";
        public bool Todos;

        public string Host
        {
            get
            {
                Uri u;
                return Uri.TryCreate(Url, UriKind.Absolute, out u) ? Clean(u.Host, 80) : "";
            }
        }

        public override bool GivesTasks { get { return Kind == "tugas" || Todos; } }
        public override bool GivesEvents { get { return Kind != "tugas"; } }

        public override object Snapshot() { return new[] { Url, Kind, Name, Key }; }

        // webcal and http become https; else null
        public static string Normalize(string input)
        {
            string s = (input ?? "").Trim();
            if (s.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s.Substring(10);
            else if (s.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s.Substring(9);
            else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s.Substring(7);
            Uri u;
            if (s.Length > 2000 || !Uri.TryCreate(s, UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps || u.Host.Length == 0) return null;
            return s;
        }

        // Task apps publish their tasks as events
        public static string GuessKind(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return "jadwal";
            string h = u.Host.ToLowerInvariant();
            foreach (var app in new[] { "todoist.com", "ticktick.com", "asana.com", "clickup.com" })
                if (h == app || h.EndsWith("." + app)) return "tugas";
            return "jadwal";
        }

        public static string Provider(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return Lang.T("Kalender");
            string h = u.Host.ToLowerInvariant();
            if (h.EndsWith("calendar.google.com")) return "Google Calendar";
            if (h.StartsWith("outlook.")) return "Outlook";
            if (h.EndsWith("icloud.com")) return "iCloud";
            if (h.EndsWith("proton.me")) return "Proton Calendar";
            if (h == "user.fm") return "Fastmail";
            if (h.EndsWith("todoist.com")) return "Todoist";
            if (h.EndsWith("ticktick.com")) return "TickTick";
            return Clean(u.Host, 60);
        }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        public static IcsFeed Make(string id, string url, string name, string kind)
        {
            var feed = new IcsFeed { Id = id, Key = "ics:" + id, Url = url, Kind = kind == "tugas" ? "tugas" : "jadwal" };
            feed.Name = Clean(name, 60);
            if (feed.Name.Length == 0) feed.Name = Provider(url);
            return feed;
        }

        public static List<IcsFeed> LoadAll()
        {
            var list = new List<IcsFeed>();
            object saved;
            try { saved = Json.Parse(Vault.Load(FilePath) ?? ""); }
            catch (Exception ex) { Log.Write("calendars load: " + ex.GetType().Name); return list; }
            foreach (var o in Json.Arr(saved, "feeds"))
            {
                string url = Normalize(Json.Str(o, "url")), id = Json.Str(o, "id");
                if (url == null || id.Length == 0 || list.Exists(x => x.Id == id)) continue;
                var feed = new IcsFeed { Id = id, Key = "ics:" + id, Url = url, Kind = Json.Str(o, "kind") == "tugas" ? "tugas" : "jadwal", Todos = Json.Str(o, "todos") == "True" };
                feed.Name = Clean(Json.Str(o, "name"), 60);
                if (feed.Name.Length == 0) feed.Name = Provider(url);
                list.Add(feed);
            }
            return list;
        }

        public static void SaveAll(List<IcsFeed> feeds)
        {
            if (feeds.Count == 0) { Vault.Delete(FilePath); return; }
            var arr = new List<object>();
            foreach (var f in feeds)
                arr.Add(new Dictionary<string, object> { { "id", f.Id }, { "url", f.Url }, { "name", f.Name }, { "kind", f.Kind }, { "todos", f.Todos } });
            try { Vault.Save(FilePath, Json.Encode(new Dictionary<string, object> { { "feeds", arr.ToArray() } })); }
            catch (Exception ex) { Log.Write("calendars save: " + ex.GetType().Name); }
        }

        public override SourceResult Fetch(object snap, DateTime nowUtc)
        {
            var s = (string[])snap;
            return Pull(s[0], s[1], s[2], s[3], nowUtc);
        }

        // Fetch and expand; also validates links
        public static SourceResult Pull(string url, string kind, string name, string key, DateTime nowUtc)
        {
            var r = Deskmate.Fetch.Send("GET", url, new Dictionary<string, string> { { "Accept", "text/calendar, text/plain;q=0.8, */*;q=0.5" } }, null, Ics.MaxBytes, 30000);
            if (r.Insecure) return SourceResult.Fail(Lang.T("Hanya tautan https"), "insecure", true);
            if (r.Revoked || r.Status == 400 || r.Status == 401 || r.Status == 403 || r.Status == 404 || r.Status == 410)
                return SourceResult.Fail(Lang.T("Tautan kalender tidak berlaku"), "http " + r.Status, true);
            if (r.TooBig) return SourceResult.Fail(Lang.T("Kalender lebih dari 5 MB"), "too big", true);
            if (!r.Ok) return SourceResult.Http(Lang.T("Kalender"), r);
            return Read(r.Data, r.Length, url, kind, name, key);
        }

        // Feed bytes into tasks and events
        public static SourceResult Read(byte[] data, int length, string url, string kind, string name, string key)
        {
            if (!Calendar(data, length)) return SourceResult.Fail(Lang.T("Tautan ini bukan kalender iCal"), "not ics", true);
            var today = DateTime.Now.Date;
            bool asTasks = kind == "tugas";
            var from = (asTasks ? today.AddDays(-14) : today.AddDays(-1)).ToUniversalTime();
            var to = today.AddDays(22).ToUniversalTime();
            var ics = Ics.Parse(data, length, from, to);
            var res = new SourceResult { Name = Clean(ics.Name, 60), Every = ics.Refresh, Tasks = new List<KuliaTask>(), Events = asTasks ? null : new List<ClassSlot>() };
            if (string.IsNullOrEmpty(name)) name = res.Name.Length > 0 ? res.Name : Provider(url);
            Convert(ics, res, asTasks, key, name, today);
            if (!asTasks && res.Tasks.Count == 0) res.Tasks = null;
            res.Extra = ics.Todos > 0;
            if (ics.Capped) Log.Write("source " + key + ": capped");
            return res;
        }

        static bool Calendar(byte[] data, int length)
        {
            int i = 0;
            if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) i = 3;
            while (i < length && (data[i] == 32 || data[i] == 9 || data[i] == 10 || data[i] == 13)) i++;
            const string head = "BEGIN:VCALENDAR";
            if (length - i < head.Length) return false;
            return string.Equals(Encoding.ASCII.GetString(data, i, head.Length), head, StringComparison.OrdinalIgnoreCase);
        }

        static void Convert(IcsResult ics, SourceResult res, bool asTasks, string key, string name, DateTime today)
        {
            var course = Clean(name, 60);
            var ids = new HashSet<string>();
            foreach (var it in ics.Items)
            {
                string title = Clean(it.Summary, 160), room = Clean(it.Location, 80), link = SafeLink(it.Url), meet = SafeLink(it.Meet);
                // Link as location: meeting link
                if (room.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || room.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    if (meet.Length == 0) meet = SafeLink(room);
                    room = "";
                }
                if (it.Todo || asTasks)
                {
                    if (it.Cancelled || res.Tasks.Count >= 300) continue;
                    string id = key + ":" + IdPart(it.Uid) + ":" + it.Start.Ticks;
                    // Hit regions key on ids
                    if (!ids.Add(id)) continue;
                    var due = it.AllDay ? EndOfDay(it.End > it.Start ? it.End.AddDays(-1) : it.Start) : it.Start;
                    res.Tasks.Add(new KuliaTask
                    {
                        Id = id, Title = title.Length > 0 ? title : Lang.T("Tanpa judul"), Course = course,
                        DeadlineUtc = DateTime.SpecifyKind(due, DateTimeKind.Utc), DateOnly = it.AllDay, ReadOnly = true, Source = key, Url = link,
                    });
                    continue;
                }
                if (res.Events.Count >= 1500) continue;
                var slot = new ClassSlot { Title = title.Length > 0 ? title : Lang.T("Sibuk"), Room = room, Url = meet.Length > 0 ? meet : link, Online = meet.Length > 0, Cancelled = it.Cancelled, Source = key };
                if (!it.AllDay)
                {
                    slot.Start = it.Start.ToLocalTime();
                    slot.End = it.End.ToLocalTime();
                    res.Events.Add(slot);
                    continue;
                }
                // One row per all-day date
                for (var d = it.Start.Date; d < it.End.Date; d = d.AddDays(1))
                {
                    if (d < today.AddDays(-1)) continue;
                    if (d > today.AddDays(22)) break;
                    res.Events.Add(new ClassSlot
                    {
                        Title = slot.Title, Room = slot.Room, Url = slot.Url, Online = slot.Online, Cancelled = slot.Cancelled, Source = key, AllDay = true,
                        Start = DateTime.SpecifyKind(d, DateTimeKind.Local), End = DateTime.SpecifyKind(d.AddDays(1), DateTimeKind.Local),
                    });
                }
            }
        }

        // UID as a short, single-line id part
        static string IdPart(string uid)
        {
            foreach (char c in uid) if (c < 0x21 || c > 0x7E) return Json.Sha256(uid).Substring(0, 32);
            return uid.Length > 100 ? Json.Sha256(uid).Substring(0, 32) : uid;
        }

        // Remembers the feed's own name and to-dos
        public override bool Absorb(SourceResult r)
        {
            bool changed = false, todos = r.Extra is bool && (bool)r.Extra;
            if (todos != Todos) { Todos = todos; changed = true; }
            if (!string.IsNullOrEmpty(r.Name) && r.Name != Name) { Name = r.Name; changed = true; }
            return changed;
        }
    }
}
