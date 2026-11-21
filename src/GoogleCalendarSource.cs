using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Deskmate
{
    partial class GoogleCalendarSource : Source
    {
        const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        const string CalendarListEndpoint = "https://www.googleapis.com/calendar/v3/users/me/calendarList?minAccessRole=reader";
        const string EventsEndpoint = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
        const int MaxCalendars = 15;
        const int MaxPagesPerCalendar = 10;
        volatile bool connected;
        public string Email = "";

        public GoogleCalendarSource()
        {
            Key = "google";
            Name = "Google Calendar";
            Every = TimeSpan.FromMinutes(15);
        }

        public bool Connected { get { return connected; } }
        public override bool GivesTasks { get { return false; } }
        public override bool GivesEvents { get { return true; } }

        public void Reload()
        {
            var v = GoogleCalVault.Load();
            connected = v.HasRefreshToken;
            Email = connected ? (v.Email ?? "") : "";
        }

        public override object Snapshot()
        {
            return Version;
        }

        public override bool Absorb(SourceResult r)
        {
            var email = r != null ? r.Extra as string : null;
            if (email != null) Email = email;
            return false;
        }

        public override SourceResult Fetch(object snap, DateTime nowUtc)
        {
            if (Gone) return null;
            return Pull(nowUtc);
        }

        public SourceResult Pull(DateTime now)
        {
            DateTime nowUtc = now.Kind == DateTimeKind.Utc ? now : (now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : DateTime.SpecifyKind(now, DateTimeKind.Utc));

            var vault = GoogleCalVault.Load();
            if (!vault.HasRefreshToken)
            {
                return new SourceResult { Events = new List<ClassSlot>(), Extra = "" };
            }

            bool refreshed = false;
            if (string.IsNullOrEmpty(vault.AccessToken) || vault.ExpiresUtc <= nowUtc.AddMinutes(2))
            {
                SourceResult refreshErr = RefreshAccessToken(vault);
                if (refreshErr != null) return refreshErr;
                refreshed = true;
            }

            if (Gone) return null;

            List<string> calendarIds = null;
            FetchResult calListRes = SendEventsRequest(vault.AccessToken, CalendarListEndpoint);
            if (calListRes.Status == 401 && !refreshed && !Gone)
            {
                SourceResult refreshErr = RefreshAccessToken(vault);
                if (refreshErr != null) return refreshErr;
                refreshed = true;
                calListRes = SendEventsRequest(vault.AccessToken, CalendarListEndpoint);
            }
            if (Gone) return null;

            if (calListRes.Ok)
            {
                object calRoot = null;
                try { calRoot = Json.Parse(calListRes.Text); }
                catch (Exception) { calRoot = null; }
                if (calRoot != null)
                    calendarIds = ParseCalendarIds(calRoot);
            }
            if (calendarIds == null || calendarIds.Count == 0)
            {
                calendarIds = new List<string> { "primary" };
            }

            string timeMin = nowUtc.AddDays(-60).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string timeMax = nowUtc.AddDays(180).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string queryBase = "?timeMin=" + Uri.EscapeDataString(timeMin)
                + "&timeMax=" + Uri.EscapeDataString(timeMax)
                + "&singleEvents=true"
                + "&orderBy=startTime"
                + "&maxResults=250";

            var events = new List<ClassSlot>();
            bool anyOk = false;
            bool hadJsonError = false;
            FetchResult lastError = null;

            int calLimit = Math.Min(calendarIds.Count, MaxCalendars);
            for (int ci = 0; ci < calLimit; ci++)
            {
                if (Gone) return null;
                string calId = calendarIds[ci];
                string baseUrl = "https://www.googleapis.com/calendar/v3/calendars/"
                    + Uri.EscapeDataString(calId)
                    + "/events" + queryBase;

                string pageToken = null;
                for (int page = 0; page < MaxPagesPerCalendar; page++)
                {
                    if (Gone) return null;
                    string url = string.IsNullOrEmpty(pageToken)
                        ? baseUrl
                        : baseUrl + "&pageToken=" + Uri.EscapeDataString(pageToken);

                    FetchResult r = SendEventsRequest(vault.AccessToken, url);
                    if (r.Status == 401 && !refreshed && !Gone)
                    {
                        SourceResult refreshErr = RefreshAccessToken(vault);
                        if (refreshErr != null) return refreshErr;
                        refreshed = true;
                        r = SendEventsRequest(vault.AccessToken, url);
                    }
                    if (Gone) return null;

                    if (!r.Ok)
                    {
                        lastError = r;
                        break;
                    }

                    object root;
                    try { root = Json.Parse(r.Text); }
                    catch (Exception) { root = null; }
                    if (root == null)
                    {
                        hadJsonError = true;
                        break;
                    }

                    anyOk = true;
                    events.AddRange(ParseEvents(root));
                    pageToken = Json.Str(root, "nextPageToken");
                    if (string.IsNullOrEmpty(pageToken)) break;
                }
            }

            if (!anyOk)
            {
                if (lastError != null) return SourceResult.Http("Google Calendar", lastError);
                if (hadJsonError) return SourceResult.Fail(Lang.T("Gagal memuat"), "json", false);
            }

            events.Sort(CompareEventsByStart);
            return new SourceResult { Events = events, Extra = vault.Email ?? "" };
        }

        static List<string> ParseCalendarIds(object root)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Json.Arr(root, "items"))
            {
                string id = Json.Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;
                if (seen.Add(id))
                {
                    ids.Add(id);
                    if (ids.Count >= MaxCalendars) break;
                }
            }
            return ids;
        }

        static int CompareEventsByStart(ClassSlot a, ClassSlot b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            return a.Start.CompareTo(b.Start);
        }

        public string CreateEvent(string title, DateTime startLocal, DateTime endLocal, string location, bool allDay, out string error)
        {
            ClassSlot slot = CreateEventSlot(title, startLocal, endLocal, location, allDay, out error);
            return slot != null ? slot.Id : null;
        }

        public ClassSlot CreateEventSlot(string title, DateTime startLocal, DateTime endLocal, string location, bool allDay, out string error)
        {
            return SendEventWrite("POST", EventsEndpoint, title, startLocal, endLocal, location, allDay, out error);
        }

        public ClassSlot UpdateEventSlot(string eventId, string title, DateTime startLocal, DateTime endLocal, string location, bool allDay, out string error)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                error = Lang.T("Gagal tersimpan");
                return null;
            }
            string url = EventsEndpoint + "/" + Uri.EscapeDataString(eventId);
            return SendEventWrite("PATCH", url, title, startLocal, endLocal, location, allDay, out error);
        }

        ClassSlot SendEventWrite(string method, string url, string title, DateTime startLocal, DateTime endLocal, string location, bool allDay, out string error)
        {
            GoogleCalVault vault;
            bool refreshed;
            if (!EnsureAccessToken(out vault, out refreshed, out error)) return null;

            DateTime slotStart, slotEnd;
            Dictionary<string, object> startObj;
            Dictionary<string, object> endObj;
            if (allDay)
            {
                DateTime startDay = startLocal.Date;
                DateTime endDay = endLocal.Date <= startDay ? startDay.AddDays(1) : endLocal.Date;
                slotStart = DateTime.SpecifyKind(startDay, DateTimeKind.Local);
                slotEnd = DateTime.SpecifyKind(endDay, DateTimeKind.Local);
                startObj = new Dictionary<string, object> { { "date", startDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } };
                endObj = new Dictionary<string, object> { { "date", endDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } };
            }
            else
            {
                DateTime s = startLocal.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(startLocal, DateTimeKind.Local) : startLocal;
                DateTime e = endLocal.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(endLocal, DateTimeKind.Local) : endLocal;
                if (s.Kind == DateTimeKind.Utc) s = s.ToLocalTime();
                if (e.Kind == DateTimeKind.Utc) e = e.ToLocalTime();
                if (e < s) e = s;
                slotStart = DateTime.SpecifyKind(s, DateTimeKind.Local);
                slotEnd = DateTime.SpecifyKind(e, DateTimeKind.Local);
                startObj = new Dictionary<string, object> { { "dateTime", slotStart.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture) } };
                endObj = new Dictionary<string, object> { { "dateTime", slotEnd.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture) } };
            }

            var bodyObj = new Dictionary<string, object>
            {
                { "summary", title ?? "" },
                { "start", startObj },
                { "end", endObj }
            };
            string loc = (location ?? "").Trim();
            if (loc.Length > 0 || method == "PATCH") bodyObj["location"] = loc;

            string body = Json.Encode(bodyObj);
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + vault.AccessToken },
                { "Accept", "application/json" },
                { "Content-Type", "application/json" }
            };

            FetchResult r = Deskmate.Fetch.Send(method, url, headers, body, Deskmate.Fetch.JsonCap, 20000);
            if (r.Status == 401 && !refreshed)
            {
                SourceResult refreshErr = RefreshAccessToken(vault);
                if (refreshErr != null)
                {
                    error = refreshErr.Error ?? Lang.T("Gagal tersimpan");
                    return null;
                }
                headers["Authorization"] = "Bearer " + vault.AccessToken;
                r = Deskmate.Fetch.Send(method, url, headers, body, Deskmate.Fetch.JsonCap, 20000);
            }

            if (r.Status == 403)
            {
                error = Lang.T("Izin tulis Google Calendar belum aktif — putuskan lalu sambungkan ulang di Pengaturan › Tugas & Kalender.");
                return null;
            }
            if (!r.Ok)
            {
                error = SourceResult.Http("Google Calendar", r).Error;
                return null;
            }

            object ev = null;
            try { ev = Json.Parse(r.Text); }
            catch (Exception) { }
            string id = ev != null ? Json.Str(ev, "id") : "";
            if (string.IsNullOrEmpty(id))
            {
                error = Lang.F("Gagal tersimpan di {0}", "Google Calendar");
                return null;
            }

            string cleanTitle = Clean(Json.Str(ev, "summary"), 160);
            if (cleanTitle.Length == 0) cleanTitle = Clean(title, 160);
            if (cleanTitle.Length == 0) cleanTitle = Lang.T("Sibuk");

            string rawLocation = Json.Str(ev, "location");
            if (rawLocation.Length == 0) rawLocation = loc;
            string room = Clean(rawLocation, 80);

            bool online;
            string slotUrl = EventLink(ev, rawLocation, out online);

            error = null;
            return new ClassSlot
            {
                Id = id,
                Title = cleanTitle,
                Room = room,
                Url = slotUrl,
                Online = online,
                AllDay = allDay,
                Source = "google",
                Start = slotStart,
                End = slotEnd
            };
        }

        public bool DeleteEvent(string eventId, out string error)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                error = Lang.T("Gagal tersimpan");
                return false;
            }

            GoogleCalVault vault;
            bool refreshed;
            if (!EnsureAccessToken(out vault, out refreshed, out error)) return false;

            string url = EventsEndpoint + "/" + Uri.EscapeDataString(eventId);
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + vault.AccessToken },
                { "Accept", "application/json" }
            };

            FetchResult r = Deskmate.Fetch.Send("DELETE", url, headers, null, Deskmate.Fetch.JsonCap, 20000);
            if (r.Status == 401 && !refreshed)
            {
                SourceResult refreshErr = RefreshAccessToken(vault);
                if (refreshErr != null)
                {
                    error = refreshErr.Error ?? Lang.T("Gagal tersimpan");
                    return false;
                }
                headers["Authorization"] = "Bearer " + vault.AccessToken;
                r = Deskmate.Fetch.Send("DELETE", url, headers, null, Deskmate.Fetch.JsonCap, 20000);
            }

            if (r.Status == 403)
            {
                error = Lang.T("Izin tulis Google Calendar belum aktif — putuskan lalu sambungkan ulang di Pengaturan › Tugas & Kalender.");
                return false;
            }
            if (!r.Ok)
            {
                error = SourceResult.Http("Google Calendar", r).Error;
                return false;
            }

            error = null;
            return true;
        }

    }
}
