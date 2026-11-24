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
}
