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
    class FetchResult
    {
        public int Status, Length;
        public byte[] Data = new byte[0];
        public string RetryAfter = "", ContentType = "";
        public bool TooBig, Revoked, Insecure;
        public bool Ok { get { return Status >= 200 && Status < 300 && !TooBig; } }
        public string Text { get { return Encoding.UTF8.GetString(Data, 0, Length); } }
    }

    // HTTPS only, capped body, manual redirects
    static class Fetch
    {
        public const int JsonCap = 2 * 1024 * 1024;

        static Fetch()
        {
            // TLS 1.2+ only; legacy defaults include SSL3
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288); }
            catch (Exception) { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
        }

        public static FetchResult Send(string method, string url, Dictionary<string, string> headers, string body, int maxBytes, int timeoutMs)
        {
            return SendBytes(method, url, headers, body == null ? null : Encoding.UTF8.GetBytes(body), maxBytes, timeoutMs);
        }

        internal static FetchResult SendBytes(string method, string url, Dictionary<string, string> headers, byte[] body, int maxBytes, int timeoutMs)
        {
            var res = new FetchResult();
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) { res.Insecure = true; return res; }
            for (int hop = 0; hop <= 3; hop++)
            {
                HttpWebResponse resp = null;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(uri);
                    req.Method = method;
                    req.AllowAutoRedirect = false;
                    // Gzip only: .NET misreads zlib deflate
                    req.AutomaticDecompression = DecompressionMethods.GZip;
                    req.Timeout = req.ReadWriteTimeout = timeoutMs;
                    req.UserAgent = "Deskmate/" + Island.AppVersion + " (+https://github.com/movlution/deskmate)";
                    req.ServicePoint.Expect100Continue = false;
                    if (headers != null)
                        foreach (var h in headers)
                        {
                            if (h.Key == "Content-Type") req.ContentType = h.Value;
                            else if (h.Key == "Accept") req.Accept = h.Value;
                            else req.Headers[h.Key] = h.Value;
                        }
                    if (body != null)
                    {
                        var bytes = body;
                        if (req.ContentType == null) req.ContentType = "application/json";
                        req.ContentLength = bytes.Length;
                        using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                    }
                    else if (method != "GET") req.ContentLength = 0;
                    try { resp = (HttpWebResponse)req.GetResponse(); }
                    catch (WebException ex)
                    {
                        resp = ex.Response as HttpWebResponse;
                        if (resp == null) { res.Status = 0; return res; }
                    }
                    res.Status = (int)resp.StatusCode;
                    res.RetryAfter = resp.Headers["Retry-After"] ?? "";
                    res.ContentType = resp.ContentType ?? "";
                    if (res.Status >= 300 && res.Status < 400 && res.Status != 304)
                    {
                        Uri next;
                        string loc = resp.Headers["Location"] ?? "";
                        if (loc.Length == 0 || !Uri.TryCreate(uri, loc, out next)) return res;
                        // Outlook: dead links redirect to errors
                        string l = next.AbsoluteUri.ToLowerInvariant();
                        if (l.Contains("httpcode=404") || l.Contains("errorfe.aspx") || l.Contains("olkerror")) { res.Revoked = true; res.Status = 404; return res; }
                        if (next.Scheme != Uri.UriSchemeHttps) { res.Insecure = true; return res; }
                        // Token or body never crosses origins
                        bool cross = Uri.Compare(uri, next, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0;
                        if (cross && (method != "GET" || (headers != null && headers.ContainsKey("Authorization")))) return res;
                        if (res.Status == 303) { method = "GET"; body = null; }
                        uri = next;
                        continue;
                    }
                    Read(resp, res, maxBytes, timeoutMs * 2);
                    return res;
                }
                catch (Exception) { res.Status = 0; return res; }
                finally { if (resp != null) resp.Close(); }
            }
            res.Status = 0;
            return res;
        }

        // Size cap and an overall deadline
        static void Read(HttpWebResponse resp, FetchResult res, int max, int deadlineMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[32 * 1024];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > max) { res.TooBig = true; break; }
                    ms.Write(buf, 0, n);
                    if (clock.ElapsedMilliseconds > deadlineMs) { res.Status = 0; break; }
                }
                res.Data = ms.GetBuffer();
                res.Length = (int)ms.Length;
            }
        }

        // Seconds from Retry-After, else 0
        public static int RetrySeconds(FetchResult r)
        {
            int s;
            return int.TryParse(r.RetryAfter.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out s) ? Math.Min(s, 3600) : 0;
        }
    }

    class SourceResult
    {
        public List<KuliaTask> Tasks;
        public List<ClassSlot> Events;
        public string Error, Log, Name;
        // Needs the user: token, link, sharing
        public bool Stop;
        public int RetryAfter;
        public TimeSpan Every;
        public object Extra;

        public static SourceResult Fail(string error, string log, bool stop)
        {
            return new SourceResult { Error = error, Log = log, Stop = stop };
        }

        public static SourceResult Http(string who, FetchResult r)
        {
            if (r.Status == 0) return Fail(Lang.F("{0} tidak terjangkau · dicoba lagi otomatis", who), "net", false);
            if (r.TooBig) return Fail(Lang.T("Respons terlalu besar"), "too big", true);
            if (r.Status == 401) return Fail(Lang.F("Token {0} ditolak", who), "http 401", true);
            if (r.Status == 403) return Fail(Lang.T("Tanpa izin akses"), "http 403", true);
            if (r.Status == 429) return new SourceResult { Error = Lang.T("Terlalu banyak permintaan · dicoba lagi otomatis"), Log = "http 429", RetryAfter = Fetch.RetrySeconds(r) };
            if (r.Status >= 500) return new SourceResult { Error = Lang.F("{0} sedang bermasalah · dicoba lagi otomatis", who), Log = "http " + r.Status, RetryAfter = Fetch.RetrySeconds(r) };
            return Fail(Lang.F("{0} menolak permintaan ({1})", who, r.Status), "http " + r.Status, false);
        }
    }

    // A connected task or calendar source
    abstract class Source
    {
        public string Key = "", Name = "";
        // UI-thread state
        public bool Busy, Stopped;
        public string Error;
        // HoldUtc: server asked to wait until
        public DateTime OkUtc = DateTime.MinValue, TriedUtc = DateTime.MinValue, NextUtc = DateTime.MinValue, HoldUtc = DateTime.MinValue;
        public int Failures, Version;
        // Removed from the hub; workers stop
        public volatile bool Gone;
        public List<KuliaTask> Tasks;
        public List<ClassSlot> Events;
        public TimeSpan Every = TimeSpan.FromMinutes(15);

        public abstract object Snapshot();
        // Worker thread; reads the snapshot only
        public abstract SourceResult Fetch(object snap, DateTime nowUtc);
        public virtual string Close(object snap, KuliaTask t) { return Lang.T("Tidak bisa ditandai dari Deskmate"); }
        // New task from a quick note; null when saved
        public virtual string Create(object snap, string text) { return Lang.T("Tidak bisa dibuat dari Deskmate"); }
        // UI thread; true means save config
        public virtual bool Absorb(SourceResult r) { return false; }
        public virtual bool GivesTasks { get { return true; } }
        public virtual bool GivesEvents { get { return false; } }

        // Fmt's set, plus breaks, fillers, tags
        public static string Clean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Bounded work for huge fields
            if (s.Length > 8192) s = s.Substring(0, 8192);
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                if (c == 0x2028 || c == 0x2029) { sb.Append(' '); continue; }
                if (c == 0x034F || c == 0x115F || c == 0x1160 || c == 0x17B4 || c == 0x17B5 || c == 0x3164 || c == 0xFFA0 || (c >= 0x206A && c <= 0x206F) || (c >= 0xFFF9 && c <= 0xFFFB)) continue;
                // Tag characters U+E0000..E007F
                if (c == 0xDB40 && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDC7F) { i++; continue; }
                sb.Append(s[i]);
            }
            string t = Fmt.OneLine(Fmt.StripHidden(sb.ToString()), max);
            // Never end on half a pair
            return t.Length > 0 && char.IsHighSurrogate(t[t.Length - 1]) ? t.Substring(0, t.Length - 1) : t;
        }

        // Printable ASCII only: safe in a header
        public static bool TokenOk(string t, int max)
        {
            if (t.Length < 20 || t.Length > max) return false;
            foreach (char c in t) if (c < 0x21 || c > 0x7E) return false;
            return true;
        }

        // http(s) links only
        public static string SafeLink(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || url.Length > 2000 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out u)) return "";
            return u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp ? u.AbsoluteUri : "";
        }

        // Deadline for a date without time
        public static DateTime EndOfDay(DateTime date)
        {
            return DateTime.SpecifyKind(date.Date.AddHours(23).AddMinutes(59), DateTimeKind.Local).ToUniversalTime();
        }
    }
}
