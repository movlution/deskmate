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
    partial class GoogleCalendarSource
    {
        bool EnsureAccessToken(out GoogleCalVault vault, out bool refreshed, out string error)
        {
            error = null;
            refreshed = false;
            vault = GoogleCalVault.Load();
            if (!vault.HasRefreshToken)
            {
                error = Lang.T("Belum terhubung");
                return false;
            }
            DateTime nowUtc = DateTime.UtcNow;
            if (string.IsNullOrEmpty(vault.AccessToken) || vault.ExpiresUtc <= nowUtc.AddMinutes(2))
            {
                SourceResult refreshErr = RefreshAccessToken(vault);
                if (refreshErr != null)
                {
                    error = refreshErr.Error ?? Lang.T("Gagal tersimpan");
                    return false;
                }
                refreshed = true;
            }
            return true;
        }

        static FetchResult SendEventsRequest(string accessToken, string url)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + accessToken },
                { "Accept", "application/json" }
            };
            return Deskmate.Fetch.Send("GET", url, headers, null, Deskmate.Fetch.JsonCap, 20000);
        }

        internal static GoogleCalVault EnsureVaultToken(out string error)
        {
            error = null;
            var vault = GoogleCalVault.Load();
            if (!vault.HasRefreshToken)
            {
                error = Lang.T("Belum terhubung");
                return null;
            }
            if (string.IsNullOrEmpty(vault.AccessToken) || DateTime.UtcNow >= vault.ExpiresUtc.AddMinutes(-2))
            {
                SourceResult err = RefreshAccessTokenStatic(vault);
                if (err != null)
                {
                    error = err.Error ?? Lang.T("Gagal memuat");
                    return null;
                }
            }
            return vault;
        }

        internal static SourceResult RefreshAccessTokenStatic(GoogleCalVault vault)
        {
            return new GoogleCalendarSource().RefreshAccessToken(vault);
        }

        SourceResult RefreshAccessToken(GoogleCalVault vault)
        {
            string cid = vault.EffectiveClientId;
            string csec = vault.EffectiveClientSecret;
            if (string.IsNullOrEmpty(cid))
                return SourceResult.Fail(Lang.F("Token {0} ditolak", "Google Calendar"), "no client_id", true);

            var form = new List<string>
            {
                "client_id=" + Uri.EscapeDataString(cid),
                "refresh_token=" + Uri.EscapeDataString(vault.RefreshToken),
                "grant_type=refresh_token"
            };
            if (!string.IsNullOrEmpty(csec))
                form.Add("client_secret=" + Uri.EscapeDataString(csec));

            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/x-www-form-urlencoded" }
            };
            var r = Deskmate.Fetch.Send("POST", TokenEndpoint, headers, string.Join("&", form.ToArray()), Deskmate.Fetch.JsonCap, 20000);
            if (r.Status == 0) return SourceResult.Http("Google Calendar", r);

            object tok = null;
            try { tok = Json.Parse(r.Text); }
            catch (Exception) { }

            if (r.Status == 400 || r.Status == 401)
            {
                string errCode = tok != null ? Json.Str(tok, "error") : "";
                if (r.Status == 401 || errCode == "invalid_grant" || errCode == "invalid_client" || errCode == "unauthorized_client")
                    return SourceResult.Fail(Lang.F("Token {0} ditolak", "Google Calendar"), "oauth " + r.Status + " " + errCode, true);
                return SourceResult.Http("Google Calendar", r);
            }

            if (!r.Ok || tok == null) return SourceResult.Http("Google Calendar", r);

            string newAccess = Json.Str(tok, "access_token");
            if (string.IsNullOrEmpty(newAccess))
                return SourceResult.Fail(Lang.T("Gagal memuat"), "empty access_token", false);

            double expiresIn = Json.Num(tok, "expires_in", 3600);
            string rotatedRefresh = Json.Str(tok, "refresh_token");
            string expectedRefresh = vault.RefreshToken;

            vault.AccessToken = newAccess;
            vault.ExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
            if (!string.IsNullOrEmpty(rotatedRefresh)) vault.RefreshToken = rotatedRefresh;

            lock (GoogleCalVault.Sync)
            {
                var current = GoogleCalVault.LoadLocked();
                if (!current.HasRefreshToken || current.RefreshToken != expectedRefresh)
                    return SourceResult.Fail(Lang.T("Google Calendar dilepas"), "disconnected", true);
                vault.SaveLocked();
            }
            return null;
        }

        static List<ClassSlot> ParseEvents(object root)
        {
            var list = new List<ClassSlot>();
            var today = DateTime.Now.Date;
            foreach (var item in Json.Arr(root, "items"))
            {
                if (Json.Str(item, "status") == "cancelled") continue;
                string id = Json.Str(item, "id");

                var startObj = Json.Obj(item, "start");
                var endObj = Json.Obj(item, "end");
                if (startObj == null) continue;

                string startDtStr = Json.Str(startObj, "dateTime");
                string startDateStr = Json.Str(startObj, "date");

                string title = Clean(Json.Str(item, "summary"), 160);
                if (title.Length == 0) title = Lang.T("Sibuk");

                string rawLocation = Json.Str(item, "location");
                string room = Clean(rawLocation, 80);

                bool online;
                string url = EventLink(item, rawLocation, out online);

                if (startDtStr.Length > 0)
                {
                    DateTimeOffset startOff;
                    if (!DateTimeOffset.TryParse(startDtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out startOff))
                        continue;

                    DateTime startLocal = DateTime.SpecifyKind(startOff.LocalDateTime, DateTimeKind.Local);
                    DateTime endLocal;
                    string endDtStr = endObj != null ? Json.Str(endObj, "dateTime") : "";
                    DateTimeOffset endOff;
                    if (endDtStr.Length > 0 && DateTimeOffset.TryParse(endDtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out endOff))
                        endLocal = DateTime.SpecifyKind(endOff.LocalDateTime, DateTimeKind.Local);
                    else
                        endLocal = startLocal.AddHours(1);

                    if (endLocal < startLocal) endLocal = startLocal;

                    list.Add(new ClassSlot
                    {
                        Id = id,
                        Title = title,
                        Room = room,
                        Url = url,
                        Online = online,
                        AllDay = false,
                        Source = "google",
                        Start = startLocal,
                        End = endLocal
                    });
                }
                else if (startDateStr.Length > 0)
                {
                    DateTime startDay;
                    if (!DateTime.TryParseExact(startDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDay))
                        continue;

                    DateTime endDay = startDay.AddDays(1);
                    string endDateStr = endObj != null ? Json.Str(endObj, "date") : "";
                    DateTime parsedEnd;
                    if (endDateStr.Length > 0 && DateTime.TryParseExact(endDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedEnd) && parsedEnd > startDay)
                        endDay = parsedEnd;

                    for (var d = startDay.Date; d < endDay.Date; d = d.AddDays(1))
                    {
                        if (d < today.AddDays(-61)) continue;
                        if (d > today.AddDays(181)) break;
                        list.Add(new ClassSlot
                        {
                            Id = id,
                            Title = title,
                            Room = room,
                            Url = url,
                            Online = online,
                            AllDay = true,
                            Source = "google",
                            Start = DateTime.SpecifyKind(d, DateTimeKind.Local),
                            End = DateTime.SpecifyKind(d.AddDays(1), DateTimeKind.Local)
                        });
                    }
                }
            }
            return list;
        }

        // htmlLink is the generated record in the Google Calendar UI.
        static string EventLink(object ev, string location, out bool online)
        {
            string url = EventDestination(Json.Str(ev, "hangoutLink"));
            if (url.Length == 0)
            {
                foreach (var point in Json.Arr(Json.Obj(ev, "conferenceData"), "entryPoints"))
                {
                    if (Json.Str(point, "entryPointType") != "video") continue;
                    url = EventDestination(Json.Str(point, "uri"));
                    if (url.Length > 0) break;
                }
            }
            if (url.Length > 0) { online = true; return url; }

            string raw = location.Trim();
            bool singleUrl = true;
            foreach (char c in raw)
                if (char.IsWhiteSpace(c)) { singleUrl = false; break; }
            if (singleUrl) url = EventDestination(raw);
            if (url.Length == 0) url = EventDestination(Json.Str(Json.Obj(ev, "source"), "url"));
            online = HasMeetingHost(location) || HasMeetingHost(url);
            return url;
        }

        static string EventDestination(string raw)
        {
            string url = SafeLink(raw);
            if (url.Length == 0) return "";
            var uri = new Uri(url);
            string host = uri.IdnHost.TrimEnd('.');
            if (host == "calendar.google.com" || host == "calendar.app.google" ||
                ((host == "www.google.com" || host == "google.com") &&
                 (uri.AbsolutePath == "/calendar" || uri.AbsolutePath.StartsWith("/calendar/", StringComparison.Ordinal))))
                return "";
            return url;
        }

        static bool HasMeetingHost(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant();
            return lower.Contains("meet.google.com") || lower.Contains("zoom.us") || lower.Contains("teams.microsoft.com");
        }
    }
}
