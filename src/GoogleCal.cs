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
    // Encrypted OAuth credentials and tokens for Google Calendar
    class GoogleCalVault
    {
        public static readonly string FilePath = Path.Combine(Settings.Dir, "googlecal.vault");
        internal static readonly string DefaultClientId = (Environment.GetEnvironmentVariable("DESKMATE_GOOGLE_CLIENT_ID") ?? "").Trim();
        internal static readonly string DefaultClientSecret = (Environment.GetEnvironmentVariable("DESKMATE_GOOGLE_CLIENT_SECRET") ?? "").Trim();
        public static readonly object Sync = new object();

        public string ClientId = "";
        public string ClientSecret = "";
        public string RefreshToken = "";
        public string AccessToken = "";
        public DateTime ExpiresUtc = DateTime.MinValue;
        public string Email = "";

        public string EffectiveClientId
        {
            get { return !string.IsNullOrEmpty(ClientId) ? ClientId : DefaultClientId; }
        }

        public string EffectiveClientSecret
        {
            get { return !string.IsNullOrEmpty(ClientId) ? (ClientSecret ?? "") : DefaultClientSecret; }
        }

        public bool HasRefreshToken
        {
            get { return !string.IsNullOrEmpty(RefreshToken); }
        }

        public static GoogleCalVault Load()
        {
            lock (Sync) { return LoadLocked(); }
        }

        public static GoogleCalVault LoadLocked()
        {
            var v = new GoogleCalVault();
            try
            {
                string raw = Vault.Load(FilePath);
                if (string.IsNullOrEmpty(raw)) return v;
                var o = Json.Parse(raw);
                if (o == null) return v;
                v.ClientId = Json.Str(o, "client_id");
                v.ClientSecret = Json.Str(o, "client_secret");
                v.RefreshToken = Json.Str(o, "refresh_token");
                v.AccessToken = Json.Str(o, "access_token");
                v.ExpiresUtc = Json.Time(o, "expires_utc");
                v.Email = Json.Str(o, "email");
            }
            catch (Exception ex) { Log.Write("googlecal load: " + ex.GetType().Name); }
            return v;
        }

        public void Save()
        {
            lock (Sync) { SaveLocked(); }
        }

        public void SaveLocked()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "client_id", ClientId ?? "" },
                    { "client_secret", ClientSecret ?? "" },
                    { "refresh_token", RefreshToken ?? "" },
                    { "access_token", AccessToken ?? "" },
                    { "expires_utc", ExpiresUtc > DateTime.MinValue ? ExpiresUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : "" },
                    { "email", Email ?? "" },
                };
                Vault.Save(FilePath, Json.Encode(data));
            }
            catch (Exception ex) { Log.Write("googlecal save: " + ex.GetType().Name); }
        }

        public static void Forget()
        {
            lock (Sync) { Vault.Delete(FilePath); }
        }
    }

    // OAuth 2.0 Loopback PKCE flow for Google Calendar
    static class GoogleCalAuth
    {
        const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
        const string Scope = "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/tasks https://www.googleapis.com/auth/drive.file email";
        const int TimeoutMs = 120000;

        static readonly object activeLock = new object();
        static HttpListener activeListener;

        public static string StartLoopbackLogin(string clientId, string clientSecret, out string email, out string refreshToken, out string accessToken, out DateTime expiresUtc)
        {
            email = "";
            refreshToken = "";
            accessToken = "";
            expiresUtc = DateTime.MinValue;

            clientId = (clientId ?? "").Trim();
            clientSecret = (clientSecret ?? "").Trim();
            if (clientId.Length == 0)
            {
                clientId = GoogleCalVault.DefaultClientId;
                if (clientSecret.Length == 0) clientSecret = GoogleCalVault.DefaultClientSecret;
            }
            if (clientId.Length == 0) return Lang.T("Isi Google Client ID di Kredensial OAuth kustom terlebih dahulu.");

            string verifier, challenge;
            CreatePkce(out verifier, out challenge);
            string state = RandomBase64Url(16);

            HttpListener listener = null;
            int port = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                port = FreePort();
                var probe = new HttpListener();
                probe.Prefixes.Add("http://127.0.0.1:" + port + "/");
                try
                {
                    probe.Start();
                    listener = probe;
                    break;
                }
                catch (Exception ex)
                {
                    try { probe.Close(); } catch (Exception) { }
                    if (attempt == 2)
                    {
                        Log.Write("googlecal listener: " + ex.Message);
                        return Lang.T("Gagal terhubung");
                    }
                }
            }

            string redirectUri = "http://127.0.0.1:" + port + "/";
            lock (activeLock)
            {
                if (activeListener != null)
                {
                    try { activeListener.Abort(); } catch (Exception) { }
                }
                activeListener = listener;
            }

            string code = "";
            string oauthError = "";
            try
            {
                string authUrl = AuthEndpoint
                    + "?client_id=" + Uri.EscapeDataString(clientId)
                    + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                    + "&response_type=code"
                    + "&scope=" + Uri.EscapeDataString(Scope)
                    + "&code_challenge=" + Uri.EscapeDataString(challenge)
                    + "&code_challenge_method=S256"
                    + "&access_type=offline"
                    + "&prompt=consent"
                    + "&state=" + Uri.EscapeDataString(state);

                try
                {
                    Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Write("googlecal browser: " + ex.Message);
                    return Lang.T("Gagal membuka browser");
                }

                var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
                while (true)
                {
                    int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0) return Lang.T("Waktu masuk habis");

                    IAsyncResult ar;
                    try { ar = listener.BeginGetContext(null, null); }
                    catch (Exception) { return Lang.T("Gagal terhubung"); }

                    if (!ar.AsyncWaitHandle.WaitOne(remaining))
                        return Lang.T("Waktu masuk habis");

                    HttpListenerContext ctx;
                    try { ctx = listener.EndGetContext(ar); }
                    catch (Exception) { return Lang.T("Login dibatalkan"); }

                    string path = ctx.Request.Url != null ? ctx.Request.Url.AbsolutePath : "/";
                    if (path != "/")
                    {
                        WriteResponse(ctx.Response, 404, "text/plain; charset=utf-8", "Not found");
                        continue;
                    }

                    NameValueCollection qs = ctx.Request.QueryString;
                    string gotState = qs["state"] ?? "";
                    string gotCode = qs["code"] ?? "";
                    string gotErr = qs["error"] ?? "";

                    if (gotCode.Length == 0 && gotErr.Length == 0)
                    {
                        WriteResponse(ctx.Response, 400, "text/plain; charset=utf-8", "Missing code");
                        continue;
                    }

                    if (!string.Equals(gotState, state, StringComparison.Ordinal))
                    {
                        WriteResponse(ctx.Response, 400, "text/html; charset=utf-8", HtmlPage("Permintaan tidak valid.", false));
                        return Lang.T("Gagal terhubung");
                    }

                    if (gotErr.Length > 0)
                    {
                        oauthError = gotErr;
                        WriteResponse(ctx.Response, 200, "text/html; charset=utf-8", HtmlPage("Otorisasi dibatalkan (" + HtmlEscape(gotErr) + "). Tab ini boleh ditutup.", false));
                        break;
                    }

                    code = gotCode;
                    WriteResponse(ctx.Response, 200, "text/html; charset=utf-8", HtmlPage("Deskmate terhubung ke Google Calendar! Kamu bisa menutup tab ini dan kembali ke Deskmate.", true));
                    break;
                }
            }
            finally
            {
                lock (activeLock)
                {
                    if (activeListener == listener) activeListener = null;
                }
                try { listener.Close(); } catch (Exception) { }
            }

            if (oauthError.Length > 0)
            {
                Log.Write("googlecal oauth error: " + oauthError);
                return oauthError == "access_denied" ? Lang.T("Tanpa izin akses") : Lang.T("Gagal terhubung") + " (" + oauthError + ")";
            }

            var form = new List<string>
            {
                "code=" + Uri.EscapeDataString(code),
                "client_id=" + Uri.EscapeDataString(clientId),
                "redirect_uri=" + Uri.EscapeDataString(redirectUri),
                "grant_type=authorization_code",
                "code_verifier=" + Uri.EscapeDataString(verifier)
            };
            if (clientSecret.Length > 0) form.Add("client_secret=" + Uri.EscapeDataString(clientSecret));

            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/x-www-form-urlencoded" }
            };
            var tokRes = Fetch.Send("POST", TokenEndpoint, headers, string.Join("&", form.ToArray()), Fetch.JsonCap, 20000);
            if (tokRes.Status == 0) return Lang.T("Gagal terhubung");

            object tokJson = null;
            try { tokJson = Json.Parse(tokRes.Text); }
            catch (Exception) { }

            if (!tokRes.Ok || tokJson == null)
            {
                string errDesc = tokJson != null ? (Json.Str(tokJson, "error_description") + " " + Json.Str(tokJson, "error")).Trim() : "";
                Log.Write("googlecal token http " + tokRes.Status + (errDesc.Length > 0 ? ": " + errDesc : ""));
                return Lang.T("Gagal menukar token Google");
            }

            accessToken = Json.Str(tokJson, "access_token");
            refreshToken = Json.Str(tokJson, "refresh_token");
            double expiresIn = Json.Num(tokJson, "expires_in", 3600);
            if (accessToken.Length == 0 || refreshToken.Length == 0)
            {
                Log.Write("googlecal token missing access/refresh");
                return Lang.T("Token Google tidak lengkap");
            }
            expiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));

            // Optional userinfo fetch for connected email label
            try
            {
                var uiRes = Fetch.Send("GET", UserInfoEndpoint, new Dictionary<string, string> { { "Authorization", "Bearer " + accessToken } }, null, Fetch.JsonCap, 15000);
                if (uiRes.Ok)
                {
                    var uiJson = Json.Parse(uiRes.Text);
                    if (uiJson != null) email = Source.Clean(Json.Str(uiJson, "email"), 120);
                }
            }
            catch (Exception ex) { Log.Write("googlecal userinfo: " + ex.GetType().Name); }

            bool usingDefault = string.Equals(clientId, GoogleCalVault.DefaultClientId, StringComparison.Ordinal);
            var vault = new GoogleCalVault
            {
                ClientId = usingDefault ? "" : clientId,
                ClientSecret = usingDefault ? "" : clientSecret,
                RefreshToken = refreshToken,
                AccessToken = accessToken,
                ExpiresUtc = expiresUtc,
                Email = email
            };
            vault.Save();
            return null;
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        static string RandomBase64Url(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        static void CreatePkce(out string verifier, out string challenge)
        {
            verifier = RandomBase64Url(32);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(verifier));
                challenge = Base64Url(hash);
            }
        }

        static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static void WriteResponse(HttpListenerResponse resp, int status, string contentType, string body)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                resp.StatusCode = status;
                resp.ContentType = contentType;
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
                resp.Close();
            }
            catch (Exception) { }
        }

        static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static string HtmlPage(string message, bool ok)
        {
            string badge = ok ? "#22c55e" : "#ef4444";
            return "<!doctype html><html><head><meta charset='utf-8'><title>Deskmate</title></head>"
                + "<body style='margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#111215;color:#f4f4f5;font-family:system-ui,-apple-system,Segoe UI,sans-serif'>"
                + "<div style='max-width:420px;padding:28px 32px;border-radius:16px;background:#1b1d22;border:1px solid #2b2e36;text-align:center'>"
                + "<div style='width:12px;height:12px;border-radius:50%;background:" + badge + ";margin:0 auto 14px'></div>"
                + "<p style='margin:0;font-size:16px;line-height:1.5'>" + message + "</p>"
                + "</div></body></html>";
        }
    }

    // Google Calendar primary events via OAuth 2.0
    class GoogleCalendarSource : Source
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
                error = Lang.T("Izin tulis Google Calendar belum aktif — putuskan lalu sambungkan ulang di Pengaturan › Integrasi.");
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
                error = Lang.T("Izin tulis Google Calendar belum aktif — putuskan lalu sambungkan ulang di Pengaturan › Integrasi.");
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
